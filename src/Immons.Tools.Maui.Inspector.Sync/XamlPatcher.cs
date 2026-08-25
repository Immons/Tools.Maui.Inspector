using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Immons.Tools.Maui.Inspector.Sync;

/// <summary>
/// A single edit reported by the app. Op selects the kind: "attr" patches an attribute in
/// place, "insert" adds a new child element under the anchor (the parent's opening tag),
/// "remove-el" deletes the element at the anchor. For structural ops Attribute carries the
/// operation id and Remove flips the direction (cancel an insert / restore a removal).
/// </summary>
public sealed record XamlChange(string Source, int Line, int Column, string Element, string Attribute, string Value, bool Remove = false, string Op = "attr");

/// <summary>
/// Applies changes to XAML files by plain-text edits (no reformatting): finds the tag at the
/// reported line/column, verifies the element name matches, then patches attributes or
/// inserts/removes whole elements. Reported locations are build-time; a per-file map of line
/// shifts caused by this session's own edits keeps later changes on target.
/// </summary>
public sealed class XamlPatcher
{
    readonly string _root;
    readonly bool _dryRun;
    readonly Dictionary<string, string?> _resolvedFiles = [];
    readonly HashSet<string> _appliedKeys = [];
    readonly Dictionary<string, FileState> _states = [];

    /// <summary>Per-file bookkeeping for this session's structural edits.</summary>
    sealed class FileState
    {
        /// <summary>The file as first read this session — the text build-time locations refer to.</summary>
        public string? OriginalText;

        /// <summary>
        /// Every text mutation this session, in application order, each in the coordinates of
        /// the file at its time: [Start, Start+Removed) replaced by Inserted characters.
        /// A removal carrying RemoveId "captures" positions inside its range; a later edit
        /// with a matching RestoreOf drops them back in at the new location — this is how
        /// moved/restored elements (and their children) stay addressable.
        /// </summary>
        public List<(int Start, int Removed, int Inserted, string? RemoveId, string? RestoreOf, string? RestoredBlock, int ReindentDelta)> Edits { get; } = [];

        /// <summary>
        /// Spans inserted by op id — replaced on upsert, undone on cancel. ReplacedText is what
        /// the span overwrote ("" for pure insertions, the original "/>" tail when a self-closing
        /// parent was expanded) and is put back when the span is dropped.
        /// </summary>
        public Dictionary<string, (int Offset, int Length, string ReplacedText)> Inserts { get; } = [];

        /// <summary>Text removed by op id — put back verbatim on restore.</summary>
        public Dictionary<string, (int Offset, string Text)> Removed { get; } = [];

        /// <summary>
        /// Wrapper tag pairs by op id: the opening tag span, the closing tag span and the
        /// indentation the wrapper carries. Upserts rewrite the opening tag; cancel strips
        /// both tags and re-indents the content back.
        /// </summary>
        public Dictionary<string, (int OpenOffset, int OpenLength, int CloseOffset, int CloseLength, string Indent)> Wraps { get; } = [];

        /// <summary>
        /// Containers stripped by unwrap, by op id: where their promoted content now lives, the
        /// removed opening/closing tag text and the content's original indentation — enough to
        /// put the container back on undo. PromotedLength tracks edits landing inside.
        /// </summary>
        public Dictionary<string, (int Offset, int PromotedLength, string OpenText, string CloseText, string ContentIndent)> Unwrapped { get; } = [];

        /// <summary>
        /// Registers one mutation and updates every tracked span accordingly. restoredBlock and
        /// reindentDelta describe a restore whose text was re-indented on the way back in, so
        /// captured interior positions can be corrected line by line.
        /// </summary>
        public void RecordEdit(int start, int removedLength, int insertedLength, string? removeId = null, string? restoreOf = null, string? restoredBlock = null, int reindentDelta = 0)
        {
            Edits.Add((start, removedLength, insertedLength, removeId, restoreOf, restoredBlock, reindentDelta));

            var delta = insertedLength - removedLength;
            if (delta == 0)
                return;
            var rangeEnd = start + removedLength;
            foreach (var (key, span) in Inserts.ToList())
            {
                if (span.Offset >= rangeEnd)
                    Inserts[key] = (span.Offset + delta, span.Length, span.ReplacedText);
            }
            foreach (var (key, span) in Removed.ToList())
            {
                if (span.Offset >= rangeEnd)
                    Removed[key] = (span.Offset + delta, span.Text);
            }
            foreach (var (key, wrap) in Wraps.ToList())
            {
                var moved = wrap;
                if (moved.OpenOffset >= rangeEnd)
                    moved.OpenOffset += delta;
                if (moved.CloseOffset >= rangeEnd)
                    moved.CloseOffset += delta;
                Wraps[key] = moved;
            }
            foreach (var (key, unwrapped) in Unwrapped.ToList())
            {
                var moved = unwrapped;
                if (start >= moved.Offset && start < moved.Offset + moved.PromotedLength)
                    moved.PromotedLength += delta; // edit inside the promoted block
                else if (moved.Offset >= rangeEnd)
                    moved.Offset += delta;
                Unwrapped[key] = moved;
            }
        }

        /// <summary>
        /// Maps an offset in the original text to the current text. -1 when the position sits
        /// inside a removed (and not restored) range.
        /// </summary>
        public int MapOffset(int originalOffset)
        {
            var position = originalOffset;
            string? capturedBy = null;
            var interior = 0;
            foreach (var (start, removed, inserted, removeId, restoreOf, restoredBlock, reindentDelta) in Edits)
            {
                if (capturedBy != null)
                {
                    if (restoreOf == capturedBy)
                    {
                        var corrected = interior;
                        if (restoredBlock != null && reindentDelta != 0)
                        {
                            var newlines = 0;
                            for (var i = 0; i < interior && i < restoredBlock.Length; i++)
                            {
                                if (restoredBlock[i] == '\n')
                                    newlines++;
                            }
                            corrected += reindentDelta * (newlines + 1);
                        }
                        position = start + corrected;
                        capturedBy = null;
                    }
                    continue;
                }

                if (position >= start + removed)
                {
                    position += inserted - removed;
                }
                else if (position > start)
                {
                    if (removeId != null)
                    {
                        capturedBy = removeId;
                        interior = position - start;
                    }
                    else
                    {
                        position = start;
                    }
                }
            }
            return capturedBy != null ? -1 : position;
        }
    }

    public XamlPatcher(string root, bool dryRun)
    {
        _root = root;
        _dryRun = dryRun;
    }

    /// <summary>Applies one change; the outcome feeds the ack POSTed back to the app.</summary>
    public (bool Ok, string Message) Apply(XamlChange change)
    {
        // "Views/Foo.xaml;assembly=MyApp" → relative path
        var relativePath = change.Source.Split(';')[0].TrimStart('/');
        var key = $"{relativePath}:{change.Line}:{change.Column}|{change.Op}|{change.Attribute}={(change.Remove ? "\0removed" : change.Value)}";
        if (!_appliedKeys.Add(key))
            return (true, "already applied"); // same value already applied this session

        var file = ResolveFile(relativePath);
        if (file == null)
        {
            return Fail($"{relativePath}: file not found under {_root}");
        }

        string text;
        try
        {
            text = File.ReadAllText(file);
        }
        catch (Exception ex)
        {
            return Fail($"{relativePath}: {ex.Message}");
        }

        var state = _states.TryGetValue(file, out var existing) ? existing : _states[file] = new FileState();
        state.OriginalText ??= text;

        // "{inspector:Adaptive …}" from the panel's ⋔ editor: the placeholder prefix becomes
        // whatever prefix this file declares for the Extensions namespace (declared on the
        // root when missing), so the expression compiles.
        if (!change.Remove && change.Value.Contains("{inspector:", StringComparison.Ordinal))
        {
            var ensured = EnsureXmlnsForNamespace(text,
                "Immons.Tools.Maui.Inspector.Extensions", "Immons.Tools.Maui.Inspector.Extensions",
                out var extensionsPrefix, out var xmlnsMessage, out var xmlnsAt);
            if (ensured == null)
                return Fail($"{relativePath}: {xmlnsMessage}");
            if (!ReferenceEquals(ensured, text))
                state.RecordEdit(xmlnsAt, 0, ensured.Length - text.Length);
            text = ensured;
            change = change with { Value = change.Value.Replace("{inspector:", "{" + extensionsPrefix + ":") };
        }

        string? patched;
        string message;
        string report;
        switch (change.Op)
        {
            case "insert":
                patched = ApplyInsert(text, state, change, out message, out report);
                break;
            case "remove-el":
                patched = ApplyElementRemove(text, state, change, out message, out report);
                break;
            case "move-el":
                patched = ApplyElementMove(text, state, change, out message, out report);
                break;
            case "wrap-el":
                patched = ApplyElementWrap(text, state, change, out message, out report);
                break;
            case "unwrap-el":
                patched = ApplyElementUnwrap(text, state, change, out message, out report);
                break;
            case "style-res":
                patched = ApplyStyleResource(text, state, change, out message, out report);
                break;
            case "setter":
                patched = ApplyStyleSetter(text, state, change, out message, out report);
                break;
            case "res-val":
                patched = ApplyResourceValue(text, state, change, out message, out report);
                break;
            default:
                patched = PatchAttribute(text, state, change, out message);
                report = $"{change.Element}.{change.Attribute} {(change.Remove ? "removed" : $"= \"{change.Value}\"")}";
                break;
        }

        if (patched == null)
            return Fail($"{relativePath}:{change.Line} {message}");

        if (patched == text)
        {
            Info($"{relativePath}:{change.Line} {report} — already applied");
            return (true, "already applied");
        }

        if (!_dryRun)
        {
            try
            {
                File.WriteAllText(file, patched);
            }
            catch (Exception ex)
            {
                return Fail($"{relativePath}: write failed: {ex.Message}");
            }
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"✔ {relativePath}:{change.Line}  {report}{(_dryRun ? "  (dry run)" : "")}");
        Console.ResetColor();
        return (true, report);
    }

    /// <summary>Build-time anchor → current offset through this session's edit history.</summary>
    static int ResolveAnchor(string text, FileState state, int line, int column)
    {
        _ = text;
        var original = OffsetOf(state.OriginalText ?? text, line, column);
        return original < 0 ? -1 : state.MapOffset(original);
    }

    // ---- attributes ------------------------------------------------------------------------

    string? PatchAttribute(string text, FileState state, XamlChange change, out string message)
    {
        var offset = ResolveAnchor(text, state, change.Line, change.Column);
        var patched = Patch(text, change, offset, out message);
        if (patched == null || patched == text)
            return patched;

        // The edit stays inside the opening tag: model it just past the tag's anchor so the
        // anchor itself keeps its position while everything behind the tag moves.
        state.RecordEdit(offset + 1, 0, patched.Length - text.Length);
        return patched;
    }

    /// <summary>Returns the patched text, null with a message when the change cannot be applied safely.</summary>
    internal static string? Patch(string text, XamlChange change, int offset, out string message)
    {
        message = "";

        if (offset < 0)
        {
            message = "line/column out of range (file changed since the app was built? restart the app)";
            return null;
        }

        // Line info points at the element name (just after '<'). Verify it to catch stale locations.
        var nameMatch = Regex.Match(text[offset..Math.Min(text.Length, offset + 160)],
            @"^([A-Za-z_][\w]*:)?([A-Za-z_][\w.]*)");
        if (!nameMatch.Success || nameMatch.Groups[2].Value != change.Element)
        {
            message = $"expected <{change.Element}> here but found \"{Snippet(text, offset)}\" — restart the app after editing XAML by hand";
            return null;
        }

        var tagEnd = XamlTagScanner.FindTagEnd(text, offset);
        if (tagEnd < 0)
        {
            message = "could not find the end of the opening tag";
            return null;
        }

        var tag = text[offset..tagEnd];
        var value = EscapeAttributeValue(change.Value);
        var attrPattern = new Regex($@"(\s{Regex.Escape(change.Attribute)}\s*=\s*)(""[^""]*""|'[^']*')");

        if (change.Remove)
        {
            // Attribute on its own line disappears with the whole line; inline only with its spacing.
            var removal = Regex.Replace(tag,
                $@"(\r?\n[ \t]*|[ \t]+){Regex.Escape(change.Attribute)}\s*=\s*(""[^""]*""|'[^']*')", "");
            return text[..offset] + removal + text[tagEnd..];
        }

        string newTag;
        var existing = attrPattern.Match(tag);
        if (existing.Success)
        {
            var quote = existing.Groups[2].Value[0];
            newTag = tag[..existing.Groups[2].Index] + quote + value + quote
                     + tag[(existing.Groups[2].Index + existing.Groups[2].Length)..];
        }
        else
        {
            // Insert right after the element name — always safe, keeps the tag's own formatting.
            var nameLength = nameMatch.Length;
            newTag = tag[..nameLength] + $" {change.Attribute}=\"{value}\"" + tag[nameLength..];
        }

        return text[..offset] + newTag + text[tagEnd..];
    }

    // ---- element insert --------------------------------------------------------------------

    string? ApplyInsert(string text, FileState state, XamlChange change, out string message, out string report)
    {
        message = "";
        report = "";
        var opId = change.Attribute;

        if (change.Remove)
        {
            // Take a previously applied insert back out (element removed / add undone).
            if (!state.Inserts.TryGetValue(opId, out var span))
            {
                report = "insert not applied in this session — nothing to take back";
                return text;
            }
            var result = DropInsertSpan(text, state, opId, span);
            report = "inserted element taken back out";
            return result;
        }

        // Upsert: drop the previous version of this insert, then place the fresh snippet.
        if (state.Inserts.TryGetValue(opId, out var previous))
            text = DropInsertSpan(text, state, opId, previous);

        JsonObject? payload;
        try
        {
            payload = JsonNode.Parse(change.Value) as JsonObject;
        }
        catch
        {
            payload = null;
        }
        if (payload == null)
        {
            message = "unreadable insert payload";
            return null;
        }

        var typeName = payload["type"]?.GetValue<string>() ?? "";
        var assembly = payload["asm"]?.GetValue<string>() ?? "";
        var shortName = payload["name"]?.GetValue<string>() ?? "";

        // Custom controls need an xmlns on the root element; built-ins use the default namespace.
        var tagName = shortName;
        if (!typeName.StartsWith("Microsoft.Maui.Controls", StringComparison.Ordinal))
        {
            var ensured = EnsureXmlns(text, typeName, assembly, out var prefix, out message, out var xmlnsAt);
            if (ensured == null)
                return null;
            if (!ReferenceEquals(ensured, text))
                state.RecordEdit(xmlnsAt, 0, ensured.Length - text.Length);
            text = ensured;
            tagName = $"{prefix}:{shortName}";
        }

        // Custom controls nested in the snippet arrive with placeholder prefixes plus a map of
        // their namespaces — declare each on the root element and rewrite the placeholders to
        // the prefixes the file actually uses.
        var childrenXml = payload["childrenXml"]?.GetValue<string>();
        if (childrenXml != null && payload["xmlns"] is JsonObject xmlnsMap)
        {
            foreach (var (localPrefix, declarationNode) in xmlnsMap)
            {
                var declaration = declarationNode?.GetValue<string>() ?? "";
                var match = Regex.Match(declaration, @"^clr-namespace:([^;]+);assembly=(.+)$");
                if (!match.Success)
                    continue;

                var ensured = EnsureXmlnsForNamespace(text, match.Groups[1].Value, match.Groups[2].Value,
                    out var actualPrefix, out message, out var xmlnsAt);
                if (ensured == null)
                    return null;
                if (!ReferenceEquals(ensured, text))
                    state.RecordEdit(xmlnsAt, 0, ensured.Length - text.Length);
                text = ensured;

                if (actualPrefix != localPrefix)
                    childrenXml = childrenXml
                        .Replace($"<{localPrefix}:", $"<{actualPrefix}:")
                        .Replace($"</{localPrefix}:", $"</{actualPrefix}:");
            }
        }

        var offset = ResolveAnchor(text, state, change.Line, change.Column);
        if (offset < 0)
        {
            message = "line/column out of range (file changed since the app was built? restart the app)";
            return null;
        }

        var parentQName = XamlTagScanner.ReadQName(text, offset);
        if (XamlTagScanner.LocalNameOf(parentQName) != change.Element)
        {
            message = $"expected parent <{change.Element}> here but found \"{Snippet(text, offset)}\" — restart the app after editing XAML by hand";
            return null;
        }

        var parentIndent = IndentOfLine(text, offset);
        var childIndent = parentIndent + (parentIndent.Contains('\t') ? "\t" : "    ");
        var snippet = RenderSnippet(tagName, payload["attrs"] as JsonObject, childrenXml, childIndent);

        var tagEnd = XamlTagScanner.FindTagEnd(text, offset);
        if (tagEnd < 0)
        {
            message = "could not find the end of the parent's opening tag";
            return null;
        }

        string patched;
        int insertOffset;
        string inserted;
        string replacedText;
        if (XamlTagScanner.IsSelfClosing(text, tagEnd))
        {
            // <Grid ... /> → <Grid ...>\n    <child />\n</Grid>
            var slash = text.LastIndexOf('/', tagEnd - 1);
            inserted = ">\n" + childIndent + snippet + "\n" + parentIndent + "</" + parentQName + ">";
            insertOffset = slash;
            replacedText = text[slash..tagEnd];
            patched = text[..slash] + inserted + text[tagEnd..];
        }
        else if (ResolveSiblingAnchor(text, state, payload) is { } anchored)
        {
            // Positioned insert: right before/after the sibling the element sits next to.
            inserted = childIndent + snippet + "\n";
            insertOffset = anchored;
            replacedText = "";
            patched = text.Insert(insertOffset, inserted);
        }
        else
        {
            var (closeStart, _) = XamlTagScanner.FindClosingTag(text, offset, change.Element);
            if (closeStart < 0)
            {
                message = $"could not find </{change.Element}> for the insert";
                return null;
            }

            var lineStart = LineStartOf(text, closeStart);
            var closingOnOwnLine = text[lineStart..closeStart].Trim().Length == 0;
            if (closingOnOwnLine)
            {
                inserted = childIndent + snippet + "\n";
                insertOffset = lineStart;
            }
            else
            {
                inserted = snippet;
                insertOffset = closeStart;
            }
            replacedText = "";
            patched = text.Insert(insertOffset, inserted);
        }

        state.RecordEdit(insertOffset, replacedText.Length, inserted.Length);
        state.Inserts[opId] = (insertOffset, inserted.Length, replacedText);

        report = $"<{tagName}> inserted into <{change.Element}>";
        return patched;
    }

    // ---- element remove --------------------------------------------------------------------

    string? ApplyElementRemove(string text, FileState state, XamlChange change, out string message, out string report)
    {
        message = "";
        report = "";
        var opId = change.Attribute;

        if (!change.Remove)
        {
            // Restore: put the exact removed text back.
            if (!state.Removed.TryGetValue(opId, out var removed))
            {
                Warn("nothing recorded to restore (removed in an earlier session?) — restore the element from source control");
                report = "restore skipped";
                return text;
            }
            var patchedText = text.Insert(removed.Offset, removed.Text);
            state.Removed.Remove(opId);
            state.RecordEdit(removed.Offset, 0, removed.Text.Length, restoreOf: opId);
            report = $"<{change.Element}> restored";
            return patchedText;
        }

        var offset = ResolveAnchor(text, state, change.Line, change.Column);
        if (offset < 0)
        {
            message = "line/column out of range (file changed since the app was built? restart the app)";
            return null;
        }

        var qname = XamlTagScanner.ReadQName(text, offset);
        if (XamlTagScanner.LocalNameOf(qname) != change.Element)
        {
            message = $"expected <{change.Element}> here but found \"{Snippet(text, offset)}\" — restart the app after editing XAML by hand";
            return null;
        }

        if (ElementSpan(text, offset, change.Element, out message) is not { } span)
            return null;
        var (spanStart, spanEnd) = span;

        var removedText = text[spanStart..spanEnd];
        var result = text.Remove(spanStart, removedText.Length);
        state.RecordEdit(spanStart, removedText.Length, 0, removeId: opId);
        state.Removed[opId] = (spanStart, removedText);

        report = $"<{change.Element}> removed";
        return result;
    }

    /// <summary>
    /// Insertion offset next to the payload's sibling anchor: another pending insert (by op id)
    /// or a source-backed element (by build-time location). Null = no/unusable anchor, the
    /// caller falls back to the parent's end.
    /// </summary>
    int? ResolveSiblingAnchor(string text, FileState state, JsonObject payload)
    {
        var before = payload["before"]?.GetValue<bool>() ?? false;

        if (payload["anchorOp"]?.GetValue<string>() is { } anchorOp)
        {
            // A parent-expansion span wraps the whole parent — anchoring next to it would land
            // outside the parent, so only plain sibling spans qualify.
            if (state.Inserts.TryGetValue(anchorOp, out var span) && span.ReplacedText.Length == 0)
                return before ? span.Offset : span.Offset + span.Length;
            return null;
        }

        if (payload["sibLine"]?.GetValue<int>() is not { } sibLine)
            return null;

        var sibColumn = payload["sibColumn"]?.GetValue<int>() ?? 0;
        var sibElement = payload["sibElement"]?.GetValue<string>() ?? "";
        var sibOffset = ResolveAnchor(text, state, sibLine, sibColumn);
        if (sibOffset < 0
            || XamlTagScanner.LocalNameOf(XamlTagScanner.ReadQName(text, sibOffset)) != sibElement
            || ElementSpan(text, sibOffset, sibElement, out _) is not { } span2)
            return null;

        return before ? span2.Start : span2.End;
    }

    // ---- element move ----------------------------------------------------------------------

    string? ApplyElementMove(string text, FileState state, XamlChange change, out string message, out string report)
    {
        message = "";
        report = "";
        var opId = change.Attribute;

        JsonObject? payload;
        try
        {
            payload = JsonNode.Parse(change.Value) as JsonObject;
        }
        catch
        {
            payload = null;
        }
        if (payload == null)
        {
            message = "unreadable move payload";
            return null;
        }

        var sibLine = payload["sibLine"]?.GetValue<int>() ?? 0;
        var sibColumn = payload["sibColumn"]?.GetValue<int>() ?? 0;
        var sibElement = payload["sibElement"]?.GetValue<string>() ?? "";
        var parLine = payload["parLine"]?.GetValue<int>() ?? 0;
        var parColumn = payload["parColumn"]?.GetValue<int>() ?? 0;
        var parElement = payload["parElement"]?.GetValue<string>() ?? "";
        var before = payload["before"]?.GetValue<bool>() ?? false;

        var offset = ResolveAnchor(text, state, change.Line, change.Column);
        var sibOffset = sibLine > 0 ? ResolveAnchor(text, state, sibLine, sibColumn) : -1;
        var parOffset = parLine > 0 ? ResolveAnchor(text, state, parLine, parColumn) : -1;
        if (offset < 0 || (sibOffset < 0 && parOffset < 0))
        {
            message = "line/column out of range (file changed since the app was built? restart the app)";
            return null;
        }

        if (XamlTagScanner.LocalNameOf(XamlTagScanner.ReadQName(text, offset)) != change.Element)
        {
            message = $"expected <{change.Element}> here but found \"{Snippet(text, offset)}\" — restart the app after editing XAML by hand";
            return null;
        }
        if (sibOffset >= 0 && XamlTagScanner.LocalNameOf(XamlTagScanner.ReadQName(text, sibOffset)) != sibElement)
            sibOffset = -1; // stale sibling — fall back to the parent anchor when present
        if (parOffset >= 0 && XamlTagScanner.LocalNameOf(XamlTagScanner.ReadQName(text, parOffset)) != parElement)
            parOffset = -1;
        if (sibOffset < 0 && parOffset < 0)
        {
            message = "neither the sibling nor the parent anchor matches — restart the app after editing XAML by hand";
            return null;
        }

        if (ElementSpan(text, offset, change.Element, out message) is not { } span)
            return null;
        var (spanStart, spanEnd) = span;
        var movedText = text[spanStart..spanEnd];

        // Cut the element, then find the anchors again on the shortened text.
        var cut = text.Remove(spanStart, movedText.Length);
        state.RecordEdit(spanStart, movedText.Length, 0, removeId: opId);
        if (sibOffset > spanStart)
            sibOffset -= movedText.Length;
        if (parOffset > spanStart)
            parOffset -= movedText.Length;

        int insertOffset;
        string targetIndent;
        string prologue = "";
        string epilogue = "";
        if (sibOffset >= 0)
        {
            if (ElementSpan(cut, sibOffset, sibElement, out message) is not { } sibSpan)
                return null;
            insertOffset = before ? sibSpan.Start : sibSpan.End;
            targetIndent = IndentOfLine(cut, sibOffset);
            report = $"<{change.Element}> moved {(before ? "before" : "after")} <{sibElement}>";
        }
        else
        {
            // Into the parent: before its closing tag, expanding a self-closing tag if needed.
            var parentQName = XamlTagScanner.ReadQName(cut, parOffset);
            var parentIndent = IndentOfLine(cut, parOffset);
            targetIndent = parentIndent + (parentIndent.Contains('\t') ? "\t" : "    ");
            var parentTagEnd = XamlTagScanner.FindTagEnd(cut, parOffset);
            if (parentTagEnd < 0)
            {
                message = "could not find the end of the target parent's opening tag";
                return null;
            }

            if (XamlTagScanner.IsSelfClosing(cut, parentTagEnd))
            {
                var slash = cut.LastIndexOf('/', parentTagEnd - 1);
                var expansion = ">\n" + parentIndent + "</" + parentQName + ">";
                cut = cut[..slash] + expansion + cut[parentTagEnd..];
                state.RecordEdit(slash, parentTagEnd - slash, expansion.Length);
                insertOffset = slash + 2; // right after ">\n"
            }
            else
            {
                var (closeStart, _) = XamlTagScanner.FindClosingTag(cut, parOffset, parElement);
                if (closeStart < 0)
                {
                    message = $"could not find </{parElement}> of the target parent";
                    return null;
                }
                var lineStart = LineStartOf(cut, closeStart);
                insertOffset = cut[lineStart..closeStart].Trim().Length == 0 ? lineStart : closeStart;
            }
            report = $"<{change.Element}> moved into <{parElement}>";
        }

        // The span travels with its subtree — align its indentation with the new home.
        var originalMoved = movedText;
        var firstContent = movedText.TrimStart(' ', '\t');
        var currentIndent = movedText[..(movedText.Length - firstContent.Length)];
        movedText = Reindent(movedText, targetIndent);
        var patched = cut.Insert(insertOffset, movedText);
        state.RecordEdit(insertOffset, 0, movedText.Length, restoreOf: opId,
            restoredBlock: originalMoved, reindentDelta: targetIndent.Length - currentIndent.Length);

        return patched;
    }

    // ---- element wrap ----------------------------------------------------------------------

    string? ApplyElementWrap(string text, FileState state, XamlChange change, out string message, out string report)
    {
        message = "";
        report = "";
        var opId = change.Attribute;

        if (change.Remove)
        {
            // Strip the wrapper back off; the content returns to its previous indentation.
            if (!state.Wraps.TryGetValue(opId, out var wrap))
            {
                report = "wrap not applied in this session — nothing to strip";
                return text;
            }

            var contentStart = wrap.OpenOffset + wrap.OpenLength;
            var content = text[contentStart..wrap.CloseOffset];
            var unindented = Reindent(content, wrap.Indent);
            var firstContent = content.TrimStart(' ', '\t');
            var innerIndent = content[..(content.Length - firstContent.Length)];

            // Order matters: each edit's coordinates describe the file at its own time.
            var result = text.Remove(wrap.CloseOffset, wrap.CloseLength);
            state.RecordEdit(wrap.CloseOffset, wrap.CloseLength, 0);
            result = result.Remove(contentStart, content.Length).Insert(contentStart, unindented);
            state.RecordEdit(contentStart, content.Length, 0, removeId: "u:" + opId);
            state.RecordEdit(contentStart, 0, unindented.Length, restoreOf: "u:" + opId,
                restoredBlock: content, reindentDelta: wrap.Indent.Length - innerIndent.Length);
            result = result.Remove(wrap.OpenOffset, wrap.OpenLength);
            state.RecordEdit(wrap.OpenOffset, wrap.OpenLength, 0);
            state.Wraps.Remove(opId);

            report = "wrapper stripped, content kept";
            return result;
        }

        JsonObject? payload;
        try
        {
            payload = JsonNode.Parse(change.Value) as JsonObject;
        }
        catch
        {
            payload = null;
        }
        if (payload == null)
        {
            message = "unreadable wrap payload";
            return null;
        }

        var typeName = payload["type"]?.GetValue<string>() ?? "";
        var assembly = payload["asm"]?.GetValue<string>() ?? "";
        var shortName = payload["name"]?.GetValue<string>() ?? "";

        var tagName = shortName;
        if (!typeName.StartsWith("Microsoft.Maui.Controls", StringComparison.Ordinal))
        {
            var ensured = EnsureXmlns(text, typeName, assembly, out var prefix, out message, out var xmlnsAt);
            if (ensured == null)
                return null;
            if (!ReferenceEquals(ensured, text))
                state.RecordEdit(xmlnsAt, 0, ensured.Length - text.Length);
            text = ensured;
            tagName = $"{prefix}:{shortName}";
        }

        var openTag = RenderOpenTag(tagName, payload["attrs"] as JsonObject);

        // Upsert: only the opening tag changes when the wrapper's attributes are edited.
        if (state.Wraps.TryGetValue(opId, out var existing))
        {
            var newOpen = existing.Indent + openTag + "\n";
            var result = text.Remove(existing.OpenOffset, existing.OpenLength).Insert(existing.OpenOffset, newOpen);
            state.RecordEdit(existing.OpenOffset, existing.OpenLength, newOpen.Length);
            // Re-read after RecordEdit — it already shifted this wrap's CloseOffset.
            state.Wraps[opId] = state.Wraps[opId] with { OpenLength = newOpen.Length };
            report = $"<{tagName}> wrapper updated";
            return result;
        }

        var offset = ResolveAnchor(text, state, change.Line, change.Column);
        if (offset < 0)
        {
            message = "line/column out of range (file changed since the app was built? restart the app)";
            return null;
        }
        if (change.Element.Length > 0
            && XamlTagScanner.LocalNameOf(XamlTagScanner.ReadQName(text, offset)) != change.Element)
        {
            message = $"expected <{change.Element}> here but found \"{Snippet(text, offset)}\" — restart the app after editing XAML by hand";
            return null;
        }

        if (ElementSpan(text, offset, XamlTagScanner.LocalNameOf(XamlTagScanner.ReadQName(text, offset)), out message) is not { } span)
            return null;
        var (spanStart, spanEnd) = span;
        var content2 = text[spanStart..spanEnd];

        var indent = IndentOfLine(text, offset);
        var step = indent.Contains('\t') ? "\t" : "    ";
        var open = indent + openTag + "\n";
        var close = indent + "</" + tagName + ">\n";
        var inner = Reindent(content2, indent + step);

        var patched = text.Remove(spanStart, content2.Length)
            .Insert(spanStart, open + inner + close);

        state.RecordEdit(spanStart, content2.Length, 0, removeId: "w:" + opId);
        state.RecordEdit(spanStart, 0, open.Length);
        state.RecordEdit(spanStart + open.Length, 0, inner.Length, restoreOf: "w:" + opId,
            restoredBlock: content2, reindentDelta: step.Length);
        state.RecordEdit(spanStart + open.Length + inner.Length, 0, close.Length);
        state.Wraps[opId] = (spanStart, open.Length, spanStart + open.Length + inner.Length, close.Length, indent);

        report = $"<{change.Element}> wrapped in <{tagName}>";
        return patched;
    }

    /// <summary>
    /// Strips a source-backed container: its opening/closing tag lines disappear, the content
    /// stays and drops one indentation level. Requires the usual formatting — the opening tag
    /// ends its line and the closing tag stands on its own line.
    /// </summary>
    string? ApplyElementUnwrap(string text, FileState state, XamlChange change, out string message, out string report)
    {
        message = "";
        report = "";
        var opId = change.Attribute;

        if (!change.Remove)
        {
            // Undo: put the container's tags back around its (possibly edited) content.
            if (!state.Unwrapped.TryGetValue(opId, out var unwrapped))
            {
                Warn("nothing recorded to re-wrap (unwrapped in an earlier session?) — restore the container from source control");
                report = "re-wrap skipped";
                return text;
            }

            var currentBlock = text.Substring(unwrapped.Offset, unwrapped.PromotedLength);
            var blockFirst = currentBlock.TrimStart(' ', '\t');
            var blockIndent = currentBlock[..(currentBlock.Length - blockFirst.Length)];
            var back = Reindent(currentBlock, unwrapped.ContentIndent);

            var restored = text.Remove(unwrapped.Offset, unwrapped.PromotedLength).Insert(unwrapped.Offset, back);
            state.RecordEdit(unwrapped.Offset, unwrapped.PromotedLength, 0, removeId: "uwr:" + opId);
            state.RecordEdit(unwrapped.Offset, 0, back.Length, restoreOf: "uwr:" + opId,
                restoredBlock: currentBlock, reindentDelta: unwrapped.ContentIndent.Length - blockIndent.Length);
            restored = restored.Insert(unwrapped.Offset, unwrapped.OpenText);
            state.RecordEdit(unwrapped.Offset, 0, unwrapped.OpenText.Length, restoreOf: "uwo:" + opId);
            var closeAt = unwrapped.Offset + unwrapped.OpenText.Length + back.Length;
            restored = restored.Insert(closeAt, unwrapped.CloseText);
            state.RecordEdit(closeAt, 0, unwrapped.CloseText.Length, restoreOf: "uwc:" + opId);
            state.Unwrapped.Remove(opId);

            report = $"<{change.Element}> re-wrapped";
            return restored;
        }

        var offset = ResolveAnchor(text, state, change.Line, change.Column);
        if (offset < 0)
        {
            message = "line/column out of range (file changed since the app was built? restart the app)";
            return null;
        }
        if (XamlTagScanner.LocalNameOf(XamlTagScanner.ReadQName(text, offset)) != change.Element)
        {
            message = $"expected <{change.Element}> here but found \"{Snippet(text, offset)}\" — restart the app after editing XAML by hand";
            return null;
        }

        var tagEnd = XamlTagScanner.FindTagEnd(text, offset);
        if (tagEnd < 0 || XamlTagScanner.IsSelfClosing(text, tagEnd))
        {
            message = "the container is self-closing — nothing to unwrap";
            return null;
        }
        var (closeStart, _) = XamlTagScanner.FindClosingTag(text, offset, change.Element);
        if (closeStart < 0)
        {
            message = $"could not find </{change.Element}> to unwrap";
            return null;
        }

        if (ElementSpan(text, offset, change.Element, out message) is not { } span)
            return null;
        var (spanStart, spanEnd) = span;

        // Content = whole lines between the opening tag's line and the closing tag's line.
        var openBlockEnd = tagEnd;
        while (openBlockEnd < text.Length && text[openBlockEnd] != '\n')
        {
            if (text[openBlockEnd] is not (' ' or '\t' or '\r'))
            {
                message = "content follows the opening tag on the same line — unwrap needs one element per line";
                return null;
            }
            openBlockEnd++;
        }
        openBlockEnd = Math.Min(text.Length, openBlockEnd + 1);

        var closeBlockStart = LineStartOf(text, closeStart);
        if (text[closeBlockStart..closeStart].Trim().Length > 0)
        {
            message = "the closing tag shares a line with content — unwrap needs one element per line";
            return null;
        }
        if (closeBlockStart <= openBlockEnd)
        {
            message = "the container is empty — use Remove instead";
            return null;
        }

        var content = text[openBlockEnd..closeBlockStart];
        var parentIndent = IndentOfLine(text, offset);
        var firstContent = content.TrimStart(' ', '\t');
        var contentIndent = content[..(content.Length - firstContent.Length)];
        var promoted = Reindent(content, parentIndent);

        // Order matters: each edit's coordinates describe the file at its own time. The tag
        // blocks are removed under their own capture ids so the container's anchor (and any
        // position inside its closing tag) survives an unwrap → undo round trip.
        var result = text.Remove(closeBlockStart, spanEnd - closeBlockStart);
        state.RecordEdit(closeBlockStart, spanEnd - closeBlockStart, 0, removeId: "uwc:" + opId);
        result = result.Remove(openBlockEnd, content.Length).Insert(openBlockEnd, promoted);
        state.RecordEdit(openBlockEnd, content.Length, 0, removeId: "uw:" + opId);
        state.RecordEdit(openBlockEnd, 0, promoted.Length, restoreOf: "uw:" + opId,
            restoredBlock: content, reindentDelta: parentIndent.Length - contentIndent.Length);
        result = result.Remove(spanStart, openBlockEnd - spanStart);
        state.RecordEdit(spanStart, openBlockEnd - spanStart, 0, removeId: "uwo:" + opId);

        state.Unwrapped[opId] = (spanStart, promoted.Length,
            text[spanStart..openBlockEnd], text[closeBlockStart..spanEnd], contentIndent);

        report = $"<{change.Element}> unwrapped — children kept";
        return result;
    }

    // ---- style resources ---------------------------------------------------------------------

    /// <summary>
    /// Inserts a Style block into the page's &lt;Root.Resources&gt; (creating the property
    /// element right after the root tag when missing). Cancel removes the tracked span.
    /// </summary>
    string? ApplyStyleResource(string text, FileState state, XamlChange change, out string message, out string report)
    {
        message = "";
        report = "";
        var opId = change.Attribute;

        if (change.Remove)
        {
            if (!state.Inserts.TryGetValue(opId, out var span))
            {
                report = "style not inserted in this session — nothing to take back";
                return text;
            }
            var result = DropInsertSpan(text, state, opId, span);
            report = "extracted style taken back out";
            return result;
        }

        JsonObject? payload;
        try
        {
            payload = JsonNode.Parse(change.Value) as JsonObject;
        }
        catch
        {
            payload = null;
        }
        var xml = payload?["xml"]?.GetValue<string>();
        if (xml == null)
        {
            message = "unreadable style payload";
            return null;
        }

        if (payload?["xmlns"] is JsonObject xmlnsMap)
        {
            foreach (var (localPrefix, declarationNode) in xmlnsMap)
            {
                var declaration = declarationNode?.GetValue<string>() ?? "";
                var match = Regex.Match(declaration, @"^clr-namespace:([^;]+);assembly=(.+)$");
                if (!match.Success)
                    continue;
                var ensured = EnsureXmlnsForNamespace(text, match.Groups[1].Value, match.Groups[2].Value,
                    out var actualPrefix, out message, out var xmlnsAt);
                if (ensured == null)
                    return null;
                if (!ReferenceEquals(ensured, text))
                    state.RecordEdit(xmlnsAt, 0, ensured.Length - text.Length);
                text = ensured;
                if (actualPrefix != localPrefix)
                    xml = xml.Replace("\"" + localPrefix + ":", "\"" + actualPrefix + ":");
            }
        }

        var offset = ResolveAnchor(text, state, change.Line, change.Column);
        if (offset < 0)
        {
            message = "line/column out of range (file changed since the app was built? restart the app)";
            return null;
        }
        var rootQName = XamlTagScanner.ReadQName(text, offset);
        if (XamlTagScanner.LocalNameOf(rootQName) != change.Element)
        {
            message = $"expected page root <{change.Element}> here but found \"{Snippet(text, offset)}\"";
            return null;
        }
        var rootTagEnd = XamlTagScanner.FindTagEnd(text, offset);
        if (rootTagEnd < 0 || XamlTagScanner.IsSelfClosing(text, rootTagEnd))
        {
            message = "the page root has no content to hold resources";
            return null;
        }

        var rootIndent = IndentOfLine(text, offset);
        var step = rootIndent.Contains('\t') ? "\t" : "    ";
        var resourcesTag = $"{rootQName}.Resources";

        var resOpen = text.IndexOf($"<{resourcesTag}", rootTagEnd, StringComparison.Ordinal);
        string inserted;
        int insertOffset;
        if (resOpen >= 0)
        {
            var resClose = text.IndexOf($"</{resourcesTag}>", resOpen, StringComparison.Ordinal);
            if (resClose < 0)
            {
                message = $"could not find </{resourcesTag}>";
                return null;
            }
            var resIndent = IndentOfLine(text, resOpen + 1);
            var lineStart = LineStartOf(text, resClose);
            insertOffset = text[lineStart..resClose].Trim().Length == 0 ? lineStart : resClose;
            inserted = string.Join("\n", xml.Split('\n').Select(l => resIndent + step + l)) + "\n";
        }
        else
        {
            // No resources yet — create the property element right after the root's open tag.
            var afterRoot = rootTagEnd;
            while (afterRoot < text.Length && text[afterRoot] != '\n')
                afterRoot++;
            insertOffset = Math.Min(text.Length, afterRoot + 1);
            var body = string.Join("\n", xml.Split('\n').Select(l => rootIndent + step + step + l));
            inserted = $"\n{rootIndent}{step}<{resourcesTag}>\n{body}\n{rootIndent}{step}</{resourcesTag}>\n";
        }

        var patched = text.Insert(insertOffset, inserted);
        state.RecordEdit(insertOffset, 0, inserted.Length);
        state.Inserts[opId] = (insertOffset, inserted.Length, "");

        report = "style inserted into page resources";
        return patched;
    }

    /// <summary>
    /// Edits one setter of a style located by x:Key (or, for implicit styles, by TargetType) —
    /// no line anchors involved, so it works in any resource file the updater can find.
    /// </summary>
    string? ApplyStyleSetter(string text, FileState state, XamlChange change, out string message, out string report)
    {
        message = "";
        report = "";

        JsonObject? payload;
        try
        {
            payload = JsonNode.Parse(change.Value) as JsonObject;
        }
        catch
        {
            payload = null;
        }
        if (payload == null)
        {
            message = "unreadable setter payload";
            return null;
        }
        var key = payload["key"]?.GetValue<string>() ?? "";
        var targetType = payload["targetType"]?.GetValue<string>() ?? "";
        var property = payload["property"]?.GetValue<string>() ?? "";
        var value = payload["value"]?.GetValue<string>() ?? "";

        // Keyed style first; implicit styles are keyed by the type's full name at runtime.
        var keyPattern = "<Style\\b[^>]*x:Key=\"" + Regex.Escape(key) + "\"";
        var styleMatch = Regex.Match(text, keyPattern);
        if (!styleMatch.Success)
        {
            var implicitPattern = "<Style\\b(?![^>]*x:Key)[^>]*TargetType=\"(?:\\w+:)?" + Regex.Escape(targetType) + "\"";
            styleMatch = Regex.Match(text, implicitPattern);
        }
        if (!styleMatch.Success)
        {
            message = $"style \"{key}\" not found in this file";
            return null;
        }

        var nameOffset = styleMatch.Index + 1;
        var (closeStart, _) = XamlTagScanner.FindClosingTag(text, nameOffset, "Style");
        if (closeStart < 0)
        {
            message = "could not find </Style>";
            return null;
        }

        var setterPattern = "<Setter\\b[^>]*Property=\"" + Regex.Escape(property) + "\"[^>]*";
        var setterMatch = Regex.Match(text[styleMatch.Index..closeStart], setterPattern);
        string? patched;
        if (setterMatch.Success)
        {
            var setterOffset = styleMatch.Index + setterMatch.Index + 1;
            var setterChange = change with { Element = "Setter", Attribute = "Value", Value = value, Op = "attr", Remove = false };
            patched = Patch(text, setterChange, setterOffset, out message);
            if (patched == null)
                return null;
            state.RecordEdit(setterOffset + 1, 0, patched.Length - text.Length);
        }
        else
        {
            var indent = IndentOfLine(text, nameOffset) + (IndentOfLine(text, nameOffset).Contains('\t') ? "\t" : "    ");
            var lineStart = LineStartOf(text, closeStart);
            var insertAt = text[lineStart..closeStart].Trim().Length == 0 ? lineStart : closeStart;
            var inserted = $"{indent}<Setter Property=\"{property}\" Value=\"{EscapeAttributeValue(value)}\" />\n";
            patched = text.Insert(insertAt, inserted);
            state.RecordEdit(insertAt, 0, inserted.Length);
        }

        report = $"Style {(key.Length > 0 ? key : targetType)}: Setter {property} = \"{value}\"";
        return patched;
    }

    /// <summary>
    /// Replaces the text content of a scalar resource ("&lt;x:Double x:Key="K"&gt;36&lt;/x:Double&gt;",
    /// Color, String, Thickness…) located by x:Key — no line anchors, works in any dictionary file.
    /// </summary>
    string? ApplyResourceValue(string text, FileState state, XamlChange change, out string message, out string report)
    {
        message = "";
        report = "";

        JsonObject? payload;
        try
        {
            payload = JsonNode.Parse(change.Value) as JsonObject;
        }
        catch
        {
            payload = null;
        }
        var key = payload?["key"]?.GetValue<string>();
        var value = payload?["value"]?.GetValue<string>();
        if (key == null || value == null)
        {
            message = "unreadable resource payload";
            return null;
        }

        var keyMatch = Regex.Match(text, "x:Key=\"" + Regex.Escape(key) + "\"");
        if (!keyMatch.Success)
        {
            message = $"resource \"{key}\" not found in this file";
            return null;
        }

        var tagStart = text.LastIndexOf('<', keyMatch.Index);
        if (tagStart < 0)
        {
            message = $"could not locate the tag of resource \"{key}\"";
            return null;
        }
        var qname = XamlTagScanner.ReadQName(text, tagStart + 1);
        var tagEnd = XamlTagScanner.FindTagEnd(text, tagStart);
        if (tagEnd < 0 || XamlTagScanner.IsSelfClosing(text, tagEnd))
        {
            message = $"resource \"{key}\" has no text content to edit";
            return null;
        }
        var closeStart = text.IndexOf($"</{qname}>", tagEnd, StringComparison.Ordinal);
        if (closeStart < 0)
        {
            message = $"could not find </{qname}> for resource \"{key}\"";
            return null;
        }

        var contentStart = tagEnd;
        var content = value.Replace("&", "&amp;").Replace("<", "&lt;");
        var patched = text[..contentStart] + content + text[closeStart..];
        state.RecordEdit(contentStart, closeStart - contentStart, content.Length);

        report = $"resource {key} = \"{value}\"";
        return patched;
    }

    static string RenderOpenTag(string tagName, JsonObject? attrs)
    {
        var parts = new List<string> { tagName };
        if (attrs != null)
        {
            foreach (var (name, value) in attrs)
            {
                if (value != null)
                    parts.Add($"{name}=\"{EscapeAttributeValue(value.GetValue<string>())}\"");
            }
        }
        return $"<{string.Join(' ', parts)}>";
    }

    /// <summary>Shifts every line of a whole-line block from its current leading indent to the target.</summary>
    static string Reindent(string block, string targetIndent)
    {
        var firstContent = block.TrimStart(' ', '\t');
        var currentIndent = block[..(block.Length - firstContent.Length)];
        if (currentIndent == targetIndent)
            return block;

        var lines = block.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith(currentIndent, StringComparison.Ordinal))
                lines[i] = targetIndent + lines[i][currentIndent.Length..];
        }
        return string.Join('\n', lines);
    }

    /// <summary>
    /// Whole-line span of the element whose name starts at nameOffset: from the start of its
    /// opening line through the end-of-line after it, when only whitespace surrounds it there.
    /// </summary>
    static (int Start, int End)? ElementSpan(string text, int nameOffset, string localName, out string message)
    {
        message = "";
        var tagEnd = XamlTagScanner.FindTagEnd(text, nameOffset);
        if (tagEnd < 0)
        {
            message = "could not find the end of the opening tag";
            return null;
        }

        int elementEnd;
        if (XamlTagScanner.IsSelfClosing(text, tagEnd))
        {
            elementEnd = tagEnd;
        }
        else
        {
            var (_, end) = XamlTagScanner.FindClosingTag(text, nameOffset, localName);
            if (end < 0)
            {
                message = $"could not find </{localName}>";
                return null;
            }
            elementEnd = end;
        }

        var spanStart = LineStartOf(text, nameOffset - 1);
        if (text[spanStart..(nameOffset - 1)].Trim().Length > 0)
            spanStart = nameOffset - 1;

        var spanEnd = elementEnd;
        var lineEnd = spanEnd;
        while (lineEnd < text.Length && text[lineEnd] != '\n')
            lineEnd++;
        if (spanEnd >= text.Length || text[spanEnd..lineEnd].Trim().Length == 0)
            spanEnd = Math.Min(text.Length, lineEnd + 1);

        return (spanStart, spanEnd);
    }

    /// <summary>Undoes one tracked insert span: puts back whatever it replaced.</summary>
    static string DropInsertSpan(string text, FileState state, string opId, (int Offset, int Length, string ReplacedText) span)
    {
        var result = text.Remove(span.Offset, span.Length).Insert(span.Offset, span.ReplacedText);
        state.Inserts.Remove(opId);
        state.RecordEdit(span.Offset, span.Length, span.ReplacedText.Length);
        return result;
    }

    // ---- helpers ---------------------------------------------------------------------------

    /// <summary>Finds (or adds, inline on the root tag) an xmlns for the control's namespace.</summary>
    static string? EnsureXmlns(string text, string typeName, string assembly, out string prefix, out string message, out int insertAt)
    {
        var lastDot = typeName.LastIndexOf('.');
        var clrNamespace = lastDot > 0 ? typeName[..lastDot] : typeName;
        return EnsureXmlnsForNamespace(text, clrNamespace, assembly, out prefix, out message, out insertAt);
    }

    static string? EnsureXmlnsForNamespace(string text, string clrNamespace, string assembly, out string prefix, out string message)
        => EnsureXmlnsForNamespace(text, clrNamespace, assembly, out prefix, out message, out _);

    static string? EnsureXmlnsForNamespace(string text, string clrNamespace, string assembly, out string prefix, out string message, out int insertAt)
    {
        prefix = "";
        message = "";
        insertAt = -1;

        var existing = Regex.Match(text,
            $@"xmlns:([\w]+)\s*=\s*""clr-namespace:{Regex.Escape(clrNamespace)}(;assembly=[^""]*)?""");
        if (existing.Success)
        {
            prefix = existing.Groups[1].Value;
            return text;
        }

        var rootMatch = Regex.Match(text, @"<([A-Za-z_][\w:.]*)[\s>]");
        if (!rootMatch.Success)
        {
            message = "could not find the root element to add an xmlns to";
            return null;
        }

        var candidate = "ctl";
        for (var i = 2; Regex.IsMatch(text, $@"xmlns:{candidate}\s*="); i++)
            candidate = $"ctl{i}";
        prefix = candidate;

        insertAt = rootMatch.Index + 1 + rootMatch.Groups[1].Length;
        var xmlns = $" xmlns:{prefix}=\"clr-namespace:{clrNamespace};assembly={assembly}\"";
        return text.Insert(insertAt, xmlns);
    }

    /// <summary>
    /// The inserted markup: a self-closing tag, or — when the payload carries the pasted
    /// subtree — an open/close pair with the children re-indented one step deeper. Inner
    /// lines carry the caller's indent themselves; the caller prefixes only the first line.
    /// </summary>
    static string RenderSnippet(string tagName, JsonObject? attrs, string? childrenXml, string indent)
    {
        var parts = new List<string> { tagName };
        if (attrs != null)
        {
            foreach (var (name, value) in attrs)
            {
                if (value != null)
                    parts.Add($"{name}=\"{EscapeAttributeValue(value.GetValue<string>())}\"");
            }
        }
        var open = string.Join(' ', parts);

        if (string.IsNullOrEmpty(childrenXml))
            return $"<{open} />";

        var step = indent.Contains('\t') ? "\t" : "    ";
        var inner = string.Join("\n", childrenXml.Split('\n').Select(line => indent + step + line));
        return $"<{open}>\n{inner}\n{indent}</{tagName}>";
    }

    static int OffsetOf(string text, int line, int column)
    {
        var currentLine = 1;
        var offset = 0;
        while (currentLine < line)
        {
            var next = text.IndexOf('\n', offset);
            if (next < 0)
                return -1;
            offset = next + 1;
            currentLine++;
        }
        var result = offset + column - 1;
        return result <= text.Length ? result : -1;
    }

    static int LineAt(string text, int offset)
    {
        var line = 1;
        for (var i = 0; i < offset && i < text.Length; i++)
        {
            if (text[i] == '\n')
                line++;
        }
        return line;
    }

    static int LineStartOf(string text, int offset)
    {
        var start = Math.Min(offset, text.Length);
        while (start > 0 && text[start - 1] != '\n')
            start--;
        return start;
    }

    static string IndentOfLine(string text, int offset)
    {
        var start = LineStartOf(text, offset);
        var end = start;
        while (end < text.Length && text[end] is ' ' or '\t')
            end++;
        return text[start..end];
    }

    static int CountLines(string text) => text.Count(c => c == '\n');

    static string EscapeAttributeValue(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace("\"", "&quot;");

    static string Snippet(string text, int offset)
    {
        var end = Math.Min(text.Length, offset + 24);
        return text[offset..end].Replace('\n', ' ').Replace('\r', ' ').Trim();
    }

    string? ResolveFile(string relativePath)
    {
        if (_resolvedFiles.TryGetValue(relativePath, out var cached))
            return cached;

        string? result = null;
        var direct = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(direct))
        {
            result = direct;
        }
        else
        {
            var fileName = Path.GetFileName(relativePath);
            var matches = Directory.EnumerateFiles(_root, fileName, SearchOption.AllDirectories)
                .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                            && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                .Where(p => p.Replace('\\', '/').EndsWith(relativePath, StringComparison.OrdinalIgnoreCase))
                .Take(2)
                .ToList();

            if (matches.Count == 1)
                result = matches[0];
            else if (matches.Count > 1)
                Warn($"{relativePath}: multiple matches found, skipping (narrow --src)");
        }

        _resolvedFiles[relativePath] = result;
        return result;
    }

    static void Info(string message) => Console.WriteLine($"  {message}");

    static (bool, string) Fail(string message)
    {
        Warn(message);
        return (false, message);
    }

    static void Warn(string message)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"⚠ {message}");
        Console.ResetColor();
    }
}

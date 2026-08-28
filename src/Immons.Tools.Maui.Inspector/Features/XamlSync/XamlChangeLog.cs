using System.Text.Json.Nodes;
using Immons.Tools.Maui.Inspector.Features.Structure;
using Immons.Tools.Maui.Inspector.Shared.Storage;

namespace Immons.Tools.Maui.Inspector.Features.XamlSync;

/// <summary>
/// Records successful property edits together with the XAML source location of the edited
/// object, for the sync tool to write back into the source files.
/// Only the latest value per (object, attribute) — and per structural operation — is kept.
/// </summary>
internal sealed class XamlChangeLog(IAddedElements added) : IXamlChangeLog
{
    /// <summary>Sentinel returned by an editor's XamlValue to request attribute removal.</summary>
    public const string RemoveMarker = " remove-attribute ";

    /// <summary>Operation kinds understood by the sync tool.</summary>
    public static class Ops
    {
        public const string Attribute = "attr";
        public const string InsertElement = "insert";
        public const string RemoveElement = "remove-el";
        public const string MoveElement = "move-el";
        public const string WrapElement = "wrap-el";
        public const string UnwrapElement = "unwrap-el";
        public const string StyleResource = "style-res";
        public const string StyleSetter = "setter";
        public const string ResourceValue = "res-val";
    }

    internal sealed record Change(
        long Seq,
        string SourceUri,
        int Line,
        int Column,
        string ElementType,
        string Attribute,
        string Value,
        bool Remove,
        string Op = Ops.Attribute);

    readonly object _gate = new();
    readonly Dictionary<string, Change> _latest = [];
    readonly Dictionary<long, (bool Ok, string Message)> _writeResults = [];
    long _seq;
    volatile bool _enabled;

    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    public long LastSeq
    {
        get
        {
            lock (_gate)
            {
                return _seq;
            }
        }
    }

    public void AckWrite(long seq, bool ok, string message)
    {
        lock (_gate)
        {
            _writeResults[seq] = (ok, message);
            // The ring stays small — acks older than the last hundred are of no interest.
            if (_writeResults.Count > 200)
            {
                foreach (var stale in _writeResults.Keys.OrderBy(k => k).Take(_writeResults.Count - 100).ToList())
                    _writeResults.Remove(stale);
            }
        }
    }

    public (string State, string? Message) WriteStatus(long seq)
    {
        lock (_gate)
        {
            return _writeResults.TryGetValue(seq, out var result)
                ? (result.Ok ? "applied" : "failed", result.Message)
                : ("pending", null);
        }
    }

    public void Record(object target, string attribute, string value)
    {
        // Inspector-added elements have no SourceInfo: their attribute edits update the pending
        // insert/wrap snippet (and its persisted op) instead of an in-place attribute patch.
        if (added.Find(target) is { } op)
        {
            if (op.Kind == StructureOp.KindWrap)
            {
                if (value == RemoveMarker)
                    op.Attributes.Remove(attribute);
                else
                    op.Attributes[attribute] = value;
                InspectorStorage.Current.Structure.Save(op.Id, op.ToJson());
                RecordWrap(op);
                return;
            }

            if (target is View rootView && op.ParentIdentity != null)
            {
                RefreshAddedSubtree(rootView, op);
                return;
            }

            // Unanchored add (created inside a pasted subtree): keep its own op fresh for
            // replay, and push the file through the anchored ancestor's snippet.
            if (value == RemoveMarker)
                op.Attributes.Remove(attribute);
            else
                op.Attributes[attribute] = value;
            InspectorStorage.Current.Structure.Save(op.Id, op.ToJson());
            if (target is VisualElement unanchored)
                RefreshSubtreeSnippet(unanchored);
            return;
        }

        if (!_enabled)
            return;

        SourceInfo? info;
        try
        {
            info = Microsoft.Maui.VisualDiagnostics.GetSourceInfo(target);
        }
        catch
        {
            return;
        }
        if (info?.SourceUri == null)
        {
            // No source location of its own — the element may live inside a pasted subtree,
            // whose whole snippet then carries this edit into the file.
            if (target is VisualElement insidePaste)
                RefreshSubtreeSnippet(insidePaste);
            return;
        }

        var remove = value == RemoveMarker;
        var change = new Change(
            0,
            info.SourceUri.ToString(),
            info.LineNumber,
            info.LinePosition,
            target.GetType().Name,
            attribute,
            remove ? "" : value,
            remove);

        Push($"{change.SourceUri}:{change.Line}:{change.Column}|{attribute}", change);
    }

    public void RecordInsert(StructureOp op) => PushInsert(op, cancel: false);

    public void RefreshSubtreeSnippet(VisualElement element)
    {
        for (Element? current = element; current != null; current = current.Parent)
        {
            if (current is not View view || added.Find(view) is not { Kind: StructureOp.KindAdd } op)
                continue;
            if (op.ParentIdentity == null)
                continue; // unanchored — the anchored root sits higher up
            RefreshAddedSubtree(view, op);
            return;
        }
    }

    /// <summary>Re-serializes the live subtree into the op: attributes and nested markup.</summary>
    void RefreshAddedSubtree(View root, StructureOp op)
    {
        if (ElementCloner.Describe(root, op.DeepCopy) is not { } described)
            return;

        op.Attributes.Clear();
        foreach (var (name, value) in described.Attributes)
            op.Attributes[name] = value;

        var updated = op with { SnippetXml = described.ChildrenXml, SnippetXmlns = described.XmlnsMap };
        added.Register(root, updated);
        InspectorStorage.Current.Structure.Save(updated.Id, updated.ToJson());
        RecordInsert(updated);
    }

    public void CancelInsert(StructureOp op) => PushInsert(op, cancel: true);

    public void RecordElementRemove(StructureOp op) => PushElementRemove(op, restore: false);

    public void RestoreElement(StructureOp op) => PushElementRemove(op, restore: true);

    void PushInsert(StructureOp op, bool cancel)
    {
        if (!_enabled || StructureOp.ParseIdentity(op.ParentIdentity) is not { } anchor)
            return;

        var payload = new JsonObject
        {
            ["type"] = op.TypeName,
            ["asm"] = op.Assembly,
            ["name"] = op.ElementType,
            ["attrs"] = AttrsJson(op),
        };

        if (op.SnippetXml != null)
            payload["childrenXml"] = op.SnippetXml;
        if (op.SnippetXmlns != null)
        {
            var xmlns = new JsonObject();
            foreach (var (prefix, declaration) in op.SnippetXmlns)
                xmlns[prefix] = declaration;
            payload["xmlns"] = xmlns;
        }

        // Optional position: next to a source-backed sibling, or next to another pending insert.
        if (op.SiblingOpId != null)
        {
            payload["anchorOp"] = op.SiblingOpId;
            payload["before"] = op.Before;
        }
        else if (StructureOp.ParseIdentity(op.SiblingIdentity) is { } sibling)
        {
            payload["sibLine"] = sibling.Line;
            payload["sibColumn"] = sibling.Column;
            payload["sibElement"] = op.SiblingType;
            payload["before"] = op.Before;
        }

        Push($"ins:{op.Id}", new Change(
            0, anchor.Uri, anchor.Line, anchor.Column,
            op.ParentType, op.Id, payload.ToJsonString(), cancel, Ops.InsertElement));
    }

    public void RecordStyleSetter(string? dictionarySource, string styleKey, string targetType, string property, string value)
    {
        if (!_enabled || string.IsNullOrEmpty(dictionarySource))
            return;

        var payload = new JsonObject
        {
            ["key"] = styleKey,
            ["targetType"] = targetType,
            ["property"] = property,
            ["value"] = value,
        };
        Push($"setter:{dictionarySource}|{styleKey}|{property}", new Change(
            0, dictionarySource, 0, 0, "Style", property, payload.ToJsonString(), false, Ops.StyleSetter));
    }

    public void RecordResourceValue(string? dictionarySource, string key, string value)
    {
        if (!_enabled || string.IsNullOrEmpty(dictionarySource))
            return;

        var payload = new JsonObject
        {
            ["key"] = key,
            ["value"] = value,
        };
        Push($"resval:{dictionarySource}|{key}", new Change(
            0, dictionarySource, 0, 0, "Resource", key, payload.ToJsonString(), false, Ops.ResourceValue));
    }

    public void RecordStyleResource(StructureOp op) => PushStyleResource(op, cancel: false);

    public void CancelStyleResource(StructureOp op) => PushStyleResource(op, cancel: true);

    void PushStyleResource(StructureOp op, bool cancel)
    {
        // Anchored at the PAGE root element — the style lands in <Root.Resources>.
        if (!_enabled || StructureOp.ParseIdentity(op.ParentIdentity) is not { } anchor)
            return;

        var payload = new JsonObject { ["xml"] = op.SnippetXml };
        if (op.SnippetXmlns != null)
        {
            var xmlns = new JsonObject();
            foreach (var (prefix, declaration) in op.SnippetXmlns)
                xmlns[prefix] = declaration;
            payload["xmlns"] = xmlns;
        }

        Push($"sty:{op.Id}", new Change(
            0, anchor.Uri, anchor.Line, anchor.Column,
            op.ParentType, op.Id, payload.ToJsonString(), cancel, Ops.StyleResource));
    }

    public void RecordElementMove(StructureOp op)
    {
        if (!_enabled
            || StructureOp.ParseIdentity(op.ElementIdentity) is not { } anchor
            || StructureOp.ParseIdentity(op.SiblingIdentity) is not { } sibling)
            return;

        var payload = new JsonObject
        {
            ["sibLine"] = sibling.Line,
            ["sibColumn"] = sibling.Column,
            ["sibElement"] = op.SiblingType,
            ["before"] = op.Before,
        };

        // Every move is its own change (unique key): two consecutive moves must both apply.
        Push($"mv:{op.Id}", new Change(
            0, anchor.Uri, anchor.Line, anchor.Column,
            op.ElementType, op.Id, payload.ToJsonString(), false, Ops.MoveElement));
    }

    public void RecordElementReparent(StructureOp op)
    {
        if (!_enabled
            || StructureOp.ParseIdentity(op.ElementIdentity) is not { } anchor
            || StructureOp.ParseIdentity(op.ParentIdentity) is not { } parent
            || parent.Uri != anchor.Uri)
            return; // cross-file reparenting stays live-only

        var payload = new JsonObject
        {
            ["parLine"] = parent.Line,
            ["parColumn"] = parent.Column,
            ["parElement"] = op.ParentType,
            ["before"] = op.Before,
        };
        if (StructureOp.ParseIdentity(op.SiblingIdentity) is { } sibling && sibling.Uri == anchor.Uri)
        {
            payload["sibLine"] = sibling.Line;
            payload["sibColumn"] = sibling.Column;
            payload["sibElement"] = op.SiblingType;
        }

        Push($"mv:{op.Id}", new Change(
            0, anchor.Uri, anchor.Line, anchor.Column,
            op.ElementType, op.Id, payload.ToJsonString(), false, Ops.MoveElement));
    }

    public void RecordElementUnwrap(StructureOp op) => PushUnwrap(op, restore: false);

    public void RestoreElementUnwrap(StructureOp op) => PushUnwrap(op, restore: true);

    void PushUnwrap(StructureOp op, bool restore)
    {
        if (!_enabled || StructureOp.ParseIdentity(op.ElementIdentity) is not { } anchor)
            return;

        Push($"uw:{op.Id}", new Change(
            0, anchor.Uri, anchor.Line, anchor.Column,
            op.ElementType, op.Id, "", !restore, Ops.UnwrapElement));
    }

    public void RecordWrap(StructureOp op) => PushWrap(op, cancel: false);

    public void CancelWrap(StructureOp op) => PushWrap(op, cancel: true);

    void PushWrap(StructureOp op, bool cancel)
    {
        // The anchor is the WRAPPED element — the wrapper itself has no source location.
        if (!_enabled || StructureOp.ParseIdentity(op.ElementIdentity) is not { } anchor)
            return;

        var payload = new JsonObject
        {
            ["type"] = op.TypeName,
            ["asm"] = op.Assembly,
            ["name"] = op.ElementType,
            ["attrs"] = AttrsJson(op),
        };

        // Wrap ops carry the wrapper in TypeName/ElementType; the wrapped element's type
        // travels in SiblingType so the tool can verify the tag under the anchor.
        Push($"wrap:{op.Id}", new Change(
            0, anchor.Uri, anchor.Line, anchor.Column,
            op.SiblingType, op.Id, payload.ToJsonString(), cancel, Ops.WrapElement));
    }

    void PushElementRemove(StructureOp op, bool restore)
    {
        if (!_enabled || StructureOp.ParseIdentity(op.ElementIdentity) is not { } anchor)
            return;

        Push($"rm:{op.ElementIdentity}", new Change(
            0, anchor.Uri, anchor.Line, anchor.Column,
            op.ElementType, op.Id, "", !restore, Ops.RemoveElement));
    }

    static JsonObject AttrsJson(StructureOp op)
    {
        var attrs = new JsonObject();
        foreach (var (key, value) in op.Attributes)
            attrs[key] = value;
        return attrs;
    }

    void Push(string key, Change change)
    {
        lock (_gate)
        {
            _seq++;
            _latest[key] = change with { Seq = _seq };
        }
    }

    public string ToJson(long since, bool includeStructural)
    {
        lock (_gate)
        {
            var changes = new JsonArray();
            foreach (var change in _latest.Values
                         .Where(c => c.Seq > since && (includeStructural || c.Op == Ops.Attribute))
                         .OrderBy(c => c.Seq))
            {
                changes.Add(new JsonObject
                {
                    ["seq"] = change.Seq,
                    ["source"] = change.SourceUri,
                    ["line"] = change.Line,
                    ["column"] = change.Column,
                    ["element"] = change.ElementType,
                    ["attribute"] = change.Attribute,
                    ["value"] = change.Value,
                    ["remove"] = change.Remove,
                    ["op"] = change.Op,
                });
            }

            return new JsonObject
            {
                ["seq"] = _seq,
                ["changes"] = changes,
            }.ToJsonString();
        }
    }
}

using System.Collections.Concurrent;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Immons.Tools.Maui.Inspector.Features.Editing;

/// <summary>
/// Reads the raw attribute text of an element's tag from the XAML embedded in the app's
/// assembly (runtime/XamlC inflation keeps the .xaml as a manifest resource, addressed via
/// XamlResourceIdAttribute). This is the only place the original markup of parse-time
/// expressions like "{OnIdiom …}" survives — the runtime property only holds the result.
/// </summary>
internal static class XamlSourceText
{
    static readonly ConcurrentDictionary<string, string?> Files = new();

    /// <summary>The verbatim attribute value of the element's opening tag; null when the
    /// source, the tag or the attribute cannot be found.</summary>
    public static string? AttributeText(object element, string attribute)
    {
        try
        {
            var info = Microsoft.Maui.VisualDiagnostics.GetSourceInfo(element);
            if (info?.SourceUri == null)
                return null;

            var uri = info.SourceUri.ToString();
            if (!Files.TryGetValue(uri, out var text))
            {
                text = Load(uri);
                // Failures are not cached: the page's assembly may simply not be loaded yet.
                if (text != null)
                    Files[uri] = text;
            }
            if (text == null)
                return null;

            var offset = OffsetOf(text, info.LineNumber, info.LinePosition);
            if (offset < 0)
                return null;
            // LinePosition points at the tag name; tolerate small drift by re-anchoring
            // on the nearest '<' just before the reported position.
            if (offset >= text.Length || (offset > 0 && text[offset - 1] != '<'))
            {
                var open = text.LastIndexOf('<', Math.Min(offset, text.Length - 1));
                if (open < 0 || offset - open > 300)
                    return null;
                offset = open + 1;
            }
            var tagEnd = FindTagEnd(text, offset);
            if (tagEnd < 0)
                return null;

            var tag = text[offset..tagEnd];
            var match = Regex.Match(tag,
                @"(?<![\w.:])" + Regex.Escape(attribute) + "\\s*=\\s*\"([^\"]*)\"");
            return match.Success ? Unescape(match.Groups[1].Value) : null;
        }
        catch
        {
            return null; // best-effort — display falls back to the resolved value
        }
    }

    static string? Load(string sourceUri)
    {
        // "Controls/Foo.xaml;assembly=My.App" → manifest resource via XamlResourceIdAttribute
        var parts = sourceUri.Split(';');
        var path = parts[0].TrimStart('/');
        var assemblyName = parts.Skip(1)
            .FirstOrDefault(p => p.StartsWith("assembly=", StringComparison.Ordinal))?["assembly=".Length..];
        if (assemblyName == null)
            return null;

        var assembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == assemblyName);
        var normalized = path.Replace('\\', '/');
        var resourceId = assembly?.GetCustomAttributes<Microsoft.Maui.Controls.Xaml.XamlResourceIdAttribute>()
            .FirstOrDefault(a => a.Path.Replace('\\', '/') == normalized)?.ResourceId;
        if (resourceId == null)
            return null;

        using var stream = assembly!.GetManifestResourceStream(resourceId);
        if (stream == null)
            return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>1-based line/column (pointing at the tag name) to a 0-based offset.</summary>
    static int OffsetOf(string text, int line, int column)
    {
        var offset = 0;
        for (var current = 1; current < line; current++)
        {
            offset = text.IndexOf('\n', offset);
            if (offset < 0)
                return -1;
            offset++;
        }
        offset += column - 1;
        return offset <= text.Length ? offset : -1;
    }

    static int FindTagEnd(string text, int start)
    {
        char? quote = null;
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (quote != null)
            {
                if (c == quote)
                    quote = null;
            }
            else if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (c == '>')
            {
                return i;
            }
        }
        return -1;
    }

    static string Unescape(string value) => value
        .Replace("&quot;", "\"").Replace("&apos;", "'")
        .Replace("&lt;", "<").Replace("&gt;", ">").Replace("&amp;", "&");
}

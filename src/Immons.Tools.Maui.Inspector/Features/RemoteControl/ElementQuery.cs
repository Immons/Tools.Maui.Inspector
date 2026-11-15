using System.Collections.Specialized;
using System.Text.Json.Nodes;

namespace Immons.Tools.Maui.Inspector.Features.RemoteControl;

/// <summary>
/// What a remote caller (a script, an agent) is looking for on screen. Every given criterion
/// must match; <see cref="Index"/> picks among the matches in ranked order. Free of MAUI types
/// so the parsing and the text rules are unit-testable on plain net10.0.
/// </summary>
internal sealed record ElementQuery(
    string? AutomationId = null,
    string? Text = null,
    string? Type = null,
    string? Placeholder = null,
    int? Id = null,
    int Index = 0,
    bool Exact = false)
{
    public bool IsEmpty =>
        AutomationId == null && Text == null && Type == null && Placeholder == null && Id == null;

    /// <summary>Native views (tab bars, alerts, toolbar items) can only be matched by id or text.</summary>
    public bool NativeSearchable => (AutomationId != null || Text != null) && Id == null && Type == null && Placeholder == null;

    /// <summary>Reads the selector fields of a JSON body; null when none is present.</summary>
    public static ElementQuery? From(JsonNode? node)
    {
        if (node is not JsonObject obj)
            return null;

        var query = new ElementQuery(
            NonEmpty(Str(obj, "automationId")),
            NonEmpty(Str(obj, "text")),
            NonEmpty(Str(obj, "type")),
            NonEmpty(Str(obj, "placeholder")),
            Int(obj, "id"),
            Int(obj, "index") ?? 0,
            Bool(obj, "exact") ?? false);
        return query.IsEmpty ? null : query;
    }

    /// <summary>Same fields from a query string (GET /api/find?text=…).</summary>
    public static ElementQuery? From(NameValueCollection? query)
    {
        if (query == null)
            return null;

        var parsed = new ElementQuery(
            NonEmpty(query["automationId"]),
            NonEmpty(query["text"]),
            NonEmpty(query["type"]),
            NonEmpty(query["placeholder"]),
            int.TryParse(query["id"], out var id) ? id : null,
            int.TryParse(query["index"], out var index) ? index : 0,
            query["exact"] is { } exact && (exact == "1" || exact.Equals("true", StringComparison.OrdinalIgnoreCase)));
        return parsed.IsEmpty ? null : parsed;
    }

    public string Describe()
    {
        var parts = new List<string>();
        if (Id != null) parts.Add($"id={Id}");
        if (AutomationId != null) parts.Add($"automationId=\"{AutomationId}\"");
        if (Text != null) parts.Add($"text=\"{Text}\"");
        if (Placeholder != null) parts.Add($"placeholder=\"{Placeholder}\"");
        if (Type != null) parts.Add($"type={Type}");
        if (Index != 0) parts.Add($"index={Index}");
        return string.Join(" ", parts);
    }

    static string? Str(JsonObject obj, string name) =>
        obj[name] is JsonValue value && value.TryGetValue<string>(out var s) ? s
        : obj[name] is JsonValue number ? number.ToJsonString().Trim('"')
        : null;

    static int? Int(JsonObject obj, string name)
    {
        if (obj[name] is not JsonValue value)
            return null;
        if (value.TryGetValue<int>(out var i))
            return i;
        if (value.TryGetValue<double>(out var d))
            return (int)d;
        return value.TryGetValue<string>(out var s) && int.TryParse(s, out var parsed) ? parsed : null;
    }

    static bool? Bool(JsonObject obj, string name) =>
        obj[name] is JsonValue value && value.TryGetValue<bool>(out var b) ? b : null;

    static string? NonEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;
}

/// <summary>How well a candidate matched: exact matches always outrank substring ones.</summary>
internal enum MatchKind
{
    None = 0,
    Contains = 1,
    Exact = 2,
}

/// <summary>Text comparison rules shared by every remote lookup.</summary>
internal static class TextMatch
{
    /// <summary>Case-insensitive, whitespace-collapsed (newlines and non-breaking spaces included).</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return "";

        var buffer = new System.Text.StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c) || c == ' ')
            {
                pendingSpace = buffer.Length > 0;
                continue;
            }
            if (pendingSpace)
            {
                buffer.Append(' ');
                pendingSpace = false;
            }
            buffer.Append(char.ToLowerInvariant(c));
        }
        return buffer.ToString();
    }

    /// <summary>Exact (normalized) equality, else substring — unless <paramref name="exactOnly"/>.</summary>
    public static MatchKind Compare(string? candidate, string wanted, bool exactOnly)
    {
        var have = Normalize(candidate);
        var want = Normalize(wanted);
        if (have.Length == 0 || want.Length == 0)
            return MatchKind.None;
        if (have == want)
            return MatchKind.Exact;
        return !exactOnly && have.Contains(want, StringComparison.Ordinal) ? MatchKind.Contains : MatchKind.None;
    }

    /// <summary>Best match among several texts of one element (its text, placeholder, description…).</summary>
    public static MatchKind Best(IEnumerable<string?> candidates, string wanted, bool exactOnly)
    {
        var best = MatchKind.None;
        foreach (var candidate in candidates)
        {
            var kind = Compare(candidate, wanted, exactOnly);
            if (kind > best)
                best = kind;
            if (best == MatchKind.Exact)
                break;
        }
        return best;
    }

    /// <summary>
    /// Ranks matches for index-based picking: exact before substring, on-screen before
    /// off-screen, otherwise the original (topmost root first, then tree) order.
    /// </summary>
    public static List<T> Rank<T>(IEnumerable<T> items, Func<T, MatchKind> kind, Func<T, bool> onScreen) =>
        items.Select((item, order) => (item, order))
            .Where(t => kind(t.item) != MatchKind.None)
            .OrderByDescending(t => kind(t.item))
            .ThenByDescending(t => onScreen(t.item))
            .ThenBy(t => t.order)
            .Select(t => t.item)
            .ToList();
}

namespace Immons.Tools.Maui.Inspector.Features.Cookbook;

/// <summary>
/// Which resources the cookbook lists (everything but controls): the include list (when given)
/// admits, the exclude list vetoes. Entries are prefixes matched against a resource key
/// ("Brand."), its dictionary file ("Resources/Styles/Legacy/"), an image or font file name,
/// or a style's target type. "colors:Gray" scopes an entry to one section id; "images:*" means the
/// whole section.
/// </summary>
internal sealed class ResourceFilter
{
    readonly IReadOnlyList<(string? Section, string Prefix)> _included;
    readonly IReadOnlyList<(string? Section, string Prefix)> _excluded;

    public ResourceFilter(CookbookOptions options)
    {
        _included = options.IncludedResources.Select(Parse).Where(e => e.Prefix.Length > 0).ToList();
        _excluded = options.ExcludedResources.Select(Parse).Where(e => e.Prefix.Length > 0).ToList();
    }

    public bool Allows(string sectionId, params string?[] keys)
    {
        var scoped = _included.Where(e => e.Section == null || e.Section == sectionId).ToList();
        if (scoped.Count > 0 && !scoped.Any(e => Matches(keys, e.Prefix)))
            return false;
        return !_excluded.Any(e => (e.Section == null || e.Section == sectionId) && Matches(keys, e.Prefix));
    }

    const string Wildcard = "*";

    static bool Matches(string?[] keys, string prefix) =>
        prefix == Wildcard
        || keys.Any(key => key != null && Normalize(key).StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    /// <summary>"styles:Legacy." → (styles, Legacy.); "images:*" or "images:" → the whole section; a lone "Legacy." applies everywhere.</summary>
    static (string? Section, string Prefix) Parse(string entry)
    {
        var text = entry.Trim();
        var colon = text.IndexOf(':');
        if (colon > 0 && !text[..colon].Contains('/') && !text[..colon].Contains('.'))
        {
            var prefix = Normalize(text[(colon + 1)..]);
            return (text[..colon].ToLowerInvariant(), prefix.Length == 0 ? Wildcard : prefix);
        }
        return (null, Normalize(text));
    }

    static string Normalize(string value) => value.Trim().Replace('\\', '/').TrimStart('/');
}

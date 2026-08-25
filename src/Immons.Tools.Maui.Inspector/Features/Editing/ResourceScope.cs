namespace Immons.Tools.Maui.Inspector.Features.Editing;

/// <summary>One reachable resource dictionary and the XAML file the updater would patch for it.</summary>
internal sealed record ResourceScope(string Name, ResourceDictionary Dictionary, string? Source)
{
    /// <summary>The source without its ";assembly=…" suffix — what a person reads as the file.</summary>
    public string Label => Source?.Split(';')[0].TrimStart('/') is { Length: > 0 } file ? file : Name;

    /// <summary>
    /// Entries ordered by key. Keys/TryGetValue expose the content of a Source-loaded dictionary
    /// (kept in a private merged instance) which the public enumerator skips — hence no foreach.
    /// </summary>
    public IEnumerable<(string Key, object? Value)> Entries()
    {
        // An edited entry of a Source-loaded dictionary lives in both the outer and the inner
        // instance — Keys lists it twice, TryGetValue answers the same value for both.
        foreach (var key in Dictionary.Keys.Distinct().OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
        {
            object? value;
            try
            {
                if (!Dictionary.TryGetValue(key, out value))
                    continue;
            }
            catch
            {
                continue; // a dictionary can throw while resolving a DynamicResource — skip the entry
            }
            yield return (key, value);
        }
    }
}

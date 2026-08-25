namespace Immons.Tools.Maui.Inspector.Features.Cookbook;

/// <summary>
/// Where each bundled image came from — "beer" → "Resources/Images/2.0/beer.svg" — written into
/// the app by the package's build targets (the bundle itself keeps file names only). Empty when
/// the app was built without them; the Images section then knows names alone.
/// </summary>
internal static class ImageSourceManifest
{
    public const string ResourceName = "maui-inspector-images.txt";

    static readonly Lazy<IReadOnlyDictionary<string, string>> Entries = new(Load);

    /// <summary>The source path of a bundled image, by its file name (with or without extension).</summary>
    public static string? SourceOf(string fileName)
    {
        var key = Path.GetFileNameWithoutExtension(fileName);
        return Entries.Value.GetValueOrDefault(key);
    }

    static IReadOnlyDictionary<string, string> Load()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var assembly in AppAssemblies.Own())
        {
            try
            {
                using var stream = assembly.GetManifestResourceStream(ResourceName);
                if (stream == null)
                    continue;
                using var reader = new StreamReader(stream);
                while (reader.ReadLine() is { } line)
                {
                    var separator = line.IndexOf('|');
                    if (separator <= 0)
                        continue;
                    map.TryAdd(line[..separator].Trim(), line[(separator + 1)..].Trim().Replace('\\', '/'));
                }
            }
            catch
            {
                // a reflection-hostile assembly has no manifest either
            }
        }
        return map;
    }
}

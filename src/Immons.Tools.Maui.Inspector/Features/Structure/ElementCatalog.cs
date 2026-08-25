using System.Reflection;

namespace Immons.Tools.Maui.Inspector.Features.Structure;

/// <summary>
/// Flat, searchable catalog: a curated set of MAUI built-ins plus every public, concrete
/// <see cref="View"/> with a parameterless constructor found in the app's own assemblies.
/// </summary>
internal sealed class ElementCatalog : IElementCatalog
{
    static readonly (Type Type, string Description, bool Container)[] BuiltIns =
    [
        (typeof(Grid), "Rows/columns layout — children placed with Grid.Row / Grid.Column.", true),
        (typeof(VerticalStackLayout), "Stacks children top to bottom.", true),
        (typeof(HorizontalStackLayout), "Stacks children left to right.", true),
        (typeof(FlexLayout), "Wrapping/flexible layout modeled after CSS flexbox.", true),
        (typeof(AbsoluteLayout), "Positions children at explicit coordinates or proportions.", true),
        (typeof(Border), "Single child with stroke, corner radius and background.", true),
        (typeof(ContentView), "Single-child container — a styling/composition wrapper.", true),
        (typeof(ScrollView), "Makes its single child scrollable.", true),
        (typeof(Frame), "Legacy bordered container with shadow (prefer Border).", true),
        (typeof(Label), "Read-only text with spans and formatting.", false),
        (typeof(Button), "Tappable button with text.", false),
        (typeof(ImageButton), "Tappable button showing an image.", false),
        (typeof(Entry), "Single-line text input.", false),
        (typeof(Editor), "Multi-line text input.", false),
        (typeof(Image), "Bitmap or vector image.", false),
        (typeof(BoxView), "Solid rectangle — separators and placeholders.", false),
        (typeof(CheckBox), "Boolean check box.", false),
        (typeof(Switch), "Boolean on/off switch.", false),
        (typeof(Slider), "Double value picked by dragging.", false),
        (typeof(Stepper), "Double value changed in +/- steps.", false),
        (typeof(ProgressBar), "Read-only progress from 0 to 1.", false),
        (typeof(ActivityIndicator), "Spinner shown while something is running.", false),
        (typeof(DatePicker), "Date selection.", false),
        (typeof(TimePicker), "Time selection.", false),
        (typeof(Picker), "Single item picked from a list.", false),
        (typeof(SearchBar), "Text input styled for searching.", false),
        (typeof(WebView), "Embedded browser.", false),
    ];

    readonly object _gate = new();
    IReadOnlyList<CatalogEntry>? _all;
    Dictionary<string, Type>? _byName;

    public IReadOnlyList<CatalogEntry> All()
    {
        EnsureBuilt();
        return _all!;
    }

    public Type? Resolve(string typeName)
    {
        EnsureBuilt();
        return _byName!.GetValueOrDefault(typeName);
    }

    void EnsureBuilt()
    {
        lock (_gate)
        {
            if (_all != null)
                return;

            var entries = new List<CatalogEntry>();
            var byName = new Dictionary<string, Type>();

            foreach (var (type, description, container) in BuiltIns)
            {
                entries.Add(new CatalogEntry(type.Name, type.FullName!, description, container, IsCustom: false));
                byName[type.FullName!] = type;
            }

            foreach (var type in ScanAppViews())
            {
                if (byName.ContainsKey(type.FullName!))
                    continue;
                entries.Add(new CatalogEntry(
                    type.Name,
                    type.FullName!,
                    $"Custom control · {type.Namespace}",
                    IsContainer: typeof(Layout).IsAssignableFrom(type) || typeof(ContentView).IsAssignableFrom(type),
                    IsCustom: true));
                byName[type.FullName!] = type;
            }

            _all = entries;
            _byName = byName;
        }
    }

    /// <summary>Public, concrete, parameterless-constructible Views from non-framework assemblies.</summary>
    static IEnumerable<Type> ScanAppViews()
    {
        var result = new List<Type>();
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var name = assembly.GetName().Name ?? "";
            // The inspector's own packages by exact name — an app assembly may share the prefix.
            if (name.StartsWith("Microsoft.", StringComparison.Ordinal)
                || name.StartsWith("System", StringComparison.Ordinal)
                || name is "Immons.Tools.Maui.Inspector" or "Immons.Tools.Maui.Inspector.Persistency" or "Immons.Tools.Maui.Inspector.Extensions"
                || name.StartsWith("Mono.", StringComparison.Ordinal)
                || name.StartsWith("Java.", StringComparison.Ordinal)
                || name.StartsWith("Xamarin.", StringComparison.Ordinal)
                || name.StartsWith("SkiaSharp", StringComparison.Ordinal)
                || name is "mscorlib" or "netstandard" or "WindowsBase")
                continue;

            Type[] types;
            try
            {
                types = assembly.GetExportedTypes();
            }
            catch
            {
                continue; // reflection-hostile assembly — nothing to offer from it
            }

            foreach (var type in types)
            {
                if (!type.IsAbstract
                    && !type.IsGenericTypeDefinition
                    && typeof(View).IsAssignableFrom(type)
                    && type.GetConstructor(BindingFlags.Public | BindingFlags.Instance, Type.EmptyTypes) != null)
                    result.Add(type);
            }
        }

        result.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));
        return result;
    }
}

namespace Immons.Tools.Maui.Inspector.Features.Cookbook;

/// <summary>
/// The property sheet as the cookbook's popup shows it: the control's own bindable properties
/// first, then an "Inherited" accordion — one toggle per section a base type contributes
/// (Layout, Appearance, Text…). A built-in control has no own section and keeps the plain sheet.
/// </summary>
internal static class CookbookPropertySections
{
    const string InheritedPrefix = "inherited:";
    const string OwnSuffix = " properties";
    const string AllProperties = "All properties";

    public static List<PropertySection> Arrange(List<PropertySection> sections)
    {
        var own = sections.Where(IsOwn).ToList();
        if (own.Count == 0)
            return sections;

        var result = new List<PropertySection>(own);
        var toggles = SectionBuilder.New("Inherited");
        var inherited = new List<PropertySection>();
        foreach (var section in sections.Where(s => !IsOwn(s)))
        {
            // Sections that already fold behind a toggle (All properties, ViewModel…) keep it.
            if (section.Group != null)
            {
                inherited.Add(section);
                continue;
            }
            var group = InheritedPrefix + section.Title;
            toggles.Rows.Add(new PropertyRow(section.Title,
                $"{section.Rows.Count} row{(section.Rows.Count == 1 ? "" : "s")}", TogglesGroup: group));
            inherited.Add(section with { Group = group });
        }

        if (toggles.Rows.Count > 0)
            result.Add(toggles);
        result.AddRange(inherited);
        return result;
    }

    static bool IsOwn(PropertySection section) =>
        section.Title.EndsWith(OwnSuffix, StringComparison.Ordinal) && section.Title != AllProperties;
}

using System.Text;

namespace Immons.Tools.Maui.Inspector.Features.Cookbook;

/// <summary>
/// Collects items from the collectors, uniquifies their ids and orders the sections: recipe
/// sections first (the curated ones), then the built-ins in their fixed order.
/// </summary>
internal sealed class CookbookItemSink(ResourceFilter resources)
{
    readonly Dictionary<string, (string Title, List<CookbookItem> Items)> _sections = [];
    readonly List<string> _recipeOrder = [];
    readonly HashSet<string> _ids = [];

    public void Add(string sectionId, string sectionTitle, CookbookItem item)
    {
        // Controls have their own filter; everything else answers to the resource lists.
        if (item.Kind is not (CookbookKinds.Control or CookbookKinds.Custom)
            && !resources.Allows(sectionId, item.Name, item.Source, item.TargetType))
            return;

        if (!_sections.TryGetValue(sectionId, out var section))
        {
            section = (sectionTitle, []);
            _sections[sectionId] = section;
            if (sectionId.StartsWith(CookbookSections.RecipePrefix, StringComparison.Ordinal))
                _recipeOrder.Add(sectionId);
        }
        section.Items.Add(item with { Id = UniqueId(item.Kind, item.Name), Section = sectionId });
    }

    public void Add(string sectionId, CookbookItem item) => Add(sectionId, CookbookSections.TitleOf(sectionId), item);

    public IReadOnlyList<CookbookSection> ToSections()
    {
        var result = new List<CookbookSection>();
        foreach (var id in _recipeOrder.Concat(CookbookSections.Order))
        {
            if (_sections.TryGetValue(id, out var section) && section.Items.Count > 0)
                result.Add(new CookbookSection(id, section.Title, section.Items));
        }
        return result;
    }

    string UniqueId(string kind, string name)
    {
        var slug = new StringBuilder(kind.Length + name.Length + 1).Append(kind).Append('-');
        foreach (var ch in name)
            slug.Append(char.IsLetterOrDigit(ch) ? ch : '-');
        var candidate = slug.ToString();
        for (var n = 2; !_ids.Add(candidate); n++)
            candidate = $"{slug}-{n}";
        return candidate;
    }
}

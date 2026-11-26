namespace Immons.Tools.Maui.Inspector.Features.VisualTree;

internal sealed class TreeNode
{
    public required VisualElement Element { get; init; }
    public TreeNode? Parent { get; init; }
    public List<TreeNode> Children { get; } = [];

    /// <summary>What a top-level node is when it is not the window's page — "window overlay".</summary>
    public string? Note { get; init; }

    public string Label => Note is null ? Describe(Element, Children.Count) : $"{Describe(Element, Children.Count)} · {Note}";

    public static List<TreeNode> Build(IEnumerable<VisualElement> roots, Func<VisualElement, string?>? note = null) =>
        roots.Select(r => BuildNode(r, null, note?.Invoke(r))).ToList();

    static TreeNode BuildNode(VisualElement element, TreeNode? parent, string? note = null)
    {
        var node = new TreeNode { Element = element, Parent = parent, Note = note };
        foreach (var child in VisualTreeWalker.GetVisualChildren(element))
            node.Children.Add(BuildNode(child, node));
        return node;
    }

    public static TreeNode? Find(IEnumerable<TreeNode> roots, VisualElement element)
    {
        foreach (var root in roots)
        {
            if (ReferenceEquals(root.Element, element))
                return root;
            if (Find(root.Children, element) is { } found)
                return found;
        }
        return null;
    }

    static string Describe(VisualElement element, int childCount)
    {
        var name = element.GetType().Name;

        string? detail = element switch
        {
            ContentPage p when !string.IsNullOrEmpty(p.Title) => Quote(p.Title),
            Label { FormattedText.Spans.Count: > 0 } fl =>
                Quote(string.Concat(fl.FormattedText.Spans.Select(s => s.Text))),
            Label l when !string.IsNullOrEmpty(l.Text) => Quote(l.Text),
            Button b when !string.IsNullOrEmpty(b.Text) => Quote(b.Text),
            Entry e when !string.IsNullOrEmpty(e.Text) => Quote(e.Text),
            Entry e when !string.IsNullOrEmpty(e.Placeholder) => Quote(e.Placeholder),
            Editor e when !string.IsNullOrEmpty(e.Text) => Quote(e.Text),
            Image i when i.Source is FileImageSource f => f.File,
            _ => null,
        };

        var id = ElementInfo.IdTag(element);
        var suffix = childCount > 0 ? $"({childCount})" : null;

        return string.Join(" ", new[] { name, id, detail, suffix }.Where(s => !string.IsNullOrEmpty(s)));
    }

    static string Quote(string text)
    {
        text = ValueFormatter.EscapeIconGlyphs(text.Replace('\n', ' '));
        if (text.Length > 24)
            text = text[..24] + "…";
        return $"“{text}”";
    }
}

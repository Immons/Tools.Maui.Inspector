using System.Text.Json.Nodes;

namespace Immons.Tools.Maui.Inspector.Features.RemoteControl;

/// <summary>One element found on screen — a MAUI element or, failing that, a native view.</summary>
internal sealed class ElementHit
{
    public VisualElement? Element { get; init; }
    public NativeHit? Native { get; init; }
    public required string Type { get; init; }
    public string? AutomationId { get; init; }
    public string? Text { get; init; }
    public Rect? Bounds { get; init; }

    /// <summary>The part of <see cref="Bounds"/> not clipped away by scroll viewports and the window.</summary>
    public Rect Visible { get; init; }

    public bool OnScreen { get; init; }
    public bool Enabled { get; init; }
    public bool Focused { get; init; }
    public int? Id { get; init; }
    public MatchKind Kind { get; init; }

    /// <summary>Where a finger goes: the center of the visible part.</summary>
    public Point TapPoint => Visible.Width > 0 && Visible.Height > 0 ? Visible.Center : Bounds?.Center ?? Point.Zero;

    /// <summary>The visible part covers the element's center — a tap lands where the user expects.</summary>
    public bool CenterVisible => Bounds is { } b && OnScreen && Visible.Contains(b.Center);

    public JsonObject ToJson(bool? hittable = null)
    {
        var json = new JsonObject
        {
            ["type"] = Type,
            ["source"] = Element != null ? "maui" : "native",
        };
        if (Id != null) json["id"] = Id;
        if (AutomationId != null) json["automationId"] = AutomationId;
        if (Text != null) json["text"] = Text;
        if (Bounds is { } b) json["bounds"] = RectJson(b);
        json["visibleBounds"] = RectJson(Visible);
        json["center"] = new JsonObject { ["x"] = Round(TapPoint.X), ["y"] = Round(TapPoint.Y) };
        json["visible"] = OnScreen;
        json["enabled"] = Enabled;
        json["focused"] = Focused;
        if (hittable != null) json["hittable"] = hittable;
        json["match"] = Kind == MatchKind.Exact ? "exact" : "contains";
        return json;
    }

    static JsonObject RectJson(Rect r) => new()
    {
        ["x"] = Round(r.X),
        ["y"] = Round(r.Y),
        ["width"] = Round(r.Width),
        ["height"] = Round(r.Height),
    };

    static double Round(double v) => Math.Round(v, 1);
}

/// <summary>
/// Resolves <see cref="ElementQuery"/> selectors against the live tree (popups and modals first,
/// current pages only), then against native views. Main thread.
/// </summary>
internal sealed class ElementLocator(IActiveInspectorProvider inspectors, IElementRegistry elements)
{
    /// <summary>All matches, ranked: exact before substring, visible before hidden, topmost first.</summary>
    public List<ElementHit> FindAll(ElementQuery query)
    {
        if (inspectors.Current is not { } inspector)
            return [];

        var window = new Rect(0, 0, inspector.WindowSize.Width, inspector.WindowSize.Height);
        var hits = new List<ElementHit>();

        if (query.Id is { } id)
        {
            if (elements.Find(id) is { } element)
                hits.Add(Describe(inspector, element, inspector.BoundsOf(element), window, MatchKind.Exact));
            return hits;
        }

        foreach (var root in LiveTree.Roots(inspector))
            Walk(inspector, root, window, query, hits, transparent: false);

        var ranked = TextMatch.Rank(hits, h => h.Kind, h => h.OnScreen);
        if (ranked.Count == 0 && query.NativeSearchable)
            ranked = TextMatch.Rank(NativeUi.Find(query).Select(n => FromNative(n, window)), h => h.Kind, h => h.OnScreen);
        return ranked;
    }

    /// <summary>The match the query's index points at, preferring ones a finger can reach.</summary>
    public ElementHit? Find(ElementQuery query)
    {
        var all = FindAll(query);
        if (query.Index > 0)
            return query.Index < all.Count ? all[query.Index] : null;
        // Index 0: skip candidates something else covers (the page under a modal, a cell under a sheet).
        return all.FirstOrDefault(h => h.OnScreen && Hittable(h) != false) ?? all.FirstOrDefault();
    }

    public static bool? Hittable(ElementHit hit) =>
        !hit.OnScreen ? false
        : hit.Element != null ? NativeUi.Reaches(hit.Element, hit.TapPoint)
        : true;

    /// <summary>The Entry/Editor/SearchBar a text request means: the element itself or the first input inside it.</summary>
    public static InputView? InputOf(VisualElement element)
    {
        if (!element.IsVisible)
            return null;
        if (element is InputView input)
            return input;
        foreach (var child in LiveTree.Children(element))
        {
            if (InputOf(child) is { } nested)
                return nested;
        }
        return null;
    }

    /// <summary>
    /// The input a field's caption or container stands for: inside the element, else inside the
    /// closest parent that holds one ("Email" label → the Entry of the same field).
    /// </summary>
    public static InputView? InputNear(VisualElement element)
    {
        if (InputOf(element) is { } input)
            return input;
        var levels = 0;
        for (var parent = element.Parent; parent != null && levels < 4; parent = parent.Parent, levels++)
        {
            if (parent is Page)
                break;
            if (parent is VisualElement container && InputOf(container) is { } nearby)
                return nearby;
        }
        return null;
    }

    /// <summary>The input that currently has focus, if any.</summary>
    public InputView? FocusedInput()
    {
        if (inspectors.Current is not { } inspector)
            return null;
        foreach (var root in LiveTree.Roots(inspector))
        {
            if (Focused(root) is { } input)
                return input;
        }
        return null;

        static InputView? Focused(VisualElement element)
        {
            if (element is InputView { IsFocused: true } input)
                return input;
            foreach (var child in LiveTree.Children(element))
            {
                if (Focused(child) is { } nested)
                    return nested;
            }
            return null;
        }
    }

    void Walk(IWindowInspector inspector, VisualElement element, Rect clip, ElementQuery query, List<ElementHit> hits, bool transparent)
    {
        // Hidden subtrees are gone. Fully transparent ones are kept but never "visible": custom
        // fields often keep their real Entry at opacity 0 behind a floating label, and /api/text
        // must still reach it.
        if (!element.IsVisible)
            return;
        transparent |= element.Opacity <= 0.01;

        var bounds = inspector.BoundsOf(element);
        var kind = Match(element, query);
        if (kind != MatchKind.None)
            hits.Add(Describe(inspector, element, bounds, transparent ? Rect.Zero : clip, kind));

        var childClip = bounds is { } b && Clips(element) ? Intersect(clip, b) : clip;
        foreach (var child in LiveTree.Children(element))
            Walk(inspector, child, childClip, query, hits, transparent);
    }

    /// <summary>Containers whose content outside their bounds is not visible.</summary>
    static bool Clips(VisualElement element) => element switch
    {
        ScrollView or ItemsView or ListView or TableView or Page => true,
        Layout { IsClippedToBounds: true } => true,
        _ => false,
    };

    ElementHit Describe(IWindowInspector inspector, VisualElement element, Rect? bounds, Rect clip, MatchKind kind)
    {
        var visible = bounds is { } b ? Intersect(clip, b) : Rect.Zero;
        return new ElementHit
        {
            Element = element,
            Type = element.GetType().Name,
            AutomationId = string.IsNullOrEmpty(element.AutomationId) ? null : element.AutomationId,
            Text = Texts(element).FirstOrDefault(t => !string.IsNullOrEmpty(t)),
            Bounds = bounds,
            Visible = visible,
            OnScreen = visible.Width > 1 && visible.Height > 1,
            Enabled = element.IsEnabled,
            Focused = element.IsFocused,
            Id = elements.GetId(element),
            Kind = kind,
        };
    }

    static ElementHit FromNative(NativeHit native, Rect window)
    {
        var visible = Intersect(window, native.Bounds);
        return new ElementHit
        {
            Native = native,
            Type = native.Type,
            AutomationId = native.AutomationId,
            Text = native.Text,
            Bounds = native.Bounds,
            Visible = visible,
            OnScreen = visible.Width > 1 && visible.Height > 1,
            Enabled = native.Enabled,
            Kind = native.Kind,
        };
    }

    static MatchKind Match(VisualElement element, ElementQuery query)
    {
        var kind = MatchKind.Exact;

        if (query.AutomationId != null
            && !string.Equals(element.AutomationId, query.AutomationId, StringComparison.OrdinalIgnoreCase))
            return MatchKind.None;

        if (query.Type != null && !IsOfType(element.GetType(), query.Type))
            return MatchKind.None;

        if (query.Text != null)
        {
            var text = TextMatch.Best(Texts(element), query.Text, query.Exact);
            if (text == MatchKind.None)
                return MatchKind.None;
            kind = text < kind ? text : kind;
        }

        if (query.Placeholder != null)
        {
            var placeholder = TextMatch.Best(Placeholders(element), query.Placeholder, query.Exact);
            if (placeholder == MatchKind.None)
                return MatchKind.None;
            kind = placeholder < kind ? placeholder : kind;
        }

        return kind;
    }

    /// <summary>"Entry" matches Entry and every subclass of it; full names work too.</summary>
    static bool IsOfType(Type type, string wanted)
    {
        for (var current = type; current != null && current != typeof(object); current = current.BaseType)
        {
            if (string.Equals(current.Name, wanted, StringComparison.OrdinalIgnoreCase)
                || string.Equals(current.FullName, wanted, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>What the element shows as text — what a user would read to find it.</summary>
    internal static IEnumerable<string?> Texts(VisualElement element)
    {
        switch (element)
        {
            case Label { FormattedText.Spans.Count: > 0 } formatted:
                yield return string.Concat(formatted.FormattedText.Spans.Select(s => s.Text));
                break;
            case Label label:
                yield return label.Text;
                break;
            case Button button:
                yield return button.Text;
                break;
            case RadioButton radio:
                yield return radio.Content as string;
                break;
            case InputView input:
                yield return string.IsNullOrEmpty(input.Text) ? input.Placeholder : input.Text;
                break;
            case Picker picker:
                yield return picker.SelectedItem?.ToString() ?? picker.Title;
                break;
            case Page:
                break; // a page's title is not something to tap — /api/page reports it
            default:
                // Custom and third-party controls (SfTextInputLayout's Hint, a ContentView with Text…).
                foreach (var property in CaptionProperties(element.GetType()))
                    yield return property.GetValue(element) as string;
                break;
        }

        yield return SemanticProperties.GetDescription(element);
    }

    static readonly Dictionary<Type, System.Reflection.PropertyInfo[]> Captions = [];

    static System.Reflection.PropertyInfo[] CaptionProperties(Type type)
    {
        lock (Captions)
        {
            if (!Captions.TryGetValue(type, out var properties))
            {
                properties = new[] { "Text", "Hint", "Placeholder", "Title" }
                    .Select(name => type.GetProperty(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                    .Where(p => p != null && p.PropertyType == typeof(string) && p.GetIndexParameters().Length == 0)
                    .ToArray()!;
                Captions[type] = properties;
            }
            return properties;
        }
    }

    static IEnumerable<string?> Placeholders(VisualElement element) => element switch
    {
        InputView input => [input.Placeholder],
        Picker picker => [picker.Title],
        _ => [],
    };

    static Rect Intersect(Rect a, Rect b)
    {
        var left = Math.Max(a.Left, b.Left);
        var top = Math.Max(a.Top, b.Top);
        var right = Math.Min(a.Right, b.Right);
        var bottom = Math.Min(a.Bottom, b.Bottom);
        return right > left && bottom > top ? new Rect(left, top, right - left, bottom - top) : Rect.Zero;
    }
}

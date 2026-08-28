using Immons.Tools.Maui.Inspector.Features.VisualTree;
using Immons.Tools.Maui.Inspector.Features.XamlSync;
using Immons.Tools.Maui.Inspector.Shared.Storage;

namespace Immons.Tools.Maui.Inspector.Features.Structure;

/// <summary>
/// Replays persisted structural edits after an app restart: whenever a page appears, pending
/// adds whose parent lives on that page are re-created (with their recorded attributes) and
/// pending removes are re-applied. Ops target elements by XAML source identity, so a page
/// rebuilt from unchanged XAML matches; once the sync tool has written the edit into the
/// source and the app was rebuilt, the op simply no longer finds a target.
/// </summary>
internal sealed class StructureReplay(IElementCatalog catalog, IAddedElements added)
{
    readonly HashSet<string> _appliedThisSession = [];
    readonly object _gate = new();
    bool _hooked;

    /// <summary>Idempotent; call once a Window exists (Application.Current is set by then).</summary>
    public void Hook()
    {
        lock (_gate)
        {
            if (_hooked || Application.Current is not { } app)
                return;
            _hooked = true;
            app.PageAppearing += (_, page) => OnPageAppearing(page);
        }
    }

    void OnPageAppearing(Page page)
    {
        List<StructureOp> ops = [];
        foreach (var json in InspectorStorage.Current.Structure.All())
        {
            if (StructureOp.FromJson(json) is { } op)
                ops.Add(op);
        }
        if (ops.Count == 0)
            return;

        // Moves must re-apply in the order they were made; adds/removes are order-insensitive
        // but sorting everything by creation time keeps the replay deterministic.
        ops.Sort(static (a, b) => a.Order.CompareTo(b.Order));

        // Let the page finish building its content before we walk it.
        page.Dispatcher.Dispatch(() => Apply(page, ops));
    }

    void Apply(Page page, List<StructureOp> ops)
    {
        Dictionary<string, VisualElement>? identities = null;

        foreach (var op in ops)
        {
            lock (_gate)
            {
                if (_appliedThisSession.Contains(op.Id))
                    continue;
            }

            var targetIdentity = op.Kind == StructureOp.KindAdd ? op.ParentIdentity : op.ElementIdentity;
            if (targetIdentity == null)
                continue;

            identities ??= IndexIdentities(page);
            if (!identities.TryGetValue(targetIdentity, out var target))
                continue;

            var applied = op.Kind switch
            {
                StructureOp.KindAdd => ReplayAdd(op, target),
                StructureOp.KindMove => ReplayMove(op, target, identities),
                StructureOp.KindReparent => ReplayReparent(op, target, identities),
                StructureOp.KindWrap => ReplayWrap(op, target),
                StructureOp.KindStyle => ReplayStyle(op, target),
                StructureOp.KindUnwrap => ReplayUnwrap(op, target),
                _ => ReplayRemove(op, target),
            };
            if (applied)
            {
                lock (_gate)
                {
                    _appliedThisSession.Add(op.Id);
                }
            }
        }
    }

    bool ReplayAdd(StructureOp op, VisualElement parent)
    {
        if (catalog.Resolve(op.TypeName) is not { } type)
            return false;

        View child;
        try
        {
            child = (View)Activator.CreateInstance(type)!;
        }
        catch
        {
            return false;
        }

        AttributeApplier.Apply(child, op.Attributes);
        if (ElementAttacher.Attach(parent, child) != null)
            return false;

        added.Register(child, op);
        return true;
    }

    /// <summary>Re-applies the extracted values directly — visually identical to the style.</summary>
    static bool ReplayStyle(StructureOp op, VisualElement element)
    {
        if (element.GetType().Name != op.ElementType)
            return false;
        AttributeApplier.Apply(element, op.Attributes);
        return true;
    }

    static bool ReplayRemove(StructureOp op, VisualElement element) =>
        element.GetType().Name == op.ElementType && ElementAttacher.Detach(element) >= 0;

    static bool ReplayMove(StructureOp op, VisualElement element, Dictionary<string, VisualElement> identities)
    {
        if (element.GetType().Name != op.ElementType
            || op.SiblingIdentity == null
            || !identities.TryGetValue(op.SiblingIdentity, out var sibling)
            || element is not View view
            || element.Parent is not Layout parent
            || !ReferenceEquals(sibling.Parent, parent)
            || sibling is not IView siblingView)
            return false;

        parent.Children.Remove(view);
        var siblingIndex = parent.Children.IndexOf(siblingView);
        if (siblingIndex < 0)
            return false;
        parent.Children.Insert(op.Before ? siblingIndex : siblingIndex + 1, view);
        return true;
    }

    static bool ReplayReparent(StructureOp op, VisualElement element, Dictionary<string, VisualElement> identities)
    {
        if (element.GetType().Name != op.ElementType
            || element is not View view
            || op.ParentIdentity == null
            || !identities.TryGetValue(op.ParentIdentity, out var newParent)
            || ElementAttacher.Detach(element) < 0)
            return false;

        var index = -1;
        if (op.SiblingIdentity != null
            && identities.TryGetValue(op.SiblingIdentity, out var sibling)
            && ReferenceEquals(sibling.Parent, newParent)
            && newParent is Layout layout
            && sibling is IView siblingView)
        {
            var siblingIndex = layout.Children.IndexOf(siblingView);
            if (siblingIndex >= 0)
                index = op.Before ? siblingIndex : siblingIndex + 1;
        }

        return ElementAttacher.Attach(newParent, view, index) == null;
    }

    bool ReplayWrap(StructureOp op, VisualElement element)
    {
        if (element is not View view
            || element.GetType().Name != op.SiblingType
            || element.Parent is not VisualElement parent
            || catalog.Resolve(op.TypeName) is not { } type)
            return false;

        View wrapper;
        try
        {
            wrapper = (View)Activator.CreateInstance(type)!;
        }
        catch
        {
            return false;
        }

        AttributeApplier.Apply(wrapper, op.Attributes);
        var index = ElementAttacher.Detach(view);
        if (index < 0)
            return false;

        if (ElementAttacher.Attach(wrapper, view) != null
            || ElementAttacher.Attach(parent, wrapper, index) != null)
        {
            ElementAttacher.Detach(view);
            ElementAttacher.Attach(parent, view, index);
            return false;
        }

        added.Register(wrapper, op);
        return true;
    }

    static bool ReplayUnwrap(StructureOp op, VisualElement element)
    {
        if (element.GetType().Name != op.ElementType || element.Parent is not VisualElement parent)
            return false;

        var children = element switch
        {
            Layout layout => layout.Children.OfType<View>().ToList(),
            Border { Content: View b } => new List<View> { b },
            ScrollView { Content: View s } => new List<View> { s },
            ContentView { Content: View c } => new List<View> { c },
            _ => new List<View>(),
        };

        var index = ElementAttacher.Detach(element);
        if (index < 0)
            return false;

        var slot = index;
        foreach (var child in children)
        {
            ElementAttacher.Detach(child);
            if (ElementAttacher.Attach(parent, child, slot) != null)
                return false;
            slot++;
        }
        return true;
    }

    static Dictionary<string, VisualElement> IndexIdentities(Page page)
    {
        var map = new Dictionary<string, VisualElement>();

        void Walk(VisualElement element)
        {
            if (XamlSource.Describe(element) is { } identity)
                map.TryAdd(identity, element);
            foreach (var child in VisualTreeWalker.GetVisualChildren(element))
                Walk(child);
        }

        Walk(page);
        return map;
    }
}

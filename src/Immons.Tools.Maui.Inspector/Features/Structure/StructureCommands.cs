using Immons.Tools.Maui.Inspector.Features.History;
using Immons.Tools.Maui.Inspector.Features.VisualTree;
using Immons.Tools.Maui.Inspector.Features.XamlSync;
using Immons.Tools.Maui.Inspector.Shared.Storage;

namespace Immons.Tools.Maui.Inspector.Features.Structure;

/// <summary>
/// Structural edits driven from the web client. Every operation lands in three places: the live
/// tree (immediately), the structure store (replayed after an app restart) and the XAML change
/// log (written back to the sources by the sync tool).
/// </summary>
internal sealed class StructureCommands(
    IActiveInspectorProvider inspectors,
    IElementRegistry elements,
    IElementCatalog catalog,
    IEditHistory history,
    IXamlChangeLog xamlChanges,
    IAddedElements added) : IStructureCommands
{
    abstract record Undoable;

    sealed record UndoAdd(StructureOp Op, View Element, VisualElement Parent) : Undoable;

    sealed record UndoRemove(StructureOp? RemoveOp, StructureOp? AddOp, View Element, VisualElement Parent, int Index) : Undoable;

    sealed record UndoMove(
        StructureOp? MoveOp, StructureOp? PrevAddOp, StructureOp? NewAddOp,
        View Element, Layout Parent, int FromIndex, int ToIndex) : Undoable;

    sealed record UndoReparent(
        StructureOp? ReparentOp, StructureOp? ReverseOp, StructureOp? PrevAddOp, StructureOp? NewAddOp,
        View Element, VisualElement OldParent, int OldIndex,
        VisualElement NewParent, int NewIndex) : Undoable;

    sealed record UndoWrap(StructureOp Op, View Wrapper, View Element, VisualElement Parent, int Index) : Undoable;

    sealed record UndoUnwrapWrapper(StructureOp Op, View Wrapper, View? Inner, VisualElement Parent, int Index) : Undoable;

    sealed record UndoUnwrapContainer(StructureOp? Op, View Container, VisualElement Parent, int Index, List<View> Children) : Undoable;

    sealed record UndoExtractStyle(StructureOp Op, View Element, Page Page, StyleExtractor.Extraction Extraction) : Undoable;

    readonly Dictionary<long, Undoable> _undoables = [];
    readonly Dictionary<long, Undoable> _redoables = [];
    readonly object _gate = new();

    public (int Id, string? Error) Add(int parentId, string typeName)
    {
        if (elements.Find(parentId) is not { } parent)
            return (0, "parent element not found");
        return AddCore(parent, typeName, index: -1, sibling: null, before: false);
    }

    public (int Id, string? Error) AddAt(Point windowPoint, string typeName)
    {
        if (inspectors.Current is not { } inspector)
            return (0, "no active window");

        var hit = HitTester.HitTest(inspector.Roots, windowPoint, inspector.BoundsOf);
        if (hit == null)
            return (0, "nothing under the drop point");

        // Climb to the nearest container that can actually take a child.
        for (var candidate = hit; candidate != null; candidate = candidate.Parent as VisualElement)
        {
            if (!CanAcceptChild(candidate))
                continue;

            var (index, sibling, before) = InsertionAtPoint(candidate, windowPoint, inspector);
            return AddCore(candidate, typeName, index, sibling, before);
        }

        return (0, "no container under the drop point");
    }

    public (Rect Bounds, string Label, IReadOnlyList<Rect> Children)? DropTargetAt(Point windowPoint)
    {
        if (inspectors.Current is not { } inspector
            || HitTester.HitTest(inspector.Roots, windowPoint, inspector.BoundsOf) is not { } hit)
            return null;

        for (var candidate = hit; candidate != null; candidate = candidate.Parent as VisualElement)
        {
            if (!CanAcceptChild(candidate) || inspector.BoundsOf(candidate) is not { } bounds)
                continue;

            // Sibling rects feed the panel's snap lines while dragging.
            var children = new List<Rect>();
            if (candidate is Layout layout)
            {
                foreach (var child in layout.Children.Take(60))
                {
                    if (child is VisualElement ve && inspector.BoundsOf(ve) is { } childBounds)
                        children.Add(childBounds);
                }
            }
            return (bounds, candidate.GetType().Name, children);
        }
        return null;
    }

    static bool CanAcceptChild(VisualElement candidate) => candidate switch
    {
        Layout => true,
        Border border => border.Content == null,
        ScrollView scroll => scroll.Content == null,
        ContentView view => view.Content == null,
        ContentPage page => page.Content == null,
        _ => false,
    };

    /// <summary>Child position matching the drop point: by X in horizontal stacks, by Y elsewhere.</summary>
    static (int Index, VisualElement? Sibling, bool Before) InsertionAtPoint(
        VisualElement container, Point point, IWindowInspector inspector)
    {
        if (container is not Layout layout || layout.Children.Count == 0)
            return (-1, null, false);

        var horizontal = layout is HorizontalStackLayout;
        var index = 0;
        foreach (var child in layout.Children)
        {
            if (child is not VisualElement ve || inspector.BoundsOf(ve) is not { } bounds)
                break;
            var center = horizontal ? bounds.X + bounds.Width / 2 : bounds.Y + bounds.Height / 2;
            if ((horizontal ? point.X : point.Y) < center)
                break;
            index++;
        }

        if (index < layout.Children.Count)
            return (index, layout.Children[index] as VisualElement, true);
        return (-1, layout.Children[^1] as VisualElement, false); // append, anchored after the last child
    }

    (int Id, string? Error) AddCore(VisualElement parent, string typeName, int index, VisualElement? sibling, bool before)
    {
        if (catalog.Resolve(typeName) is not { } type)
            return (0, $"unknown control type: {typeName}");

        View child;
        try
        {
            child = (View)Activator.CreateInstance(type)!;
        }
        catch (Exception ex)
        {
            return (0, $"could not create {type.Name}: {ex.Message}");
        }

        ApplyPlaceholderDefaults(child);

        if (ElementAttacher.Attach(parent, child, index) is { } attachError)
            return (0, attachError);

        return FinishAdd(parent, child, SeedAttributes(child), snippetXml: null, snippetXmlns: null, sibling, before, $"Add {type.Name}");
    }

    public (int Id, string? Error) Paste(int targetId, int sourceId, bool force)
    {
        if (elements.Find(sourceId) is not View source || source.Window == null)
            return (0, "the copied element is no longer in the tree");
        if (elements.Find(targetId) is not { } target)
            return (0, "paste target not found");
        if (ElementCloner.Clone(source, force) is not { } cloned)
            return (0, $"{source.GetType().Name} cannot be recreated (no parameterless constructor)");

        // Paste into the target itself when it takes children, otherwise into its nearest
        // container ancestor.
        for (var candidate = target; candidate != null; candidate = candidate.Parent as VisualElement)
        {
            if (!CanAcceptChild(candidate))
                continue;

            var sibling = candidate is Layout { Children.Count: > 0 } layout
                ? layout.Children[^1] as VisualElement
                : null;
            if (ElementAttacher.Attach(candidate, cloned.Element) is { } attachError)
                return (0, attachError);
            return FinishAdd(candidate, cloned.Element, cloned.Attributes, cloned.ChildrenXml, cloned.XmlnsMap,
                sibling, before: false, $"Paste {source.GetType().Name}", deepCopy: force);
        }

        return (0, "no container can take the pasted element");
    }

    /// <summary>Shared tail of add/paste: the persisted op, XAML insert, history and undo.</summary>
    (int Id, string? Error) FinishAdd(
        VisualElement parent, View child, Dictionary<string, string> attributes, string? snippetXml,
        Dictionary<string, string>? snippetXmlns, VisualElement? sibling, bool before, string actionLabel,
        bool deepCopy = false)
    {
        var type = child.GetType();
        string? siblingIdentity = null;
        string siblingType = "";
        string? siblingOpId = null;
        if (sibling != null && XamlSource.Describe(sibling) is { } identity)
        {
            siblingIdentity = identity;
            siblingType = sibling.GetType().Name;
        }
        else if (sibling != null && added.Find(sibling) is { } siblingOp)
        {
            siblingOpId = siblingOp.Id;
        }

        var op = new StructureOp(
            Guid.NewGuid().ToString("N"),
            StructureOp.KindAdd,
            XamlSource.Describe(parent),
            parent.GetType().Name,
            type.FullName!,
            type.Assembly.GetName().Name ?? "",
            type.Name,
            ElementIdentity: null,
            attributes,
            siblingIdentity,
            siblingType,
            siblingOpId,
            before,
            Order: DateTime.UtcNow.Ticks,
            snippetXml,
            snippetXmlns,
            deepCopy);

        added.Register(child, op);
        InspectorStorage.Current.Structure.Save(op.Id, op.ToJson());
        xamlChanges.RecordInsert(op);
        xamlChanges.RefreshSubtreeSnippet(parent); // an add INSIDE pasted content re-renders its snippet

        history.Record(child, "Structure", actionLabel, "", "(added)");
        RememberUndo(new UndoAdd(op, child, parent));

        inspectors.Current?.RemoteAfterEdit();
        return (elements.GetId(child), null);
    }

    public string? Remove(int elementId)
    {
        if (elements.Find(elementId) is not View element)
            return "element not found (only Views can be removed)";
        if (element.Parent is not VisualElement parent)
            return "element has no removable parent";

        var addOp = added.Find(element);
        if (addOp is { Kind: StructureOp.KindWrap })
            return Unwrap(element, addOp); // removing a wrapper keeps its content

        var index = ElementAttacher.Detach(element);
        if (index < 0)
            return $"could not detach from {parent.GetType().Name}";

        StructureOp? removeOp = null;
        if (addOp != null)
        {
            // Inspector-created: take the pending insert back instead of recording a removal.
            InspectorStorage.Current.Structure.Delete(addOp.Id);
            xamlChanges.CancelInsert(addOp);
            added.Unregister(element);
        }
        else if (XamlSource.Describe(element) is { } identity)
        {
            removeOp = new StructureOp(
                Guid.NewGuid().ToString("N"),
                StructureOp.KindRemove,
                ParentIdentity: XamlSource.Describe(parent),
                parent.GetType().Name,
                element.GetType().FullName!,
                element.GetType().Assembly.GetName().Name ?? "",
                element.GetType().Name,
                identity,
                Attributes: [],
                Order: DateTime.UtcNow.Ticks);
            InspectorStorage.Current.Structure.Save(removeOp.Id, removeOp.ToJson());
            xamlChanges.RecordElementRemove(removeOp);
        }

        history.Record(element, "Structure", $"Remove {element.GetType().Name}", "(present)", "(removed)");
        RememberUndo(new UndoRemove(removeOp, addOp, element, parent, Math.Max(0, index)));

        xamlChanges.RefreshSubtreeSnippet(parent);
        inspectors.Current?.RemoteAfterEdit();
        return null;
    }

    public string? Move(int elementId, int delta)
    {
        if (elements.Find(elementId) is not View element)
            return "element not found (only Views can be moved)";
        if (element.Parent is not Layout parent)
            return $"{element.Parent?.GetType().Name ?? "the parent"} is not an ordered layout — position there is set by properties, not order";

        var index = parent.Children.IndexOf(element);
        var newIndex = index + delta;
        if (index < 0 || newIndex < 0 || newIndex >= parent.Children.Count)
            return "already at the edge of its parent";

        // The sibling being jumped over anchors the XAML move (before it when moving up).
        var sibling = parent.Children[newIndex] as VisualElement;
        parent.Children.RemoveAt(index);
        parent.Children.Insert(newIndex, element);

        StructureOp? moveOp = null;
        StructureOp? prevAddOp = null;
        StructureOp? newAddOp = null;
        if (added.Find(element) is { } addOp)
        {
            // Inspector-added: no XAML span to move — re-anchor the pending insert instead.
            prevAddOp = addOp;
            newAddOp = ReanchorAddOp(element, addOp, sibling, before: delta < 0);
        }
        else
        {
            moveOp = RecordMove(element, sibling, before: delta < 0);
        }

        history.Record(element, "Structure", $"Move {element.GetType().Name} {(delta < 0 ? "up" : "down")}", $"#{index}", $"#{newIndex}");
        RememberUndo(new UndoMove(moveOp, prevAddOp, newAddOp, element, parent, index, newIndex));

        xamlChanges.RefreshSubtreeSnippet(parent);
        inspectors.Current?.RemoteAfterEdit();
        return null;
    }

    public string? Reparent(int elementId, int newParentId, int siblingId, bool before)
    {
        if (elements.Find(elementId) is not View element)
            return "element not found (only Views can be moved)";
        if (elements.Find(newParentId) is not { } newParent)
            return "target parent not found";
        if (element.Parent is not VisualElement oldParent)
            return "element has no detachable parent";
        if (ReferenceEquals(newParent, element) || IsWithin(newParent, element))
            return "cannot move an element into its own subtree";
        if (ReferenceEquals(newParent, oldParent))
            return "already in that parent — reorder instead";

        // The old neighbour anchors the reverse write-back before anything mutates.
        var reverseOp = BuildReverseReparent(element, oldParent);

        var oldIndex = ElementAttacher.Detach(element);
        if (oldIndex < 0)
            return $"could not detach from {oldParent.GetType().Name}";

        var index = -1;
        VisualElement? sibling = null;
        if (siblingId != 0
            && elements.Find(siblingId) is { } s
            && ReferenceEquals(s.Parent, newParent)
            && newParent is Layout layout
            && s is IView siblingView)
        {
            var siblingIndex = layout.Children.IndexOf(siblingView);
            if (siblingIndex >= 0)
            {
                index = before ? siblingIndex : siblingIndex + 1;
                sibling = s;
            }
        }

        if (ElementAttacher.Attach(newParent, element, index) is { } attachError)
        {
            ElementAttacher.Attach(oldParent, element, oldIndex); // roll back
            return attachError;
        }

        StructureOp? reparentOp = null;
        StructureOp? prevAddOp = null;
        StructureOp? newAddOp = null;
        if (added.Find(element) is { } addOp)
        {
            // Inspector-added: repoint the pending insert at the new parent.
            prevAddOp = addOp;
            newAddOp = ReanchorAddOpToParent(element, addOp, newParent, sibling, before);
        }
        else if (XamlSource.Describe(element) is { } identity
            && XamlSource.Describe(newParent) is { } parentIdentity)
        {
            reparentOp = new StructureOp(
                Guid.NewGuid().ToString("N"),
                StructureOp.KindReparent,
                parentIdentity,
                newParent.GetType().Name,
                element.GetType().FullName!,
                element.GetType().Assembly.GetName().Name ?? "",
                element.GetType().Name,
                identity,
                Attributes: [],
                sibling != null ? XamlSource.Describe(sibling) : null,
                sibling?.GetType().Name ?? "",
                SiblingOpId: null,
                before,
                Order: DateTime.UtcNow.Ticks);
            InspectorStorage.Current.Structure.Save(reparentOp.Id, reparentOp.ToJson());
            xamlChanges.RecordElementReparent(reparentOp);
        }

        history.Record(element, "Structure",
            $"Move {element.GetType().Name} into {newParent.GetType().Name}", oldParent.GetType().Name, newParent.GetType().Name);
        RememberUndo(new UndoReparent(reparentOp, reverseOp, prevAddOp, newAddOp, element, oldParent, Math.Max(0, oldIndex), newParent, index));

        xamlChanges.RefreshSubtreeSnippet(oldParent);
        if (newParent is VisualElement newParentVe)
            xamlChanges.RefreshSubtreeSnippet(newParentVe);
        inspectors.Current?.RemoteAfterEdit();
        return null;
    }

    public (int Id, string? Error) Wrap(int elementId, string typeName)
    {
        if (elements.Find(elementId) is not View element)
            return (0, "element not found (only Views can be wrapped)");
        if (element.Parent is not VisualElement parent)
            return (0, "element has no wrappable parent");
        if (catalog.Resolve(typeName) is not { } type)
            return (0, $"unknown container type: {typeName}");
        if (added.Find(element) != null)
            return (0, "wrapping an inspector-added element isn't supported yet — add the container first, then drag the element into it");
        if (XamlSource.Describe(element) is not { } identity)
            return (0, "the element has no XAML source location — wrap works on elements defined in XAML");

        View wrapper;
        try
        {
            wrapper = (View)Activator.CreateInstance(type)!;
        }
        catch (Exception ex)
        {
            return (0, $"could not create {type.Name}: {ex.Message}");
        }

        var index = ElementAttacher.Detach(element);
        if (index < 0)
            return (0, $"could not detach from {parent.GetType().Name}");

        if (ElementAttacher.Attach(wrapper, element) is { } innerError)
        {
            ElementAttacher.Attach(parent, element, index); // roll back
            return (0, $"{type.Name} cannot hold the element: {innerError}");
        }
        if (ElementAttacher.Attach(parent, wrapper, index) is { } outerError)
        {
            ElementAttacher.Detach(element);
            ElementAttacher.Attach(parent, element, index); // roll back
            return (0, outerError);
        }

        var op = new StructureOp(
            Guid.NewGuid().ToString("N"),
            StructureOp.KindWrap,
            XamlSource.Describe(parent),
            parent.GetType().Name,
            type.FullName!,
            type.Assembly.GetName().Name ?? "",
            type.Name,
            identity,
            Attributes: [],
            SiblingType: element.GetType().Name, // wrapped element's tag, for the tool's verification
            Order: DateTime.UtcNow.Ticks);

        added.Register(wrapper, op);
        InspectorStorage.Current.Structure.Save(op.Id, op.ToJson());
        xamlChanges.RecordWrap(op);

        history.Record(wrapper, "Structure", $"Wrap {element.GetType().Name} in {type.Name}", "", "(wrapped)");
        RememberUndo(new UndoWrap(op, wrapper, element, parent, Math.Max(0, index)));

        inspectors.Current?.RemoteAfterEdit();
        return (elements.GetId(wrapper), null);
    }

    /// <summary>
    /// Element-centric unwrap: pulls the element one level up. When it is its parent's only
    /// child, the parent container disappears and the element takes its place; otherwise the
    /// element moves out to the grandparent, right before its (still populated) parent.
    /// </summary>
    public string? UnwrapElement(int elementId)
    {
        if (elements.Find(elementId) is not View element)
            return "element not found (only Views can be unwrapped)";

        // Targeting an inspector-added wrapper strips the wrapper itself.
        if (added.Find(element) is { Kind: StructureOp.KindWrap } wrapOp)
            return Unwrap(element, wrapOp);

        if (element.Parent is not View parentView)
            return element.Parent is VisualElement
                ? "the element sits directly on the page — nothing to unwrap"
                : "element has no parent to unwrap from";

        var siblingCount = parentView switch
        {
            Layout layout => layout.Children.Count,
            _ => 1,
        };

        if (siblingCount <= 1)
            return UnwrapContainer(elements.GetId(parentView));

        if (parentView.Parent is not VisualElement grandparent)
            return "the parent has no parent to promote into";
        return Reparent(elementId, elements.GetId(grandparent), elements.GetId(parentView), before: true);
    }

    /// <summary>Removes a container, promoting all its children into its own parent.</summary>
    string? UnwrapContainer(int containerId)
    {
        if (elements.Find(containerId) is not View element)
            return "container not found";

        var addOp = added.Find(element);
        if (addOp is { Kind: StructureOp.KindWrap })
            return Unwrap(element, addOp);
        if (addOp != null)
            return "an inspector-added container is removed with Remove — its children were added by the inspector too";
        if (element.Parent is not VisualElement parent)
            return "element has no parent to promote its children into";

        var children = element switch
        {
            Layout layout => layout.Children.OfType<View>().ToList(),
            Border { Content: View b } => new List<View> { b },
            ScrollView { Content: View s } => new List<View> { s },
            ContentView { Content: View c } => new List<View> { c },
            _ => new List<View>(),
        };
        if (children.Count == 0)
            return $"{element.GetType().Name} has no children — use Remove instead";

        var index = ElementAttacher.Detach(element);
        if (index < 0)
            return $"could not detach from {parent.GetType().Name}";

        var slot = index;
        foreach (var child in children)
        {
            ElementAttacher.Detach(child);
            if (ElementAttacher.Attach(parent, child, slot) is { } promoteError)
            {
                // Roll back what we can: container returns with the children still moved out.
                ElementAttacher.Attach(parent, element, index);
                return $"the parent cannot take the children: {promoteError}";
            }
            slot++;
        }

        StructureOp? op = null;
        if (XamlSource.Describe(element) is { } identity)
        {
            op = new StructureOp(
                Guid.NewGuid().ToString("N"),
                StructureOp.KindUnwrap,
                ParentIdentity: XamlSource.Describe(parent),
                parent.GetType().Name,
                element.GetType().FullName!,
                element.GetType().Assembly.GetName().Name ?? "",
                element.GetType().Name,
                identity,
                Attributes: [],
                Order: DateTime.UtcNow.Ticks);
            InspectorStorage.Current.Structure.Save(op.Id, op.ToJson());
            xamlChanges.RecordElementUnwrap(op);
        }

        history.Record(children[0], "Structure", $"Unwrap {element.GetType().Name}", "(wrapped)", "(unwrapped)");
        RememberUndo(new UndoUnwrapContainer(op, element, parent, Math.Max(0, index), children));
        inspectors.Current?.RemoteAfterEdit();
        return null;
    }

    /// <summary>Takes a wrapper out: its content returns to where the wrapper stood.</summary>
    string? Unwrap(View wrapper, StructureOp op)
    {
        if (wrapper.Parent is not VisualElement parent)
            return "wrapper has no parent";

        var inner = (wrapper as Layout)?.Children.OfType<View>().FirstOrDefault()
            ?? (wrapper as ContentView)?.Content
            ?? (wrapper as Border)?.Content as View
            ?? (wrapper as ScrollView)?.Content as View;

        var index = ElementAttacher.Detach(wrapper);
        if (index < 0)
            return $"could not detach the wrapper from {parent.GetType().Name}";

        if (inner != null)
        {
            ElementAttacher.Detach(inner);
            ElementAttacher.Attach(parent, inner, index);
        }

        InspectorStorage.Current.Structure.Delete(op.Id);
        xamlChanges.CancelWrap(op);
        added.Unregister(wrapper);

        history.Record(inner ?? wrapper, "Structure", $"Unwrap {op.ElementType}", "(wrapped)", "(unwrapped)");
        RememberUndo(new UndoUnwrapWrapper(op, wrapper, inner, parent, Math.Max(0, index)));
        inspectors.Current?.RemoteAfterEdit();
        return null;
    }

    public (int Id, string? Error) ExtractStyle(int elementId, string key, IReadOnlyCollection<string> propertyNames)
    {
        if (elements.Find(elementId) is not View element)
            return (0, "element not found");

        Page? page = null;
        for (Element? current = element; current != null; current = current.Parent)
        {
            if (current is Page p)
            {
                page = p;
                break;
            }
        }
        if (page == null)
            return (0, "the element is not on a page");

        var (extraction, error) = StyleExtractor.Extract(element, page, key, propertyNames);
        if (extraction == null)
            return (0, error);

        var op = StyleExtractor.BuildOp(element, page, key, extraction);
        InspectorStorage.Current.Structure.Save(op.Id, op.ToJson());
        new StyleExtractor(xamlChanges).WriteBack(element, extraction, op);

        history.Record(element, "Structure", $"Extract style {key}", "", "(extracted)");
        RememberUndo(new UndoExtractStyle(op, element, page, extraction));

        inspectors.Current?.RemoteAfterEdit();
        return (elementId, null);
    }

    /// <summary>True when the candidate sits anywhere inside the element's subtree.</summary>
    static bool IsWithin(Element candidate, VisualElement element)
    {
        for (Element? current = candidate; current != null; current = current.Parent)
        {
            if (ReferenceEquals(current, element))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Unpersisted template for undoing a reparent in the XAML: back into the old parent,
    /// next to the element's current neighbour when one is identifiable.
    /// </summary>
    StructureOp? BuildReverseReparent(View element, VisualElement oldParent)
    {
        if (added.Find(element) != null
            || XamlSource.Describe(element) is not { } identity
            || XamlSource.Describe(oldParent) is not { } oldParentIdentity)
            return null;

        VisualElement? neighbour = null;
        var before = false;
        if (oldParent is Layout layout && element is IView view)
        {
            var index = layout.Children.IndexOf(view);
            if (index > 0)
                neighbour = layout.Children[index - 1] as VisualElement;
            else if (index == 0 && layout.Children.Count > 1)
            {
                neighbour = layout.Children[1] as VisualElement;
                before = true;
            }
        }

        return new StructureOp(
            Guid.NewGuid().ToString("N"),
            StructureOp.KindReparent,
            oldParentIdentity,
            oldParent.GetType().Name,
            element.GetType().FullName!,
            element.GetType().Assembly.GetName().Name ?? "",
            element.GetType().Name,
            identity,
            Attributes: [],
            neighbour != null ? XamlSource.Describe(neighbour) : null,
            neighbour?.GetType().Name ?? "",
            SiblingOpId: null,
            before,
            Order: 0);
    }

    /// <summary>Repoints an added element's insert at another parent (and optional sibling).</summary>
    StructureOp? ReanchorAddOpToParent(View element, StructureOp addOp, VisualElement newParent, VisualElement? sibling, bool before)
    {
        if (XamlSource.Describe(newParent) is not { } parentIdentity)
            return null;

        var updated = addOp with
        {
            ParentIdentity = parentIdentity,
            ParentType = newParent.GetType().Name,
            SiblingIdentity = sibling != null ? XamlSource.Describe(sibling) : null,
            SiblingType = sibling?.GetType().Name ?? "",
            SiblingOpId = sibling != null && added.Find(sibling) is { } siblingOp ? siblingOp.Id : null,
            Before = before,
        };
        if (updated is { SiblingIdentity: null, SiblingOpId: not null })
            updated = updated with { SiblingType = "" };

        added.Register(element, updated);
        InspectorStorage.Current.Structure.Save(updated.Id, updated.ToJson());
        xamlChanges.RecordInsert(updated);
        return updated;
    }

    /// <summary>
    /// Points an inspector-added element's insert at its new neighbour: a source-backed sibling
    /// by XAML identity, another added element by its op. Null when the sibling has neither
    /// (the element still moves live, its snippet stays where it was).
    /// </summary>
    StructureOp? ReanchorAddOp(View element, StructureOp addOp, VisualElement? sibling, bool before)
    {
        StructureOp updated;
        if (sibling != null && XamlSource.Describe(sibling) is { } siblingIdentity)
            updated = addOp with
            {
                SiblingIdentity = siblingIdentity,
                SiblingType = sibling.GetType().Name,
                SiblingOpId = null,
                Before = before,
            };
        else if (sibling != null && added.Find(sibling) is { } siblingOp)
            updated = addOp with
            {
                SiblingIdentity = null,
                SiblingType = "",
                SiblingOpId = siblingOp.Id,
                Before = before,
            };
        else
            return null;

        added.Register(element, updated);
        InspectorStorage.Current.Structure.Save(updated.Id, updated.ToJson());
        xamlChanges.RecordInsert(updated);
        return updated;
    }

    /// <summary>Persists and write-backs one move; null when either side has no XAML identity.</summary>
    StructureOp? RecordMove(View element, VisualElement? sibling, bool before)
    {
        if (sibling == null
            || XamlSource.Describe(element) is not { } identity
            || XamlSource.Describe(sibling) is not { } siblingIdentity)
            return null; // inspector-added elements move live-only — their insert stays at the parent's end

        var op = new StructureOp(
            Guid.NewGuid().ToString("N"),
            StructureOp.KindMove,
            ParentIdentity: null,
            ParentType: "",
            element.GetType().FullName!,
            element.GetType().Assembly.GetName().Name ?? "",
            element.GetType().Name,
            identity,
            Attributes: [],
            siblingIdentity,
            sibling.GetType().Name,
            SiblingOpId: null,
            before,
            Order: DateTime.UtcNow.Ticks);

        InspectorStorage.Current.Structure.Save(op.Id, op.ToJson());
        xamlChanges.RecordElementMove(op);
        return op;
    }

    public bool Undo(long seq)
    {
        Undoable? undoable;
        lock (_gate)
        {
            if (!_undoables.Remove(seq, out undoable))
                return false;
        }

        switch (undoable)
        {
            case UndoAdd add:
                ElementAttacher.Detach(add.Element);
                InspectorStorage.Current.Structure.Delete(add.Op.Id);
                xamlChanges.CancelInsert(add.Op);
                added.Unregister(add.Element);
                xamlChanges.RefreshSubtreeSnippet(add.Parent);
                history.Record(add.Element, "Structure", $"Add {add.Element.GetType().Name}", "(added)", "(undone)", canUndo: false);
                break;

            case UndoRemove remove:
                if (ElementAttacher.Attach(remove.Parent, remove.Element, remove.Index) is { } error)
                    return Fail(error);
                if (remove.RemoveOp != null)
                {
                    InspectorStorage.Current.Structure.Delete(remove.RemoveOp.Id);
                    xamlChanges.RestoreElement(remove.RemoveOp);
                }
                if (remove.AddOp != null)
                {
                    added.Register(remove.Element, remove.AddOp);
                    InspectorStorage.Current.Structure.Save(remove.AddOp.Id, remove.AddOp.ToJson());
                    xamlChanges.RecordInsert(remove.AddOp);
                }
                xamlChanges.RefreshSubtreeSnippet(remove.Parent);
                history.Record(remove.Element, "Structure", $"Remove {remove.Element.GetType().Name}", "(removed)", "(restored)", canUndo: false);
                break;

            case UndoMove move:
                var current = move.Parent.Children.IndexOf(move.Element);
                if (current < 0 || move.FromIndex > move.Parent.Children.Count - 1)
                    return Fail("element left its parent since the move");
                move.Parent.Children.RemoveAt(current);
                move.Parent.Children.Insert(move.FromIndex, move.Element);
                if (move.MoveOp != null)
                {
                    InspectorStorage.Current.Structure.Delete(move.MoveOp.Id);
                    // The reverse jump over the same sibling, as a fresh (unpersisted) change.
                    xamlChanges.RecordElementMove(move.MoveOp with
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Before = !move.MoveOp.Before,
                    });
                }
                if (move is { NewAddOp: not null, PrevAddOp: not null })
                {
                    // Re-anchored insert: put the previous anchor back.
                    added.Register(move.Element, move.PrevAddOp);
                    InspectorStorage.Current.Structure.Save(move.PrevAddOp.Id, move.PrevAddOp.ToJson());
                    xamlChanges.RecordInsert(move.PrevAddOp);
                }
                xamlChanges.RefreshSubtreeSnippet(move.Parent);
                history.Record(move.Element, "Structure", $"Move {move.Element.GetType().Name}", "(moved)", "(undone)", canUndo: false);
                break;

            case UndoReparent reparent:
                if (ElementAttacher.Detach(reparent.Element) < 0)
                    return Fail("element left its parent since the reparent");
                if (ElementAttacher.Attach(reparent.OldParent, reparent.Element, reparent.OldIndex) is { } backError)
                    return Fail(backError);
                if (reparent.ReparentOp != null)
                {
                    InspectorStorage.Current.Structure.Delete(reparent.ReparentOp.Id);
                    if (reparent.ReverseOp != null)
                        xamlChanges.RecordElementReparent(reparent.ReverseOp);
                }
                if (reparent is { NewAddOp: not null, PrevAddOp: not null })
                {
                    added.Register(reparent.Element, reparent.PrevAddOp);
                    InspectorStorage.Current.Structure.Save(reparent.PrevAddOp.Id, reparent.PrevAddOp.ToJson());
                    xamlChanges.RecordInsert(reparent.PrevAddOp);
                }
                xamlChanges.RefreshSubtreeSnippet(reparent.OldParent);
                history.Record(reparent.Element, "Structure", $"Move {reparent.Element.GetType().Name} into", "(reparented)", "(undone)", canUndo: false);
                break;

            case UndoWrap wrap:
                if (ElementAttacher.Detach(wrap.Element) < 0 || ElementAttacher.Detach(wrap.Wrapper) < 0)
                    return Fail("wrapper changed since the wrap");
                if (ElementAttacher.Attach(wrap.Parent, wrap.Element, wrap.Index) is { } wrapBackError)
                    return Fail(wrapBackError);
                InspectorStorage.Current.Structure.Delete(wrap.Op.Id);
                xamlChanges.CancelWrap(wrap.Op);
                added.Unregister(wrap.Wrapper);
                history.Record(wrap.Element, "Structure", $"Wrap in {wrap.Op.ElementType}", "(wrapped)", "(undone)", canUndo: false);
                break;

            case UndoUnwrapWrapper rewrap:
                if (rewrap.Inner != null)
                {
                    ElementAttacher.Detach(rewrap.Inner);
                    ElementAttacher.Attach(rewrap.Wrapper, rewrap.Inner);
                }
                if (ElementAttacher.Attach(rewrap.Parent, rewrap.Wrapper, rewrap.Index) is { } rewrapError)
                    return Fail(rewrapError);
                added.Register(rewrap.Wrapper, rewrap.Op);
                InspectorStorage.Current.Structure.Save(rewrap.Op.Id, rewrap.Op.ToJson());
                xamlChanges.RecordWrap(rewrap.Op);
                history.Record(rewrap.Wrapper, "Structure", $"Unwrap {rewrap.Op.ElementType}", "(unwrapped)", "(undone)", canUndo: false);
                break;

            case UndoUnwrapContainer renest:
                foreach (var child in renest.Children)
                    ElementAttacher.Detach(child);
                foreach (var child in renest.Children)
                {
                    if (ElementAttacher.Attach(renest.Container, child) is { } renestError)
                        return Fail(renestError);
                }
                if (ElementAttacher.Attach(renest.Parent, renest.Container, renest.Index) is { } containerBackError)
                    return Fail(containerBackError);
                if (renest.Op != null)
                {
                    InspectorStorage.Current.Structure.Delete(renest.Op.Id);
                    xamlChanges.RestoreElementUnwrap(renest.Op);
                }
                history.Record(renest.Container, "Structure", $"Unwrap {renest.Container.GetType().Name}", "(unwrapped)", "(undone)", canUndo: false);
                break;

            case UndoExtractStyle extract:
                foreach (var (property, oldValue) in extract.Extraction.Extracted)
                    extract.Element.SetValue(property, oldValue);
                extract.Element.ClearValue(VisualElement.StyleProperty);
                extract.Page.Resources.Remove(extract.Extraction.Key);
                InspectorStorage.Current.Structure.Delete(extract.Op.Id);
                new StyleExtractor(xamlChanges).UndoWriteBack(extract.Element, extract.Extraction, extract.Op);
                history.Record(extract.Element, "Structure", $"Extract style {extract.Extraction.Key}", "(extracted)", "(undone)", canUndo: false);
                break;

            default:
                return false;
        }

        history.MarkUndone(seq);
        lock (_gate)
        {
            _redoables[seq] = undoable;
        }
        inspectors.Current?.RemoteAfterEdit();
        return true;

        static bool Fail(string message)
        {
            Console.WriteLine($"[MauiInspector] undo failed: {message}");
            return false;
        }
    }

    /// <summary>Re-applies a previously undone structural op — the inverse of the inverse.</summary>
    public bool Redo(long seq)
    {
        Undoable? undoable;
        lock (_gate)
        {
            if (!_redoables.Remove(seq, out undoable))
                return false;
        }

        switch (undoable)
        {
            case UndoAdd add:
                if (ElementAttacher.Attach(add.Parent, add.Element) != null)
                    return false;
                added.Register(add.Element, add.Op);
                InspectorStorage.Current.Structure.Save(add.Op.Id, add.Op.ToJson());
                xamlChanges.RecordInsert(add.Op);
                break;

            case UndoRemove remove:
                if (ElementAttacher.Detach(remove.Element) < 0)
                    return false;
                if (remove.AddOp != null)
                {
                    InspectorStorage.Current.Structure.Delete(remove.AddOp.Id);
                    xamlChanges.CancelInsert(remove.AddOp);
                    added.Unregister(remove.Element);
                }
                else if (remove.RemoveOp != null)
                {
                    InspectorStorage.Current.Structure.Save(remove.RemoveOp.Id, remove.RemoveOp.ToJson());
                    xamlChanges.RecordElementRemove(remove.RemoveOp);
                }
                xamlChanges.RefreshSubtreeSnippet(remove.Parent);
                break;

            case UndoMove move:
                var current = move.Parent.Children.IndexOf(move.Element);
                if (current < 0 || move.ToIndex > move.Parent.Children.Count - 1)
                    return false;
                move.Parent.Children.RemoveAt(current);
                move.Parent.Children.Insert(move.ToIndex, move.Element);
                if (move.MoveOp != null)
                {
                    InspectorStorage.Current.Structure.Save(move.MoveOp.Id, move.MoveOp.ToJson());
                    xamlChanges.RecordElementMove(move.MoveOp with { Id = Guid.NewGuid().ToString("N") });
                }
                if (move.NewAddOp != null)
                {
                    added.Register(move.Element, move.NewAddOp);
                    InspectorStorage.Current.Structure.Save(move.NewAddOp.Id, move.NewAddOp.ToJson());
                    xamlChanges.RecordInsert(move.NewAddOp);
                }
                xamlChanges.RefreshSubtreeSnippet(move.Parent);
                break;

            case UndoReparent reparent:
                if (ElementAttacher.Detach(reparent.Element) < 0)
                    return false;
                if (ElementAttacher.Attach(reparent.NewParent, reparent.Element, reparent.NewIndex) != null)
                    return false;
                if (reparent.ReparentOp != null)
                {
                    InspectorStorage.Current.Structure.Save(reparent.ReparentOp.Id, reparent.ReparentOp.ToJson());
                    xamlChanges.RecordElementReparent(reparent.ReparentOp with { Id = Guid.NewGuid().ToString("N") });
                }
                if (reparent.NewAddOp != null)
                {
                    added.Register(reparent.Element, reparent.NewAddOp);
                    InspectorStorage.Current.Structure.Save(reparent.NewAddOp.Id, reparent.NewAddOp.ToJson());
                    xamlChanges.RecordInsert(reparent.NewAddOp);
                }
                xamlChanges.RefreshSubtreeSnippet(reparent.OldParent);
                break;

            case UndoWrap wrap:
                if (ElementAttacher.Detach(wrap.Element) < 0)
                    return false;
                if (ElementAttacher.Attach(wrap.Wrapper, wrap.Element) != null
                    || ElementAttacher.Attach(wrap.Parent, wrap.Wrapper, wrap.Index) != null)
                    return false;
                added.Register(wrap.Wrapper, wrap.Op);
                InspectorStorage.Current.Structure.Save(wrap.Op.Id, wrap.Op.ToJson());
                xamlChanges.RecordWrap(wrap.Op);
                break;

            case UndoUnwrapWrapper rewrap:
                // Redo the unwrap: strip the wrapper again.
                if (ElementAttacher.Detach(rewrap.Wrapper) < 0)
                    return false;
                if (rewrap.Inner != null)
                {
                    ElementAttacher.Detach(rewrap.Inner);
                    ElementAttacher.Attach(rewrap.Parent, rewrap.Inner, rewrap.Index);
                }
                InspectorStorage.Current.Structure.Delete(rewrap.Op.Id);
                xamlChanges.CancelWrap(rewrap.Op);
                added.Unregister(rewrap.Wrapper);
                break;

            case UndoUnwrapContainer renest:
                var index = ElementAttacher.Detach(renest.Container);
                if (index < 0)
                    return false;
                var slot = index;
                foreach (var child in renest.Children)
                {
                    ElementAttacher.Detach(child);
                    ElementAttacher.Attach(renest.Parent, child, slot);
                    slot++;
                }
                if (renest.Op != null)
                {
                    InspectorStorage.Current.Structure.Save(renest.Op.Id, renest.Op.ToJson());
                    xamlChanges.RecordElementUnwrap(renest.Op);
                }
                break;

            case UndoExtractStyle extract:
                extract.Page.Resources[extract.Extraction.Key] = extract.Extraction.Style;
                foreach (var (property, _) in extract.Extraction.Extracted)
                    extract.Element.ClearValue(property);
                extract.Element.Style = extract.Extraction.Style;
                InspectorStorage.Current.Structure.Save(extract.Op.Id, extract.Op.ToJson());
                new StyleExtractor(xamlChanges).WriteBack(extract.Element, extract.Extraction, extract.Op);
                break;

            default:
                return false;
        }

        // The entry is back in the Ctrl+Z chain; its inverse returns to the undo table.
        lock (_gate)
        {
            _undoables[seq] = undoable;
        }
        history.MarkRedone(seq);
        inspectors.Current?.RemoteAfterEdit();
        return true;
    }

    void RememberUndo(Undoable undoable)
    {
        lock (_gate)
        {
            _undoables[history.LastSeq] = undoable;
        }
    }

    /// <summary>Fresh text controls get a visible placeholder so the add is instantly apparent.</summary>
    static void ApplyPlaceholderDefaults(View child)
    {
        switch (child)
        {
            case Label { Text: null or "" } label:
                label.Text = "Label";
                break;
            case Button { Text: null or "" } button:
                button.Text = "Button";
                break;
            case BoxView box when box.Color == null:
                box.Color = Colors.LightGray;
                box.HeightRequest = 24;
                break;
        }
    }

    /// <summary>Attributes matching the placeholder defaults, so the XAML snippet shows them too.</summary>
    static Dictionary<string, string> SeedAttributes(View child) => child switch
    {
        Label label => new() { ["Text"] = label.Text ?? "" },
        Button button => new() { ["Text"] = button.Text ?? "" },
        BoxView => new() { ["Color"] = "LightGray", ["HeightRequest"] = "24" },
        _ => [],
    };
}

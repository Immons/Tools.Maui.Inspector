namespace Immons.Tools.Maui.Inspector.Inspector;

internal sealed partial class WindowInspector
{

    bool _debugPaint;

    public bool DebugPaintActive => _debugPaint;

    /// <summary>Flutter-style debug paint: outlines every visible element on the overlay.</summary>
    public void SetDebugPaint(bool on)
    {
        _debugPaint = on;
        if (on && !IsShown && !EnsureRemoteOverlay())
            return;
        UpdateHighlight();
    }

    List<(Rect Rect, int Depth)> CollectGuides(Point origin)
    {
        var guides = new List<(Rect, int)>();

        void Walk(VisualElement element, int depth)
        {
            if (!element.IsVisible)
                return;
            if (GetRectInWindow(element) is { } r)
                guides.Add((new Rect(r.X - origin.X, r.Y - origin.Y, r.Width, r.Height), depth));
            foreach (var child in VisualTreeWalker.GetVisualChildren(element))
                Walk(child, depth + 1);
        }

        foreach (var root in RootElements())
            Walk(root, 0);
        return guides;
    }

    void UpdateHighlight()
    {
        if (_highlightLayer == null)
            return;

        // A removed element detaches from the window — drop the selection with it,
        // otherwise its last box would stay painted over the live app.
        if (_selected is { } selected && !ElementAttachment.IsAttached(selected))
        {
            _selected = null;
            _compare = null;
        }
        else if (_compare is { } compared && !ElementAttachment.IsAttached(compared))
        {
            _compare = null;
        }

        var layerOrigin = LayerOrigin;
        var guides = _debugPaint ? CollectGuides(layerOrigin) : null;

        if (_selected is not { } element || GetRectInWindow(element) is not { } bounds)
        {
            _highlightLayer.ShowBox(guides is { Count: > 0 }
                ? new BoxModel(Rect.Zero, Rect.Zero, Rect.Zero, "", Guides: guides)
                : null);
            return;
        }

        var margin = (element as View)?.Margin ?? new Thickness(0);
        var padding = ElementInfo.GetPadding(element) ?? new Thickness(0);

        var origin = layerOrigin;
        bounds = new Rect(bounds.X - origin.X, bounds.Y - origin.Y, bounds.Width, bounds.Height);

        var marginRect = new Rect(
            bounds.X - margin.Left, bounds.Y - margin.Top,
            bounds.Width + margin.HorizontalThickness, bounds.Height + margin.VerticalThickness);
        var contentRect = new Rect(
            bounds.X + padding.Left, bounds.Y + padding.Top,
            Math.Max(0, bounds.Width - padding.HorizontalThickness), Math.Max(0, bounds.Height - padding.VerticalThickness));

        var dims = $"{ValueFormatter.F(bounds.Width)} × {ValueFormatter.F(bounds.Height)}";

        Rect? compareBounds = null;
        IReadOnlyList<DistanceSegment>? distances = null;
        if (_compare is { } compare && GetRectInWindow(compare) is { } cb)
        {
            compareBounds = new Rect(cb.X - origin.X, cb.Y - origin.Y, cb.Width, cb.Height);
            distances = DistanceMath.Compute(bounds, compareBounds.Value);
        }

        _highlightLayer.ShowBox(new BoxModel(marginRect, bounds, contentRect, dims, compareBounds, distances, guides));
    }
}

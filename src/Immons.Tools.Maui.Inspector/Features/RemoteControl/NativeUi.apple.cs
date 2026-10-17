using CoreGraphics;
using Microsoft.Maui.Platform;
using UIKit;

namespace Immons.Tools.Maui.Inspector.Features.RemoteControl;

internal static partial class NativeUi
{
    public static partial List<NativeHit> Find(ElementQuery query)
    {
        var hits = new List<NativeHit>();
        foreach (var window in UIKitTouch.OrderedWindows())
            Walk(window, window, query, hits);
        return hits;
    }

    static void Walk(UIView view, UIWindow window, ElementQuery query, List<NativeHit> hits)
    {
        if (view.Hidden || view.Alpha < 0.01)
            return;

        var kind = Match(view, query);
        if (kind != MatchKind.None)
        {
            var rect = view.ConvertRectToView(view.Bounds, null);
            var visible = CGRect.Intersect(rect, window.Bounds);
            if (visible.Width > 1 && visible.Height > 1)
            {
                hits.Add(new NativeHit(view, view.Class.Name ?? view.GetType().Name, NullIfEmpty(view.AccessibilityIdentifier),
                    TextOf(view), new Rect(rect.X, rect.Y, rect.Width, rect.Height),
                    view is not UIControl control || control.Enabled, kind));
                // The outermost exact match is the tap target (a button, not its title label).
                if (kind == MatchKind.Exact)
                    return;
            }
        }

        foreach (var child in view.Subviews)
            Walk(child, window, query, hits);
    }

    static MatchKind Match(UIView view, ElementQuery query)
    {
        var kind = MatchKind.Exact;
        if (query.AutomationId != null)
        {
            if (!string.Equals(view.AccessibilityIdentifier, query.AutomationId, StringComparison.OrdinalIgnoreCase))
                return MatchKind.None;
        }
        if (query.Text != null)
        {
            var text = TextMatch.Best(Texts(view), query.Text, query.Exact);
            if (text == MatchKind.None)
                return MatchKind.None;
            if (text < kind)
                kind = text;
        }
        return kind;
    }

    static IEnumerable<string?> Texts(UIView view)
    {
        switch (view)
        {
            case UILabel label:
                yield return label.Text;
                break;
            case UIButton button:
                yield return button.CurrentTitle;
                break;
            case UITextField field:
                yield return field.Text;
                yield return field.Placeholder;
                break;
            case UITextView textView:
                yield return textView.Text;
                break;
        }
        yield return view.AccessibilityLabel;
    }

    static string? TextOf(UIView view) => Texts(view).FirstOrDefault(t => !string.IsNullOrEmpty(t));

    static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;

    static UIView? ViewOf(VisualElement element) =>
        element.Handler is IPlatformViewHandler handler ? handler.ContainerView ?? handler.PlatformView : null;

    public static partial bool? Reaches(VisualElement element, Point windowPoint)
    {
        if (ViewOf(element) is not { Window: not null } view)
            return null;
        var point = new CGPoint(windowPoint.X, windowPoint.Y);
        if (UIKitTouch.TargetWindow(point) is not { } window)
            return false;
        var hit = window.HitTest(window.ConvertPointFromWindow(point, null), null);
        if (hit == null)
            return false;
        return ReferenceEquals(hit, view) || hit.IsDescendantOfView(view) || view.IsDescendantOfView(hit);
    }

    public static partial NativeScrollable? FindScrollable(VisualElement? container)
    {
        if (container != null)
        {
            if (ViewOf(container) is not { } root)
                return null;
            return Describe(Scrollables(root).OrderByDescending(Area).FirstOrDefault());
        }

        foreach (var window in UIKitTouch.OrderedWindows())
        {
            if (Scrollables(window).OrderByDescending(Area).FirstOrDefault() is { } largest)
                return Describe(largest);
        }
        return null;
    }

    public static partial NativeScrollable? ScrollableAround(VisualElement element)
    {
        for (var view = ViewOf(element)?.Superview; view != null; view = view.Superview)
        {
            if (view is UIScrollView scroll && CanScroll(scroll))
                return Describe(scroll);
        }
        return null;
    }

    /// <summary>Visible, scrollable scroll views under the root, the root itself included.</summary>
    static IEnumerable<UIScrollView> Scrollables(UIView root)
    {
        var queue = new Queue<UIView>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var view = queue.Dequeue();
            if (view.Hidden || view.Alpha < 0.01)
                continue;
            if (view is UIScrollView scroll && CanScroll(scroll) && view.Window != null && Area(scroll) > 1)
                yield return scroll;
            foreach (var child in view.Subviews)
                queue.Enqueue(child);
        }
    }

    static bool CanScroll(UIScrollView scroll)
    {
        if (!scroll.ScrollEnabled || scroll is UITextView)
            return false;
        var inset = scroll.AdjustedContentInset;
        return scroll.ContentSize.Height + inset.Top + inset.Bottom > scroll.Bounds.Height + 1
            || scroll.ContentSize.Width + inset.Left + inset.Right > scroll.Bounds.Width + 1;
    }

    static double Area(UIScrollView scroll)
    {
        if (scroll.Window == null)
            return 0;
        var visible = CGRect.Intersect(scroll.ConvertRectToView(scroll.Bounds, null), scroll.Window.Bounds);
        return visible.Width * visible.Height;
    }

    static NativeScrollable? Describe(UIScrollView? scroll)
    {
        if (scroll?.Window == null)
            return null;
        var rect = CGRect.Intersect(scroll.ConvertRectToView(scroll.Bounds, null), scroll.Window.Bounds);
        return new NativeScrollable(scroll, new Rect(rect.X, rect.Y, rect.Width, rect.Height), scroll.Class.Name ?? scroll.GetType().Name);
    }

    public static partial ScrollOutcome ScrollBy(NativeScrollable scrollable, double dx, double dy)
    {
        var scroll = (UIScrollView)scrollable.View;
        var inset = scroll.AdjustedContentInset;
        var before = scroll.ContentOffset;
        var maxX = Math.Max(-inset.Left, scroll.ContentSize.Width + inset.Right - scroll.Bounds.Width);
        var maxY = Math.Max(-inset.Top, scroll.ContentSize.Height + inset.Bottom - scroll.Bounds.Height);
        var x = Math.Clamp(before.X + dx, -inset.Left, maxX);
        var y = Math.Clamp(before.Y + dy, -inset.Top, maxY);
        scroll.SetContentOffset(new CGPoint(x, y), false);
        // Lets lazily realized cells (CollectionView) appear before the caller looks again.
        scroll.LayoutIfNeeded();
        var after = scroll.ContentOffset;
        var moved = Math.Abs(after.X - before.X) > 0.5 || Math.Abs(after.Y - before.Y) > 0.5;
        return new ScrollOutcome(moved, after.X, after.Y);
    }

    public static partial bool Activate(object nativeView)
    {
        switch (nativeView)
        {
            case UITextField field:
                return field.BecomeFirstResponder();
            case UISwitch toggle:
                toggle.SetState(!toggle.On, true);
                toggle.SendActionForControlEvents(UIControlEvent.ValueChanged);
                return true;
            case UIControl control:
                control.SendActionForControlEvents(UIControlEvent.TouchUpInside);
                return true;
            default:
                return false;
        }
    }

    public static partial double Scale() => UIScreen.MainScreen.Scale;
}

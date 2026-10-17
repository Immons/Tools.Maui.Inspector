using Android.Views;
using Android.Widget;
using Microsoft.Maui.Platform;
using AView = Android.Views.View;

namespace Immons.Tools.Maui.Inspector.Features.RemoteControl;

internal static partial class NativeUi
{
    static float Density =>
        Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.Resources?.DisplayMetrics?.Density ?? 1;

    static AView? Decor => Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.Window?.DecorView;

    public static partial List<NativeHit> Find(ElementQuery query)
    {
        var hits = new List<NativeHit>();
        if (Decor is { } decor)
            Walk(decor, query, hits);
        return hits;
    }

    static void Walk(AView view, ElementQuery query, List<NativeHit> hits)
    {
        if (view.Visibility != ViewStates.Visible || view.Alpha < 0.01f)
            return;

        var kind = Match(view, query);
        if (kind != MatchKind.None && WindowRect(view) is { Width: > 1, Height: > 1 } rect)
        {
            hits.Add(new NativeHit(view, view.GetType().Name, view.ContentDescription, TextOf(view), rect, view.Enabled, kind));
            if (kind == MatchKind.Exact)
                return;
        }

        if (view is ViewGroup group)
        {
            for (var i = 0; i < group.ChildCount; i++)
            {
                if (group.GetChildAt(i) is { } child)
                    Walk(child, query, hits);
            }
        }
    }

    static MatchKind Match(AView view, ElementQuery query)
    {
        var kind = MatchKind.Exact;
        if (query.AutomationId != null && !string.Equals(view.ContentDescription, query.AutomationId, StringComparison.OrdinalIgnoreCase))
            return MatchKind.None;
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

    static IEnumerable<string?> Texts(AView view)
    {
        if (view is TextView text)
        {
            yield return text.Text;
            yield return text.Hint;
        }
        yield return view.ContentDescription;
    }

    static string? TextOf(AView view) => Texts(view).FirstOrDefault(t => !string.IsNullOrEmpty(t));

    static Rect? WindowRect(AView view)
    {
        if (!view.IsAttachedToWindow)
            return null;
        var location = new int[2];
        view.GetLocationInWindow(location);
        var d = Density;
        return new Rect(location[0] / d, location[1] / d, view.Width / d, view.Height / d);
    }

    static AView? ViewOf(VisualElement element) =>
        element.Handler is IPlatformViewHandler handler ? handler.ContainerView ?? handler.PlatformView : null;

    public static partial bool? Reaches(VisualElement element, Point windowPoint) => null;

    public static partial NativeScrollable? FindScrollable(VisualElement? container)
    {
        var root = container != null ? ViewOf(container) : Decor;
        if (root == null)
            return null;

        AView? best = null;
        double bestArea = 0;
        var queue = new Queue<AView>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var view = queue.Dequeue();
            if (view.Visibility != ViewStates.Visible)
                continue;
            if (CanScroll(view) && WindowRect(view) is { } rect && rect.Width * rect.Height > bestArea)
            {
                best = view;
                bestArea = rect.Width * rect.Height;
            }
            if (view is ViewGroup group)
            {
                for (var i = 0; i < group.ChildCount; i++)
                {
                    if (group.GetChildAt(i) is { } child)
                        queue.Enqueue(child);
                }
            }
        }
        return Describe(best);
    }

    public static partial NativeScrollable? ScrollableAround(VisualElement element)
    {
        for (var parent = ViewOf(element)?.Parent; parent != null; parent = parent.Parent)
        {
            if (parent is AView view && CanScroll(view))
                return Describe(view);
        }
        return null;
    }

    static bool CanScroll(AView view) =>
        view.CanScrollVertically(1) || view.CanScrollVertically(-1) || view.CanScrollHorizontally(1) || view.CanScrollHorizontally(-1);

    static NativeScrollable? Describe(AView? view) =>
        view != null && WindowRect(view) is { } rect ? new NativeScrollable(view, rect, view.GetType().Name) : null;

    public static partial ScrollOutcome ScrollBy(NativeScrollable scrollable, double dx, double dy)
    {
        var view = (AView)scrollable.View;
        var d = Density;
        var beforeX = Offset(view, horizontal: true);
        var beforeY = Offset(view, horizontal: false);
        view.ScrollBy((int)(dx * d), (int)(dy * d));
        var afterX = Offset(view, horizontal: true);
        var afterY = Offset(view, horizontal: false);
        return new ScrollOutcome(afterX != beforeX || afterY != beforeY, afterX / d, afterY / d);
    }

    static int Offset(AView view, bool horizontal) => view switch
    {
        AndroidX.RecyclerView.Widget.RecyclerView recycler =>
            horizontal ? recycler.ComputeHorizontalScrollOffset() : recycler.ComputeVerticalScrollOffset(),
        _ => horizontal ? view.ScrollX : view.ScrollY,
    };

    public static partial bool Activate(object nativeView) => nativeView is AView view && view.PerformClick();

    public static partial double Scale() => Density;
}

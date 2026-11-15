namespace Immons.Tools.Maui.Inspector.Features.RemoteControl;

/// <summary>Direction the content scrolls toward — "down" reveals what is below (the finger moves up).</summary>
internal enum ScrollDirection
{
    Down,
    Up,
    Right,
    Left,
}

/// <summary>A point in window coordinates (device-independent units). MAUI-free for the tests.</summary>
internal readonly record struct PathPoint(double X, double Y);

/// <summary>A rectangle in window coordinates. MAUI-free for the tests.</summary>
internal readonly record struct PathRect(double X, double Y, double Width, double Height)
{
    public double CenterX => X + Width / 2;
    public double CenterY => Y + Height / 2;
}

/// <summary>Geometry of synthesized gestures: swipe interpolation and scroll distances.</summary>
internal static class GesturePath
{
    /// <summary>Interval between two synthesized move events — one frame at 60 Hz.</summary>
    public const int StepMs = 16;

    public static bool TryParseDirection(string? text, out ScrollDirection direction)
    {
        switch (text?.Trim().ToLowerInvariant())
        {
            case "down":
                direction = ScrollDirection.Down;
                return true;
            case "up":
                direction = ScrollDirection.Up;
                return true;
            case "right":
                direction = ScrollDirection.Right;
                return true;
            case "left":
                direction = ScrollDirection.Left;
                return true;
            default:
                direction = ScrollDirection.Down;
                return false;
        }
    }

    /// <summary>
    /// Points of the move phase from <paramref name="from"/> to <paramref name="to"/>, one per
    /// <see cref="StepMs"/> over the duration — the start is excluded (that is the down event),
    /// the end is always the last point. At least two steps so recognizers see a real drag.
    /// </summary>
    public static List<PathPoint> Interpolate(PathPoint from, PathPoint to, int durationMs)
    {
        var steps = Math.Max(2, durationMs / StepMs);
        var points = new List<PathPoint>(steps);
        for (var i = 1; i <= steps; i++)
        {
            var t = (double)i / steps;
            points.Add(new PathPoint(from.X + (to.X - from.X) * t, from.Y + (to.Y - from.Y) * t));
        }
        return points;
    }

    /// <summary>
    /// Scroll distance along the direction's axis: an <paramref name="amount"/> up to 1 is a
    /// fraction of the viewport, anything larger is in device-independent units.
    /// </summary>
    public static double Distance(ScrollDirection direction, double amount, PathRect viewport)
    {
        var extent = direction is ScrollDirection.Down or ScrollDirection.Up ? viewport.Height : viewport.Width;
        if (amount <= 0)
            amount = 0.8;
        return amount <= 1 ? extent * amount : amount;
    }

    /// <summary>Content offset change (dx, dy) that scrolls in the direction by the distance.</summary>
    public static PathPoint ScrollDelta(ScrollDirection direction, double distance) => direction switch
    {
        ScrollDirection.Down => new PathPoint(0, distance),
        ScrollDirection.Up => new PathPoint(0, -distance),
        ScrollDirection.Right => new PathPoint(distance, 0),
        _ => new PathPoint(-distance, 0),
    };

    /// <summary>
    /// Finger path that scrolls the viewport in the direction: centered on the viewport, kept
    /// 10% inside its edges (the edges are where system gestures and pull-to-refresh live).
    /// </summary>
    public static (PathPoint From, PathPoint To) SwipeFor(ScrollDirection direction, double distance, PathRect viewport)
    {
        var vertical = direction is ScrollDirection.Down or ScrollDirection.Up;
        var extent = vertical ? viewport.Height : viewport.Width;
        var usable = extent * 0.8;
        var length = Math.Min(Math.Abs(distance), usable);
        var half = length / 2;

        // Content moves opposite to the finger: scrolling down drags the finger up.
        var sign = direction is ScrollDirection.Down or ScrollDirection.Right ? -1 : 1;
        if (vertical)
        {
            var cy = viewport.CenterY;
            return (new PathPoint(viewport.CenterX, cy - sign * half), new PathPoint(viewport.CenterX, cy + sign * half));
        }

        var cx = viewport.CenterX;
        return (new PathPoint(cx - sign * half, viewport.CenterY), new PathPoint(cx + sign * half, viewport.CenterY));
    }

    /// <summary>Offset change that brings <paramref name="target"/> into the middle of the viewport.</summary>
    public static PathPoint CenterDelta(PathRect target, PathRect viewport) =>
        new(Math.Abs(target.CenterX - viewport.CenterX) < 1 || (target.X >= viewport.X && target.X + target.Width <= viewport.X + viewport.Width)
                ? 0
                : target.CenterX - viewport.CenterX,
            Math.Abs(target.CenterY - viewport.CenterY) < 1 || (target.Y >= viewport.Y && target.Y + target.Height <= viewport.Y + viewport.Height)
                ? 0
                : target.CenterY - viewport.CenterY);
}

using System.Collections.Specialized;
using System.Text.Json.Nodes;
using Immons.Tools.Maui.Inspector.Features.RemoteControl;

var failures = 0;

// ── Selector parsing ──────────────────────────────────────────────────────────────
var q = ElementQuery.From(JsonNode.Parse("""{"automationId":"LoginButton","index":2,"exact":true}"""));
Check("json: automationId/index/exact", q is { AutomationId: "LoginButton", Index: 2, Exact: true, Text: null });
Check("json: point only is no selector", ElementQuery.From(JsonNode.Parse("""{"x":10,"y":20}""")) == null);
Check("json: empty strings ignored", ElementQuery.From(JsonNode.Parse("""{"text":"","automationId":""}""")) == null);
Check("json: id as number", ElementQuery.From(JsonNode.Parse("""{"id":42}""")) is { Id: 42 });
Check("json: index as string", ElementQuery.From(JsonNode.Parse("""{"text":"OK","index":"1"}""")) is { Index: 1 });
Check("json: numeric automationId", ElementQuery.From(JsonNode.Parse("""{"automationId":7}""")) is { AutomationId: "7" });
Check("json: null body", ElementQuery.From((JsonNode?)null) == null);
var qs = new NameValueCollection { ["text"] = "Zaloguj", ["exact"] = "1", ["index"] = "3" };
Check("query string", ElementQuery.From(qs) is { Text: "Zaloguj", Exact: true, Index: 3 });
Check("query string: nothing", ElementQuery.From(new NameValueCollection { ["all"] = "1" }) == null);
Check("native searchable: text", new ElementQuery(Text: "Czat").NativeSearchable);
Check("native searchable: not with type", !new ElementQuery(Text: "Czat", Type: "Label").NativeSearchable);
Check("describe", new ElementQuery(AutomationId: "a", Text: "b", Index: 1).Describe() == "automationId=\"a\" text=\"b\" index=1");

// ── Text matching ─────────────────────────────────────────────────────────────────
Check("normalize whitespace + case", TextMatch.Normalize("  Zaloguj\n  SIĘ teraz ") == "zaloguj się teraz");
Check("exact", TextMatch.Compare("Kontynuuj", "kontynuuj", false) == MatchKind.Exact);
Check("contains", TextMatch.Compare("emailem i hasłem", "emailem", false) == MatchKind.Contains);
Check("contains refused when exact only", TextMatch.Compare("emailem i hasłem", "emailem", true) == MatchKind.None);
Check("empty candidate", TextMatch.Compare(null, "x", false) == MatchKind.None);
Check("best of several", TextMatch.Best(["Email", null, "Adres email"], "email", false) == MatchKind.Exact);

var ranked = TextMatch.Rank(
    new[] { ("a", MatchKind.Contains, true), ("b", MatchKind.Exact, false), ("c", MatchKind.Exact, true), ("d", MatchKind.None, true), ("e", MatchKind.Contains, false) },
    t => t.Item2, t => t.Item3).Select(t => t.Item1).ToArray();
Check("rank: exact > contains, on-screen first, stable", string.Join("", ranked) == "cbae");

// ── Gesture geometry ──────────────────────────────────────────────────────────────
Check("parse direction", GesturePath.TryParseDirection(" Left ", out var left) && left == ScrollDirection.Left);
Check("parse direction: junk", !GesturePath.TryParseDirection("sideways", out _));

var path = GesturePath.Interpolate(new PathPoint(0, 0), new PathPoint(100, 0), 160);
Check("interpolate: one step per frame", path.Count == 10);
Check("interpolate: ends at target", path[^1] == new PathPoint(100, 0) && path[0].X > 0);
Check("interpolate: at least two steps", GesturePath.Interpolate(new PathPoint(0, 0), new PathPoint(0, 10), 1).Count == 2);

var viewport = new PathRect(0, 100, 400, 600);
Check("distance: fraction of viewport", Math.Abs(GesturePath.Distance(ScrollDirection.Down, 0.5, viewport) - 300) < 0.01);
Check("distance: points", Math.Abs(GesturePath.Distance(ScrollDirection.Right, 250, viewport) - 250) < 0.01);
Check("distance: default 0.8", Math.Abs(GesturePath.Distance(ScrollDirection.Up, 0, viewport) - 480) < 0.01);
Check("delta: down is +y", GesturePath.ScrollDelta(ScrollDirection.Down, 50) == new PathPoint(0, 50));
Check("delta: left is -x", GesturePath.ScrollDelta(ScrollDirection.Left, 50) == new PathPoint(-50, 0));

var (from, to) = GesturePath.SwipeFor(ScrollDirection.Down, 300, viewport);
Check("swipe for scroll down: finger moves up", from.Y > to.Y && from.X == 200 && Math.Abs(from.Y - to.Y - 300) < 0.01);
var (f2, t2) = GesturePath.SwipeFor(ScrollDirection.Down, 5000, viewport);
Check("swipe stays 10% inside the viewport", t2.Y >= 100 + 60 - 0.01 && f2.Y <= 700 - 60 + 0.01);
var (f3, t3) = GesturePath.SwipeFor(ScrollDirection.Right, 100, viewport);
Check("swipe for scroll right: finger moves left", f3.X > t3.X && f3.Y == 400);

Check("center delta: inside = 0", GesturePath.CenterDelta(new PathRect(10, 200, 50, 50), viewport) == new PathPoint(0, 0));
var below = GesturePath.CenterDelta(new PathRect(0, 900, 400, 100), viewport);
Check("center delta: below scrolls down to center", below.X == 0 && Math.Abs(below.Y - (950 - 400)) < 0.01);
var above = GesturePath.CenterDelta(new PathRect(0, 50, 400, 100), viewport);
Check("center delta: partly above scrolls up", above.Y < 0);

Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILED");
return failures == 0 ? 0 : 1;

void Check(string name, bool ok)
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}");
    if (!ok)
        failures++;
}

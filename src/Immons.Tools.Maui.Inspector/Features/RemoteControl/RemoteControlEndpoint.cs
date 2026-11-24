using System.Diagnostics;
using System.Net;
using System.Text.Json.Nodes;

namespace Immons.Tools.Maui.Inspector.Features.RemoteControl;

/// <summary>
/// Driving the app over HTTP — what a UI-test script or an agent needs on a simulator or a
/// physical device: tap (by point, AutomationId or text), long-press, swipe, scroll, scroll-to,
/// set text, back; find / wait / page to look before and after. Touches are injected through
/// the platform's own pipeline where it allows (<see cref="TouchInput"/>), with a MAUI-level
/// fallback for taps.
/// </summary>
internal sealed class RemoteControlEndpoint(
    IMainThreadDispatcher mainThread,
    IActiveInspectorProvider inspectors,
    IElementRegistry elements) : IHttpEndpoint
{
    readonly ElementLocator _locator = new(inspectors, elements);
    readonly GestureRunner _gestures = new(mainThread);
    bool? _touchAvailable;

    public async Task<bool> TryHandle(InspectorHttpContext context, string method, string path)
    {
        switch (method)
        {
            case HttpVerbs.Get when path == ApiRoutes.Remote.Find:
                await Find(context).ConfigureAwait(false);
                return true;
            case HttpVerbs.Get when path == ApiRoutes.Remote.Page:
                await Page(context).ConfigureAwait(false);
                return true;
            case HttpVerbs.Post when path == ApiRoutes.Remote.Wait:
                await Wait(context).ConfigureAwait(false);
                return true;
            case HttpVerbs.Post when path is ApiRoutes.Mirror.Tap or ApiRoutes.Remote.LongPress or ApiRoutes.Remote.Swipe
                or ApiRoutes.Remote.Scroll or ApiRoutes.Remote.ScrollTo or ApiRoutes.Remote.Text or ApiRoutes.Remote.Back:
                if (!MauiInspector.Options.EnableRemoteControl)
                {
                    await Fail(context, 403, "remote control is disabled (MauiInspectorOptions.EnableRemoteControl)").ConfigureAwait(false);
                    return true;
                }
                var body = await RequestBody.ReadJson(context).ConfigureAwait(false) as JsonObject ?? [];
                await (path switch
                {
                    ApiRoutes.Mirror.Tap => Tap(context, body),
                    ApiRoutes.Remote.LongPress => LongPress(context, body),
                    ApiRoutes.Remote.Swipe => Swipe(context, body),
                    ApiRoutes.Remote.Scroll => Scroll(context, body),
                    ApiRoutes.Remote.ScrollTo => ScrollTo(context, body),
                    ApiRoutes.Remote.Text => SetText(context, body),
                    _ => Back(context),
                }).ConfigureAwait(false);
                return true;
            default:
                return false;
        }
    }

    // ── Looking ─────────────────────────────────────────────────────────────────────────────

    async Task Find(InspectorHttpContext context)
    {
        var query = ElementQuery.From(context.Request.QueryString);
        if (query == null)
        {
            await Fail(context, 400, "give automationId, text, placeholder, type or id").ConfigureAwait(false);
            return;
        }

        var json = await mainThread.RunAsync(() =>
        {
            var hits = _locator.FindAll(query);
            var matches = new JsonArray();
            foreach (var hit in hits.Take(50))
                matches.Add(hit.ToJson(ElementLocator.Hittable(hit)));
            return new JsonObject { ["count"] = hits.Count, ["matches"] = matches }.ToJsonString();
        }).ConfigureAwait(false);
        await HttpResponse.WriteJson(context, json).ConfigureAwait(false);
    }

    async Task Page(InspectorHttpContext context)
    {
        var json = await mainThread.RunAsync(() =>
            inspectors.Current is { } inspector ? RemoteNavigation.Describe(inspector, _locator).ToJsonString() : null).ConfigureAwait(false);
        if (json == null)
            await Fail(context, 503, "no window yet").ConfigureAwait(false);
        else
            await HttpResponse.WriteJson(context, json).ConfigureAwait(false);
    }

    async Task Wait(InspectorHttpContext context)
    {
        var body = await RequestBody.ReadJson(context).ConfigureAwait(false) as JsonObject ?? [];
        var query = ElementQuery.From(body);
        if (query == null)
        {
            await Fail(context, 400, "give automationId, text, placeholder, type or id").ConfigureAwait(false);
            return;
        }

        var timeout = Math.Clamp(Int(body, "timeoutMs") ?? 10_000, 0, 120_000);
        var interval = Math.Clamp(Int(body, "intervalMs") ?? 200, 50, 5_000);
        var gone = Bool(body, "gone") ?? false;
        var watch = Stopwatch.StartNew();

        while (true)
        {
            var hit = await mainThread.RunAsync(() => _locator.FindAll(query).FirstOrDefault(h => h.OnScreen)).ConfigureAwait(false);
            var done = gone ? hit == null : hit != null;
            if (done || watch.ElapsedMilliseconds >= timeout)
            {
                var json = new JsonObject
                {
                    ["ok"] = done,
                    ["elapsedMs"] = watch.ElapsedMilliseconds,
                };
                if (!done)
                    json["timeout"] = true;
                if (hit != null)
                    json["element"] = await mainThread.RunAsync(() => hit.ToJson()).ConfigureAwait(false);
                await HttpResponse.WriteJson(context, json.ToJsonString(), done ? 200 : 408).ConfigureAwait(false);
                return;
            }
            await Task.Delay(interval).ConfigureAwait(false);
        }
    }

    // ── Touching ────────────────────────────────────────────────────────────────────────────

    async Task Tap(InspectorHttpContext context, JsonObject body)
    {
        var native = Bool(body, "native") ?? true;
        var query = ElementQuery.From(body);

        if (query == null)
        {
            if (Point(body, "x", "y") is not { } point)
            {
                await Fail(context, 400, "give x/y or a selector (automationId, text, placeholder, type, id)").ConfigureAwait(false);
                return;
            }
            var pointVia = await TapAt(point, native).ConfigureAwait(false);
            await Respond(context, pointVia != null, new JsonObject { ["via"] = pointVia, ["x"] = point.X, ["y"] = point.Y }).ConfigureAwait(false);
            return;
        }

        var hit = await Resolve(query, Bool(body, "scroll") ?? true).ConfigureAwait(false);
        if (await RequireOnScreen(context, query, hit).ConfigureAwait(false) is not { } target)
            return;

        var tapPoint = target.TapPoint;
        string? via = null;
        if (native && await TouchAvailable().ConfigureAwait(false))
            via = await _gestures.Tap(tapPoint).ConfigureAwait(false) ? "touch" : null;
        via ??= await mainThread.RunAsync(() =>
            (target.Element != null ? SemanticActivation.Tap(target.Element)
                : target.Native != null && NativeUi.Activate(target.Native.View)) ? "semantic" : null).ConfigureAwait(false);

        await Respond(context, via != null, await Described(target, new JsonObject
        {
            ["via"] = via,
            ["x"] = Math.Round(tapPoint.X, 1),
            ["y"] = Math.Round(tapPoint.Y, 1),
        }).ConfigureAwait(false)).ConfigureAwait(false);
    }

    /// <summary>A point tap: injected touch, else the inspector's hit-test + semantic tap.</summary>
    async Task<string?> TapAt(Point point, bool native)
    {
        if (native && await TouchAvailable().ConfigureAwait(false) && await _gestures.Tap(point).ConfigureAwait(false))
            return "touch";
        return await mainThread.RunAsync(() => inspectors.Current?.RemoteTapAt(point) == true ? "semantic" : null).ConfigureAwait(false);
    }

    async Task LongPress(InspectorHttpContext context, JsonObject body)
    {
        var duration = Math.Clamp(Int(body, "durationMs") ?? 800, 100, 10_000);
        var query = ElementQuery.From(body);
        ElementHit? target = null;
        Point point;

        if (query != null)
        {
            target = await RequireOnScreen(context, query, await Resolve(query, Bool(body, "scroll") ?? true).ConfigureAwait(false)).ConfigureAwait(false);
            if (target == null)
                return;
            point = target.TapPoint;
        }
        else if (Point(body, "x", "y") is { } given)
        {
            point = given;
        }
        else
        {
            await Fail(context, 400, "give x/y or a selector").ConfigureAwait(false);
            return;
        }

        string? via = null;
        if ((Bool(body, "native") ?? true) && await TouchAvailable().ConfigureAwait(false))
            via = await _gestures.LongPress(point, duration).ConfigureAwait(false) ? "touch" : null;
        if (via == null && target?.Element is { } element)
            via = await mainThread.RunAsync(() => SemanticActivation.LongPress(element) ? "semantic" : null).ConfigureAwait(false);

        if (via == null && !await TouchAvailable().ConfigureAwait(false))
        {
            await Fail(context, 501, "long press needs touch injection (or a TouchBehavior with LongPressCommand on the element)").ConfigureAwait(false);
            return;
        }

        var extra = new JsonObject { ["via"] = via, ["x"] = Math.Round(point.X, 1), ["y"] = Math.Round(point.Y, 1), ["durationMs"] = duration };
        await Respond(context, via != null, target != null ? await Described(target, extra).ConfigureAwait(false) : extra).ConfigureAwait(false);
    }

    async Task Swipe(InspectorHttpContext context, JsonObject body)
    {
        if (!await TouchAvailable().ConfigureAwait(false))
        {
            await Fail(context, 501, "swipe needs touch injection — use /api/scroll for scrolling").ConfigureAwait(false);
            return;
        }

        var duration = Math.Clamp(Int(body, "durationMs") ?? 300, 50, 10_000);
        Point from, to;
        if (Point(body, "x1", "y1") is { } start && Point(body, "x2", "y2") is { } end)
        {
            (from, to) = (start, end);
        }
        else
        {
            if (!GesturePath.TryParseDirection(Str(body, "direction"), out var direction))
            {
                await Fail(context, 400, "give x1/y1/x2/y2, or direction (up|down|left|right — the way the finger moves) with an optional selector").ConfigureAwait(false);
                return;
            }

            Rect region;
            if (ElementQuery.From(body) is { } query)
            {
                if (await RequireOnScreen(context, query, await Resolve(query, true).ConfigureAwait(false)).ConfigureAwait(false) is not { } target)
                    return;
                region = target.Visible;
            }
            else
            {
                // No target: swipe inside the main scrolling area (a list, not the header above it).
                region = await mainThread.RunAsync(() => NativeUi.FindScrollable(null)?.Viewport
                    ?? (inspectors.Current is { } i ? new Rect(0, 0, i.WindowSize.Width, i.WindowSize.Height) : Rect.Zero)).ConfigureAwait(false);
            }

            var viewport = new PathRect(region.X, region.Y, region.Width, region.Height);
            var distance = GesturePath.Distance(direction, Double(body, "amount") ?? 0.6, viewport);
            // A finger moving down is a "scroll up" of the content — reuse the scroll geometry inverted.
            var contentDirection = direction switch
            {
                ScrollDirection.Down => ScrollDirection.Up,
                ScrollDirection.Up => ScrollDirection.Down,
                ScrollDirection.Left => ScrollDirection.Right,
                _ => ScrollDirection.Left,
            };
            var (a, b) = GesturePath.SwipeFor(contentDirection, distance, viewport);
            (from, to) = (new Point(a.X, a.Y), new Point(b.X, b.Y));
        }

        var ok = await _gestures.Swipe(from, to, duration, Int(body, "holdMs") ?? 0).ConfigureAwait(false);
        await Respond(context, ok, new JsonObject
        {
            ["via"] = ok ? "touch" : null,
            ["from"] = new JsonObject { ["x"] = Math.Round(from.X, 1), ["y"] = Math.Round(from.Y, 1) },
            ["to"] = new JsonObject { ["x"] = Math.Round(to.X, 1), ["y"] = Math.Round(to.Y, 1) },
            ["durationMs"] = duration,
        }).ConfigureAwait(false);
    }

    // ── Scrolling ───────────────────────────────────────────────────────────────────────────

    async Task Scroll(InspectorHttpContext context, JsonObject body)
    {
        if (!GesturePath.TryParseDirection(Str(body, "direction") ?? "down", out var direction))
        {
            await Fail(context, 400, "direction must be up, down, left or right (the way the content scrolls)").ConfigureAwait(false);
            return;
        }

        ElementHit? container = null;
        if (ElementQuery.From(body) is { } query)
        {
            container = await mainThread.RunAsync(() => _locator.Find(query)).ConfigureAwait(false);
            if (container == null)
            {
                await Fail(context, 404, $"no element matches {query.Describe()}").ConfigureAwait(false);
                return;
            }
        }

        var amount = Double(body, "amount") ?? 0.8;
        if (Bool(body, "gesture") == true)
        {
            await ScrollByGesture(context, container, direction, amount).ConfigureAwait(false);
            return;
        }

        var result = await mainThread.RunTaskAsync(() => ScrollProgrammatically(container?.Element, direction, amount)).ConfigureAwait(false);
        if (result == null)
        {
            await Fail(context, 404, "nothing scrollable on screen").ConfigureAwait(false);
            return;
        }
        await HttpResponse.WriteJson(context, result.ToJsonString()).ConfigureAwait(false);
    }

    async Task ScrollByGesture(InspectorHttpContext context, ElementHit? container, ScrollDirection direction, double amount)
    {
        if (!await TouchAvailable().ConfigureAwait(false))
        {
            await Fail(context, 501, "gesture scrolling needs touch injection — drop \"gesture\"").ConfigureAwait(false);
            return;
        }

        var viewport = await mainThread.RunAsync(() =>
        {
            var scrollable = container?.Element is { } element
                ? NativeUi.FindScrollable(element) ?? NativeUi.ScrollableAround(element)
                : NativeUi.FindScrollable(null);
            return scrollable?.Viewport ?? container?.Visible
                ?? (inspectors.Current is { } i ? new Rect(0, 0, i.WindowSize.Width, i.WindowSize.Height) : Rect.Zero);
        }).ConfigureAwait(false);

        var rect = new PathRect(viewport.X, viewport.Y, viewport.Width, viewport.Height);
        var (from, to) = GesturePath.SwipeFor(direction, GesturePath.Distance(direction, amount, rect), rect);
        // The hold at the end stops the fling, so the distance is what was asked for.
        var ok = await _gestures.Swipe(new Point(from.X, from.Y), new Point(to.X, to.Y), 300, holdEndMs: 120).ConfigureAwait(false);
        await Respond(context, ok, new JsonObject { ["via"] = "touch" }).ConfigureAwait(false);
    }

    /// <summary>Offsets the scroll container directly — exact, and it tells whether the end was reached.</summary>
    async Task<JsonObject?> ScrollProgrammatically(VisualElement? container, ScrollDirection direction, double amount)
    {
        var scrollable = container != null
            ? NativeUi.FindScrollable(container) ?? NativeUi.ScrollableAround(container)
            : NativeUi.FindScrollable(null);

        if (scrollable != null)
        {
            var viewport = new PathRect(scrollable.Viewport.X, scrollable.Viewport.Y, scrollable.Viewport.Width, scrollable.Viewport.Height);
            var delta = GesturePath.ScrollDelta(direction, GesturePath.Distance(direction, amount, viewport));
            var outcome = NativeUi.ScrollBy(scrollable, delta.X, delta.Y);
            return new JsonObject
            {
                ["ok"] = true,
                ["via"] = "native",
                ["moved"] = outcome.Moved,
                ["offset"] = new JsonObject { ["x"] = Math.Round(outcome.OffsetX, 1), ["y"] = Math.Round(outcome.OffsetY, 1) },
                ["container"] = scrollable.Type,
            };
        }

        // No platform scroll view reachable: a MAUI ScrollView can still be offset.
        var scrollView = container as ScrollView ?? Ancestor<ScrollView>(container) ?? FirstVisible<ScrollView>();
        if (scrollView == null)
            return null;
        var size = new PathRect(0, 0, scrollView.Width, scrollView.Height);
        var move = GesturePath.ScrollDelta(direction, GesturePath.Distance(direction, amount, size));
        var (x, y) = (scrollView.ScrollX, scrollView.ScrollY);
        await scrollView.ScrollToAsync(Math.Max(0, x + move.X), Math.Max(0, y + move.Y), false);
        return new JsonObject
        {
            ["ok"] = true,
            ["via"] = "maui",
            ["moved"] = Math.Abs(scrollView.ScrollX - x) > 0.5 || Math.Abs(scrollView.ScrollY - y) > 0.5,
            ["offset"] = new JsonObject { ["x"] = scrollView.ScrollX, ["y"] = scrollView.ScrollY },
            ["container"] = nameof(ScrollView),
        };
    }

    async Task ScrollTo(InspectorHttpContext context, JsonObject body)
    {
        var query = ElementQuery.From(body);
        if (query == null)
        {
            await Fail(context, 400, "give the target's automationId, text, placeholder, type or id").ConfigureAwait(false);
            return;
        }
        GesturePath.TryParseDirection(Str(body, "direction") ?? "down", out var direction);
        var maxScrolls = Math.Clamp(Int(body, "maxScrolls") ?? 20, 1, 200);
        var containerQuery = ElementQuery.From(body["container"]);

        for (var scrolls = 0; scrolls <= maxScrolls; scrolls++)
        {
            var hit = await mainThread.RunAsync(() => _locator.Find(query)).ConfigureAwait(false);
            if (hit is { CenterVisible: true } || (hit is { Element: null, OnScreen: true }))
            {
                await Respond(context, true, await Described(hit, new JsonObject { ["scrolls"] = scrolls }).ConfigureAwait(false)).ConfigureAwait(false);
                return;
            }
            if (scrolls == maxScrolls)
                break;

            // Realized but outside the viewport: center it. Not realized yet: page the list.
            if (hit?.Element is { } element && await mainThread.RunTaskAsync(() => ScrollIntoView(element)).ConfigureAwait(false))
            {
                await Task.Delay(150).ConfigureAwait(false);
                continue;
            }

            var paged = await mainThread.RunTaskAsync(async () =>
            {
                var container = containerQuery != null ? _locator.Find(containerQuery)?.Element : null;
                return await ScrollProgrammatically(container, direction, 0.8);
            }).ConfigureAwait(false);
            if (paged?["moved"]?.GetValue<bool>() != true)
                break;
            await Task.Delay(150).ConfigureAwait(false);
        }

        await Fail(context, 404, $"{query.Describe()} not found on screen after scrolling {direction.ToString().ToLowerInvariant()}").ConfigureAwait(false);
    }

    /// <summary>Scrolls every container around the element so its center comes into view.</summary>
    async Task<bool> ScrollIntoView(VisualElement element)
    {
        if (inspectors.Current?.BoundsOf(element) is { } bounds && NativeUi.ScrollableAround(element) is { } scrollable)
        {
            var delta = GesturePath.CenterDelta(
                new PathRect(bounds.X, bounds.Y, bounds.Width, bounds.Height),
                new PathRect(scrollable.Viewport.X, scrollable.Viewport.Y, scrollable.Viewport.Width, scrollable.Viewport.Height));
            return (delta.X != 0 || delta.Y != 0) && NativeUi.ScrollBy(scrollable, delta.X, delta.Y).Moved;
        }

        for (var parent = element.Parent; parent != null; parent = parent.Parent)
        {
            switch (parent)
            {
                case ScrollView scrollView:
                    await scrollView.ScrollToAsync(element, ScrollToPosition.Center, false);
                    return true;
                case ItemsView itemsView when ItemOf(element, itemsView) is { } item:
                    itemsView.ScrollTo(item, position: ScrollToPosition.Center, animate: false);
                    return true;
            }
        }
        return false;
    }

    /// <summary>Finds the match; an element off-screen inside a scroll container is scrolled to first.</summary>
    async Task<ElementHit?> Resolve(ElementQuery query, bool scroll)
    {
        var hit = await mainThread.RunAsync(() => _locator.Find(query)).ConfigureAwait(false);
        if (hit?.Element is not { } element || !scroll || hit.CenterVisible)
            return hit;
        if (!await mainThread.RunTaskAsync(() => ScrollIntoView(element)).ConfigureAwait(false))
            return hit;
        await Task.Delay(150).ConfigureAwait(false);
        return await mainThread.RunAsync(() => _locator.Find(query)).ConfigureAwait(false) ?? hit;
    }

    async Task<ElementHit?> RequireOnScreen(InspectorHttpContext context, ElementQuery query, ElementHit? hit)
    {
        if (hit == null)
        {
            await Fail(context, 404, $"no element matches {query.Describe()}").ConfigureAwait(false);
            return null;
        }
        if (!hit.OnScreen)
        {
            var json = new JsonObject
            {
                ["ok"] = false,
                ["error"] = $"{query.Describe()} is not on screen",
                ["element"] = await mainThread.RunAsync(() => hit.ToJson()).ConfigureAwait(false),
            };
            await HttpResponse.WriteJson(context, json.ToJsonString(), 409).ConfigureAwait(false);
            return null;
        }
        return hit;
    }

    // ── Text and navigation ─────────────────────────────────────────────────────────────────

    async Task SetText(InspectorHttpContext context, JsonObject body)
    {
        if (body["value"] is not JsonValue valueNode || !valueNode.TryGetValue<string>(out var value))
        {
            await Fail(context, 400, "give \"value\" (and a selector, else the focused input is used)").ConfigureAwait(false);
            return;
        }

        var query = ElementQuery.From(body);
        ElementHit? hit = null;
        if (query != null)
        {
            hit = await Resolve(query, Bool(body, "scroll") ?? true).ConfigureAwait(false);
            if (hit == null)
            {
                await Fail(context, 404, $"no element matches {query.Describe()}").ConfigureAwait(false);
                return;
            }
        }

        var clear = Bool(body, "clear") ?? true;
        var submit = Bool(body, "submit") ?? false;
        var dismiss = Bool(body, "dismiss") ?? false;

        var result = await mainThread.RunAsync(() =>
        {
            var input = hit?.Element is { } element ? ElementLocator.InputNear(element) : hit == null ? _locator.FocusedInput() : null;
            if (input == null)
                return null;

            input.Focus();
            input.Text = clear ? value : (input.Text ?? "") + value;
            ((Microsoft.Maui.ITextInput)input).CursorPosition = input.Text?.Length ?? 0;
            if (submit)
                SemanticActivation.Submit(input);
            if (dismiss)
                input.Unfocus();

            return new JsonObject
            {
                ["ok"] = true,
                ["type"] = input.GetType().Name,
                ["automationId"] = string.IsNullOrEmpty(input.AutomationId) ? null : input.AutomationId,
                ["text"] = input is Entry { IsPassword: true } ? new string('•', input.Text?.Length ?? 0) : input.Text,
            };
        }).ConfigureAwait(false);

        if (result == null)
            await Fail(context, 422, query == null ? "no input has focus — give a selector" : $"no Entry/Editor/SearchBar at {query.Describe()}").ConfigureAwait(false);
        else
            await HttpResponse.WriteJson(context, result.ToJsonString()).ConfigureAwait(false);
    }

    async Task Back(InspectorHttpContext context)
    {
        var action = await mainThread.RunTaskAsync(() =>
            inspectors.Current is { } inspector ? RemoteNavigation.BackAsync(inspector) : Task.FromResult("none")).ConfigureAwait(false);
        await Respond(context, action != "none", new JsonObject { ["action"] = action }).ConfigureAwait(false);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────

    async Task<bool> TouchAvailable() =>
        _touchAvailable ??= await mainThread.RunAsync(() => TouchInput.Available).ConfigureAwait(false);

    Task<JsonObject> Described(ElementHit hit, JsonObject extra) => mainThread.RunAsync(() =>
    {
        extra["element"] = hit.ToJson();
        return extra;
    });

    static Task Respond(InspectorHttpContext context, bool ok, JsonObject extra)
    {
        var json = new JsonObject { ["ok"] = ok };
        foreach (var (key, value) in extra.ToList())
        {
            extra.Remove(key);
            json[key] = value;
        }
        return HttpResponse.WriteJson(context, json.ToJsonString());
    }

    static Task Fail(InspectorHttpContext context, int status, string error) =>
        HttpResponse.WriteJson(context, new JsonObject { ["ok"] = false, ["error"] = error }.ToJsonString(), status);

    static object? ItemOf(VisualElement element, ItemsView itemsView)
    {
        for (Element? current = element; current != null && !ReferenceEquals(current, itemsView); current = current.Parent)
        {
            if (ReferenceEquals(current.Parent, itemsView) && current is VisualElement cell)
                return cell.BindingContext;
        }
        return null;
    }

    static T? Ancestor<T>(Element? element) where T : Element
    {
        for (var current = element?.Parent; current != null; current = current.Parent)
        {
            if (current is T match)
                return match;
        }
        return null;
    }

    T? FirstVisible<T>() where T : VisualElement
    {
        if (inspectors.Current is not { } inspector)
            return null;
        foreach (var root in LiveTree.Roots(inspector))
        {
            if (Search(root) is { } found)
                return found;
        }
        return null;

        T? Search(VisualElement element)
        {
            if (!element.IsVisible)
                return null;
            if (element is T match)
                return match;
            foreach (var child in LiveTree.Children(element))
            {
                if (Search(child) is { } nested)
                    return nested;
            }
            return null;
        }
    }

    static Point? Point(JsonObject body, string x, string y) =>
        Double(body, x) is { } px && Double(body, y) is { } py ? new Point(px, py) : null;

    static double? Double(JsonObject body, string name) =>
        body[name] is JsonValue value && value.TryGetValue<double>(out var d) ? d : null;

    static int? Int(JsonObject body, string name) =>
        body[name] is JsonValue value
            ? value.TryGetValue<int>(out var i) ? i : value.TryGetValue<double>(out var d) ? (int)d : null
            : null;

    static bool? Bool(JsonObject body, string name) =>
        body[name] is JsonValue value && value.TryGetValue<bool>(out var b) ? b : null;

    static string? Str(JsonObject body, string name) =>
        body[name] is JsonValue value && value.TryGetValue<string>(out var s) ? s : null;
}

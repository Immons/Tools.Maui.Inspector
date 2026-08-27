using System.Net;
using System.Text.Json.Nodes;

namespace Immons.Tools.Maui.Inspector.Features.Properties.Web;

/// <summary>
/// Multi-device hot reload: GET /api/ping identifies this app to sibling panels;
/// POST /api/broadcast/property applies an edit to every element with the given XAML
/// source identity — the key that stays stable across devices, platforms and idioms.
/// </summary>
internal sealed class BroadcastEndpoint(
    IMainThreadDispatcher mainThread,
    IActiveInspectorProvider inspectors,
    IPropertyCollector properties,
    ISyncTracker sync) : IHttpEndpoint
{
    public async Task<bool> TryHandle(HttpListenerContext context, string method, string path)
    {
        if (method == HttpVerbs.Get && path == ApiRoutes.Broadcast.Ping)
        {
            var json = new JsonObject
            {
                ["app"] = AppInfo.Current.Name,
                ["device"] = TreeJsonBuilder.DeviceDescription(),
                ["instance"] = RemoteServer.InstanceId,
                ["version"] = Shared.PackageVersion.Current,
            };
            // The heap-dump hand-off aims dotnet-gcdump by platform, emulator/device and diagnostic port.
            HeapDumpTarget.Describe(json, sync.Connected);
            await HttpResponse.WriteJson(context, json.ToJsonString()).ConfigureAwait(false);
            return true;
        }

        if (method != HttpVerbs.Post || path is not (ApiRoutes.Broadcast.Property or ApiRoutes.Broadcast.Action))
            return false;

        var node = await RequestBody.ReadJson(context).ConfigureAwait(false);
        var target = new BroadcastTarget(
            (string?)node?["source"] ?? "",
            (string?)node?["elementName"] ?? "",
            (string?)node?["automationId"] ?? "",
            (string?)node?["type"] ?? "",
            (string?)node?["page"] ?? "");
        var section = (string?)node?["section"] ?? "";
        var name = (string?)node?["name"] ?? "";
        var value = (string?)node?["value"] ?? "";
        var clear = (bool?)node?["clear"] ?? false;

        var applied = await mainThread.RunAsync(() => path == ApiRoutes.Broadcast.Property
            ? Apply(target, section, name, value, clear)
            : RunAction(target, section, name)).ConfigureAwait(false);
        await HttpResponse.WriteJson(context, $"{{\"ok\":true,\"applied\":{applied}}}").ConfigureAwait(false);
        return true;
    }

    /// <summary>Runs an action row (add span, add shadow, grid add/remove…) on every match.</summary>
    int RunAction(BroadcastTarget target, string section, string label)
    {
        if (inspectors.Current is not { } inspector)
            return 0;

        var applied = 0;
        foreach (var element in FindTargets(inspector, target))
        {
            var rows = properties.Collect(element, inspector.BoundsOf(element))
                .FirstOrDefault(s => s.Title == section)?.Rows;
            var row = rows?.FirstOrDefault(r => r.Action != null && (r.Value == label || r.Name == label));
            if (row?.Action is not { } action)
                continue;
            try
            {
                action();
                applied++;
            }
            catch
            {
                // one bad target must not stop the fan-out
            }
        }

        if (applied > 0)
            inspector.RemoteAfterEdit();
        return applied;
    }

    /// <summary>Applies to every element whose XAML source matches; 0 when this device's
    /// active templates (other idiom/platform) don't contain the edited element.</summary>
    int Apply(BroadcastTarget target, string section, string name, string value, bool clear)
    {
        if (inspectors.Current is not { } inspector)
            return 0;

        var applied = 0;
        foreach (var element in FindTargets(inspector, target))
        {
            var rows = properties.Collect(element, inspector.BoundsOf(element))
                .FirstOrDefault(s => s.Title == section)?.Rows;
            var row = rows?.FirstOrDefault(r => r.Name == name && r.Editor != null);
            if (row?.Editor is not { } editor)
                continue;
            var ok = clear ? editor.Clear() : editor.Apply(value);
            if (ok)
                applied++;
        }

        if (applied > 0)
            inspector.RemoteAfterEdit();
        return applied;
    }

    /// <summary>How a remote edit addresses elements on this device.</summary>
    sealed record BroadcastTarget(string Source, string ElementName, string AutomationId, string TypeName, string Page);

    /// <summary>
    /// Primary key is the XAML source location — identical across devices for the same build,
    /// and shared by every instance of a DataTemplate. When the device renders a *different*
    /// template (AdaptiveTemplateView, OnIdiom…) that location does not exist here, so we fall
    /// back to identifiers of the same type on the counterpart page: AutomationId first, because
    /// it exists to identify one element, then StyleId (which MAUI also fills from x:Name).
    /// StyleId is a weaker key — it doubles as the MAUI CSS "#id" selector and nothing keeps it
    /// unique — so an ambiguous match is refused rather than guessed; see <see cref="OneSourceOnly"/>.
    /// </summary>
    static IEnumerable<VisualElement> FindTargets(IWindowInspector inspector, BroadcastTarget target)
    {
        var all = inspector.Roots.SelectMany(Walk).ToList();

        if (target.Source.Length > 0)
        {
            var bySource = all.Where(e => XamlSource.Describe(e) == target.Source).ToList();
            if (bySource.Count > 0)
                return bySource;
        }

        bool SameType(VisualElement e) => target.TypeName.Length == 0 || e.GetType().Name == target.TypeName;

        // Name matching is confined to the counterpart page, so "Title" on another screen is safe.
        bool SamePage(VisualElement e) => target.Page.Length == 0 || PageIdentity.Of(e) == target.Page;

        var candidates = all.Where(e => SameType(e) && SamePage(e)).ToList();

        if (target.AutomationId.Length > 0)
        {
            var byAutomationId = OneSourceOnly(candidates.Where(e => e.AutomationId == target.AutomationId).ToList());
            if (byAutomationId.Count > 0)
                return byAutomationId;
        }

        if (target.ElementName.Length > 0)
        {
            var byName = OneSourceOnly(candidates.Where(e => e.StyleId == target.ElementName).ToList());
            if (byName.Count > 0)
                return byName;
        }

        return [];
    }

    /// <summary>
    /// Several matches mean one thing only when they are instances of the same XAML line — the rows
    /// of a DataTemplate, which is exactly what fan-out should hit. Matches coming from different
    /// lines are a name collision (two controls sharing a StyleId), and picking either would be a
    /// guess, so nothing is returned and the panel reports "—".
    /// </summary>
    static List<VisualElement> OneSourceOnly(List<VisualElement> matches)
    {
        if (matches.Count <= 1)
            return matches;

        var sources = matches.Select(XamlSource.Describe).Distinct().ToList();
        return sources is [{ Length: > 0 }] ? matches : [];
    }

    static IEnumerable<VisualElement> Walk(VisualElement element)
    {
        yield return element;
        foreach (var child in VisualTreeWalker.GetVisualChildren(element))
        {
            foreach (var nested in Walk(child))
                yield return nested;
        }
    }
}

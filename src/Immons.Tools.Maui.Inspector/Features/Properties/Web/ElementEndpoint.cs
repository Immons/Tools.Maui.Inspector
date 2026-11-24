using System.Net;

namespace Immons.Tools.Maui.Inspector.Features.Properties.Web;

/// <summary>/api/element/{id} (GET) and /api/element/{id}/select|property|action (POST).</summary>
internal sealed class ElementEndpoint(
    IMainThreadDispatcher mainThread,
    IActiveInspectorProvider inspectors,
    IElementRegistry elements,
    IElementJsonBuilder elementJson,
    IPropertyCommands commands,
    IStructureCommands structure,
    AutomationIdBinder automationIds) : IHttpEndpoint
{
    public async Task<bool> TryHandle(InspectorHttpContext context, string method, string path)
    {
        var parts = path.Trim('/').Split('/');
        if (parts.Length < 3 || parts[0] != "api" || parts[1] != "element" || !int.TryParse(parts[2], out var id))
            return false;

        var verb = parts.Length > 3 ? parts[3] : null;

        if (method == HttpVerbs.Get && verb == null)
        {
            var json = await mainThread.RunAsync(() => elementJson.Build(id)).ConfigureAwait(false);
            if (json == null)
                await HttpResponse.WriteText(context, 404, "element not found").ConfigureAwait(false);
            else
                await HttpResponse.WriteJson(context, json).ConfigureAwait(false);
            return true;
        }

        if (method == HttpVerbs.Get && verb == "xaml")
        {
            var xaml = await mainThread.RunAsync(() =>
                elements.Find(id) is View view ? Structure.ElementCloner.Preview(view) : null).ConfigureAwait(false);
            if (xaml == null)
                await HttpResponse.WriteText(context, 404, "element not found").ConfigureAwait(false);
            else
                await HttpResponse.WriteText(context, 200, xaml).ConfigureAwait(false);
            return true;
        }

        if (method == HttpVerbs.Get && verb == "automationid-candidates")
        {
            var json = await mainThread.RunAsync(() => automationIds.Candidates(id)).ConfigureAwait(false);
            await HttpResponse.WriteJson(context, json).ConfigureAwait(false);
            return true;
        }

        if (method == HttpVerbs.Post && verb == "automationid-bind")
        {
            var node = await RequestBody.ReadJson(context).ConfigureAwait(false);
            var bindingPath = node?["path"]?.GetValue<string>() ?? "";
            var format = node?["format"]?.GetValue<string>() ?? "";
            var result = await mainThread.RunAsync(() => automationIds.Bind(id, bindingPath, format)).ConfigureAwait(false);
            await HttpResponse.WriteJson(context, new System.Text.Json.Nodes.JsonObject
            {
                ["ok"] = result.Ok,
                ["error"] = result.Error,
            }.ToJsonString()).ConfigureAwait(false);
            return true;
        }

        if (method == HttpVerbs.Get && verb == "style-candidates")
        {
            var json = await mainThread.RunAsync(() =>
            {
                if (elements.Find(id) is not View view)
                    return null;
                var array = new System.Text.Json.Nodes.JsonArray();
                foreach (var candidate in Structure.StyleExtractor.Candidates(view))
                {
                    array.Add(new System.Text.Json.Nodes.JsonObject
                    {
                        ["name"] = candidate.Name,
                        ["value"] = candidate.Value,
                        ["checked"] = candidate.Preselected,
                    });
                }
                return (string?)new System.Text.Json.Nodes.JsonObject
                {
                    ["type"] = view.GetType().Name,
                    ["candidates"] = array,
                }.ToJsonString();
            }).ConfigureAwait(false);
            if (json == null)
                await HttpResponse.WriteText(context, 404, "element not found").ConfigureAwait(false);
            else
                await HttpResponse.WriteJson(context, json).ConfigureAwait(false);
            return true;
        }

        if (method == HttpVerbs.Post && verb == "select")
        {
            var ok = await mainThread.RunAsync(() => Select(id)).ConfigureAwait(false);
            await HttpResponse.WriteOk(context, ok, ok ? 200 : 404).ConfigureAwait(false);
            return true;
        }

        if (method == HttpVerbs.Post && verb is "property" or "action")
        {
            var node = await RequestBody.ReadJson(context).ConfigureAwait(false);
            var section = node?["section"]?.GetValue<string>() ?? "";
            var name = node?["name"]?.GetValue<string>() ?? "";
            var value = node?["value"]?.GetValue<string>() ?? "";
            var clear = node?["clear"]?.GetValue<bool>() ?? false;

            var result = await mainThread.RunAsync(() =>
            {
                var changeLog = InspectorServices.Current.XamlChanges;
                var seqBefore = changeLog.LastSeq;
                var ok = verb == "property"
                    ? (clear ? commands.Clear(id, section, name) : commands.Apply(id, section, name, value))
                    : commands.RunAction(id, section, name);
                var seqAfter = changeLog.LastSeq;
                // The panel watches this seq for the updater's write ack (per-field spinner).
                return (Ok: ok, WriteSeq: seqAfter > seqBefore ? seqAfter : (long?)null);
            }).ConfigureAwait(false);

            await HttpResponse.WriteJson(context, new System.Text.Json.Nodes.JsonObject
            {
                ["ok"] = result.Ok,
                ["writeSeq"] = result.WriteSeq,
            }.ToJsonString()).ConfigureAwait(false);
            return true;
        }

        if (method == HttpVerbs.Post && verb == "structure")
        {
            var node = await RequestBody.ReadJson(context).ConfigureAwait(false);
            var op = node?["op"]?.GetValue<string>() ?? "";
            var type = node?["type"]?.GetValue<string>() ?? "";
            var delta = node?["delta"]?.GetValue<int>() ?? 0;

            var parentId = node?["parent"]?.GetValue<int>() ?? 0;
            var sourceId = node?["source"]?.GetValue<int>() ?? 0;
            var force = node?["force"]?.GetValue<bool>() ?? false;
            var siblingId = node?["sibling"]?.GetValue<int>() ?? 0;
            var beforeSibling = node?["before"]?.GetValue<bool>() ?? false;

            var (newId, error) = await mainThread.RunAsync(() => op switch
            {
                "add" => structure.Add(id, type),
                "remove" => (0, structure.Remove(id)),
                "move" => (0, structure.Move(id, delta)),
                "reparent" => (0, structure.Reparent(id, parentId, siblingId, beforeSibling)),
                "wrap" => structure.Wrap(id, type),
                "paste" => structure.Paste(id, sourceId, force),
                "unwrap" => (0, structure.UnwrapElement(id)),
                "extract-style" => structure.ExtractStyle(id,
                    node?["key"]?.GetValue<string>() ?? "",
                    (node?["props"] as System.Text.Json.Nodes.JsonArray)?
                        .Select(n => n?.GetValue<string>() ?? "").Where(n => n.Length > 0).ToList()
                        ?? new List<string>()),
                _ => (0, $"unknown structure op: {op}"),
            }).ConfigureAwait(false);

            var json = new System.Text.Json.Nodes.JsonObject
            {
                ["ok"] = error == null,
                ["id"] = newId,
                ["error"] = error,
            }.ToJsonString();
            await HttpResponse.WriteJson(context, json).ConfigureAwait(false);
            return true;
        }

        return false;
    }

    bool Select(int id)
    {
        if (inspectors.Current is not { } inspector || elements.Find(id) is not { } element)
            return false;
        inspector.RemoteSelect(element);
        return true;
    }
}

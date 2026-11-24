using System.Net;
using System.Text.Json.Nodes;

namespace Immons.Tools.Maui.Inspector.Features.NetworkInspection;

/// <summary>CRUD for mock rules: GET /api/mock/rules, POST …/save, …/delete, …/enable.</summary>
internal sealed class MockRulesEndpoint(IMockRules rules, IScenarioRecorder recorder) : IHttpEndpoint
{
    public async Task<bool> TryHandle(InspectorHttpContext context, string method, string path)
    {
        if (method == HttpVerbs.Get && path == ApiRoutes.MockRules.List)
        {
            await HttpResponse.WriteJson(context, ListJson()).ConfigureAwait(false);
            return true;
        }

        if (method != HttpVerbs.Post
            || !(path.StartsWith(ApiRoutes.MockRules.List, StringComparison.Ordinal)
                 || path.StartsWith("/api/mock/record", StringComparison.Ordinal)))
            return false;

        var node = await RequestBody.ReadJson(context).ConfigureAwait(false);
        switch (path)
        {
            case ApiRoutes.MockRules.Save:
                var saved = rules.Save(MockRuleSerializer.FromJson(node));
                await HttpResponse.WriteJson(context, $"{{\"ok\":true,\"id\":{saved.Id}}}").ConfigureAwait(false);
                return true;

            case ApiRoutes.MockRules.Import:
                var incoming = (node?["rules"] as JsonArray ?? []).Select(MockRuleSerializer.FromJson).ToList();
                var names = (node?["scenarios"] as JsonArray ?? [])
                    .Select(n => (string?)n ?? "").Where(n => n.Length > 0).ToList();
                rules.Import(names, (string?)node?["activeScenario"] ?? "", incoming);
                await HttpResponse.WriteJson(context, $"{{\"ok\":true,\"count\":{incoming.Count}}}").ConfigureAwait(false);
                return true;

            case ApiRoutes.MockRules.Mocking:
                rules.SetMockingEnabled((bool?)node?["enabled"] ?? true);
                await HttpResponse.WriteOk(context, true).ConfigureAwait(false);
                return true;

            case ApiRoutes.MockRules.Delete:
                var removed = rules.Remove((int?)node?["id"] ?? 0);
                await HttpResponse.WriteOk(context, removed).ConfigureAwait(false);
                return true;

            case ApiRoutes.MockRules.RecordStart:
                recorder.Start();
                await HttpResponse.WriteOk(context, true).ConfigureAwait(false);
                return true;

            case ApiRoutes.MockRules.RecordStop:
                var created = recorder.StopAndSave(((string?)node?["name"] ?? "").Trim() is { Length: > 0 } n ? n : "recorded");
                await HttpResponse.WriteJson(context, $"{{\"ok\":true,\"rules\":{created}}}").ConfigureAwait(false);
                return true;

            case ApiRoutes.MockRules.RecordCancel:
                recorder.Cancel();
                await HttpResponse.WriteOk(context, true).ConfigureAwait(false);
                return true;

            case ApiRoutes.MockRules.ScenarioAdd:
                rules.AddScenario((string?)node?["name"] ?? "");
                await HttpResponse.WriteOk(context, true).ConfigureAwait(false);
                return true;

            case ApiRoutes.MockRules.ScenarioRemove:
                rules.RemoveScenario((string?)node?["name"] ?? "");
                await HttpResponse.WriteOk(context, true).ConfigureAwait(false);
                return true;

            case ApiRoutes.MockRules.Scenario:
                rules.SetActiveScenario((string?)node?["name"] ?? "");
                await HttpResponse.WriteOk(context, true).ConfigureAwait(false);
                return true;

            case ApiRoutes.MockRules.Enable:
                var toggled = rules.SetEnabled((int?)node?["id"] ?? 0, (bool?)node?["on"] ?? false);
                await HttpResponse.WriteOk(context, toggled).ConfigureAwait(false);
                return true;

            default:
                return false;
        }
    }

    string ListJson() => new JsonObject
    {
        ["rules"] = MockRuleSerializer.ToJsonArray(rules.All),
        ["activeScenario"] = rules.ActiveScenario,
        ["mockingEnabled"] = rules.MockingEnabled,
        ["scenarios"] = new JsonArray(rules.ScenarioNames.Select(n => (JsonNode)n!).ToArray()),
        ["recording"] = recorder.Recording,
        ["recorded"] = recorder.Count,
    }.ToJsonString();


}

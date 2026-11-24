using System.Net;
using System.Text.Json.Nodes;
using Immons.Tools.Maui.Inspector.Web.Http;

namespace Immons.Tools.Maui.Inspector.Features.Structure;

/// <summary>
/// GET /api/structure/catalog — controls offered by the "Add element" popup and the toolbox.
/// POST /api/structure/add-at — toolbox drop on the mirror (window-dp coordinates).
/// </summary>
internal sealed class StructureEndpoint(
    IMainThreadDispatcher mainThread,
    IElementCatalog catalog,
    IStructureCommands structure,
    IActiveInspectorProvider inspectors,
    IElementRegistry elements) : IHttpEndpoint
{
    string? _cachedJson;

    public async Task<bool> TryHandle(InspectorHttpContext context, string method, string path)
    {
        if (method == HttpVerbs.Post && path == ApiRoutes.Structure.AddAt)
        {
            var node = await RequestBody.ReadJson(context).ConfigureAwait(false);
            var x = node?["x"]?.GetValue<double>() ?? 0;
            var y = node?["y"]?.GetValue<double>() ?? 0;
            var type = node?["type"]?.GetValue<string>() ?? "";

            var (id, error) = await mainThread.RunAsync(() => structure.AddAt(new Point(x, y), type)).ConfigureAwait(false);
            var json = new JsonObject { ["ok"] = error == null, ["id"] = id, ["error"] = error }.ToJsonString();
            await HttpResponse.WriteJson(context, json).ConfigureAwait(false);
            return true;
        }

        if (method == HttpVerbs.Post && path == ApiRoutes.Structure.Hit)
        {
            var node = await RequestBody.ReadJson(context).ConfigureAwait(false);
            var x = node?["x"]?.GetValue<double>() ?? 0;
            var y = node?["y"]?.GetValue<double>() ?? 0;

            var id = await mainThread.RunAsync(() =>
                inspectors.Current is { } inspector
                    && VisualTree.HitTester.HitTest(inspector.Roots, new Point(x, y), inspector.BoundsOf) is { } hit
                    ? elements.GetId(hit)
                    : 0).ConfigureAwait(false);
            await HttpResponse.WriteJson(context,
                new JsonObject { ["ok"] = id != 0, ["id"] = id }.ToJsonString()).ConfigureAwait(false);
            return true;
        }

        if (method == HttpVerbs.Post && path == ApiRoutes.Structure.GridInfo)
        {
            var node = await RequestBody.ReadJson(context).ConfigureAwait(false);
            var id = node?["id"]?.GetValue<int>() ?? 0;
            var json = await mainThread.RunAsync(() => BuildGridInfo(id)).ConfigureAwait(false);
            await HttpResponse.WriteJson(context, json).ConfigureAwait(false);
            return true;
        }

        if (method == HttpVerbs.Post && path == ApiRoutes.Structure.DropTarget)
        {
            var node = await RequestBody.ReadJson(context).ConfigureAwait(false);
            var x = node?["x"]?.GetValue<double>() ?? 0;
            var y = node?["y"]?.GetValue<double>() ?? 0;

            var target = await mainThread.RunAsync(() => structure.DropTargetAt(new Point(x, y))).ConfigureAwait(false);
            string json;
            if (target is { } t)
            {
                var children = new JsonArray();
                foreach (var c in t.Children)
                    children.Add(new JsonObject { ["x"] = c.X, ["y"] = c.Y, ["w"] = c.Width, ["h"] = c.Height });
                json = new JsonObject
                {
                    ["ok"] = true,
                    ["x"] = t.Bounds.X, ["y"] = t.Bounds.Y,
                    ["w"] = t.Bounds.Width, ["h"] = t.Bounds.Height,
                    ["label"] = t.Label,
                    ["children"] = children,
                }.ToJsonString();
            }
            else
            {
                json = new JsonObject { ["ok"] = false }.ToJsonString();
            }
            await HttpResponse.WriteJson(context, json).ConfigureAwait(false);
            return true;
        }

        if (method != HttpVerbs.Get || path != ApiRoutes.Structure.Catalog)
            return false;

        _cachedJson ??= BuildJson();
        await HttpResponse.WriteJson(context, _cachedJson).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Geometry of a selected Grid for the visual grid designer: definition values plus the
    /// row/column boundary lines in window dp. Actual track sizes are not public API, so
    /// boundaries are derived from the children occupying each track and interpolated for
    /// empty ones — good enough to drag.
    /// </summary>
    string BuildGridInfo(int id)
    {
        if (elements.Find(id) is not Grid grid
            || inspectors.Current is not { } inspector
            || inspector.BoundsOf(grid) is not { } bounds)
            return new JsonObject { ["ok"] = false }.ToJsonString();

        JsonArray Definitions(bool rows)
        {
            var array = new JsonArray();
            var count = rows ? grid.RowDefinitions.Count : grid.ColumnDefinitions.Count;
            for (var i = 0; i < count; i++)
            {
                var length = rows ? grid.RowDefinitions[i].Height : grid.ColumnDefinitions[i].Width;
                array.Add(JsonValue.Create(Features.Properties.GridLengthText.Format(length)));
            }
            return array;
        }

        JsonArray Boundaries(bool rows)
        {
            var count = Math.Max(1, rows ? grid.RowDefinitions.Count : grid.ColumnDefinitions.Count);
            var start = rows ? bounds.Y : bounds.X;
            var end = rows ? bounds.Y + bounds.Height : bounds.X + bounds.Width;
            var edges = new double?[count + 1];
            edges[0] = start;
            edges[count] = end;

            foreach (var child in grid.Children)
            {
                if (child is not VisualElement ve || inspector.BoundsOf(ve) is not { } r)
                    continue;
                var index = rows ? Grid.GetRow(ve) : Grid.GetColumn(ve);
                if (index <= 0 || index >= count)
                    continue;
                var edge = rows ? r.Y : r.X;
                edges[index] = edges[index] is { } known ? Math.Min(known, edge) : edge;
            }

            // Interpolate the tracks no child starts in.
            var lastKnown = 0;
            for (var i = 1; i <= count; i++)
            {
                if (edges[i] == null)
                    continue;
                var gap = i - lastKnown;
                for (var j = lastKnown + 1; j < i; j++)
                    edges[j] = edges[lastKnown] + (edges[i] - edges[lastKnown]) * (j - lastKnown) / gap;
                lastKnown = i;
            }

            var array = new JsonArray();
            foreach (var edge in edges)
                array.Add(JsonValue.Create(edge ?? start));
            return array;
        }

        return new JsonObject
        {
            ["ok"] = true,
            ["x"] = bounds.X, ["y"] = bounds.Y, ["w"] = bounds.Width, ["h"] = bounds.Height,
            ["rows"] = Definitions(rows: true),
            ["cols"] = Definitions(rows: false),
            ["rowEdges"] = Boundaries(rows: true),
            ["colEdges"] = Boundaries(rows: false),
        }.ToJsonString();
    }

    string BuildJson()
    {
        var entries = new JsonArray();
        foreach (var entry in catalog.All())
        {
            entries.Add(new JsonObject
            {
                ["name"] = entry.Name,
                ["type"] = entry.TypeName,
                ["description"] = entry.Description,
                ["container"] = entry.IsContainer,
                ["custom"] = entry.IsCustom,
            });
        }

        return new JsonObject { ["controls"] = entries }.ToJsonString();
    }
}

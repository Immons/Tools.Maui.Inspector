using System.Text.Json.Nodes;
using Immons.Tools.Maui.Inspector.Features.Cookbook.Ui;

namespace Immons.Tools.Maui.Inspector.Features.Cookbook.Web;

/// <summary>GET /api/cookbook payload: sections and items, what the device shows right now, the theme.</summary>
internal sealed class CookbookJsonBuilder(ICookbookHost host, IElementRegistry elements)
{
    public string Build()
    {
        var page = host.Page;
        var sections = host.Catalog;
        var root = new JsonObject
        {
            ["open"] = page != null,
            ["theme"] = AppThemeSwitch.Current,
            ["effectiveTheme"] = AppThemeSwitch.Effective,
            ["scale"] = DisplayScale(),
            ["pageSize"] = CookbookPaging.PageSize,
            ["sections"] = Sections(sections, page),
        };
        if (page != null)
        {
            root["view"] = new JsonObject
            {
                ["section"] = page.CurrentSection,
                ["page"] = page.CurrentPage,
                ["pages"] = page.PageCount,
            };
        }
        if (host.Focused is { } focused)
        {
            root["focus"] = new JsonObject
            {
                ["item"] = focused.Item.Id,
                ["elementId"] = focused.Sample != null ? elements.GetId(focused.Sample) : null,
                ["error"] = focused.Error,
                ["onDevice"] = host.FocusedOnDevice,
            };
        }
        return root.ToJsonString();
    }

    JsonArray Sections(IReadOnlyList<CookbookSection> sections, CookbookPage? page)
    {
        var array = new JsonArray();
        foreach (var section in sections)
        {
            var items = new JsonArray();
            for (var index = 0; index < section.Items.Count; index++)
                items.Add(Item(section.Items[index], index, page?.FindRealized(section.Items[index].Id)));
            array.Add(new JsonObject
            {
                ["id"] = section.Id,
                ["title"] = section.Title,
                ["pages"] = CookbookPaging.PageCount(section.Items.Count),
                ["items"] = items,
            });
        }
        return array;
    }

    /// <summary>Realized = its tile is on the device screen right now: selectable, state-forceable.</summary>
    JsonObject Item(CookbookItem item, int index, CookbookTile? realized)
    {
        var json = new JsonObject
        {
            ["id"] = item.Id,
            ["section"] = item.Section,
            ["page"] = CookbookPaging.PageOf(index),
            ["name"] = item.Name,
            ["kind"] = item.Kind,
            ["targetType"] = item.TargetType,
            ["source"] = item.Source,
            ["detail"] = item.Detail,
            ["value"] = item.LiveValue?.Invoke() ?? item.Value,
            ["previewable"] = item.CreateSample != null,
            ["states"] = item.HasStates,
            ["realized"] = realized != null,
        };
        if (realized == null)
            return json;

        json["error"] = realized.Error;
        json["elementId"] = elements.GetId(realized.Normal ?? (VisualElement)realized.Host);
        if (item.HasStates)
        {
            var states = new JsonArray();
            foreach (var name in VisualStates.NamesOf(realized.Normal))
                states.Add(JsonValue.Create(name));
            json["visualStates"] = states;
        }
        return json;
    }

    static double DisplayScale()
    {
        try
        {
            return DeviceDisplay.Current.MainDisplayInfo.Density;
        }
        catch
        {
            return 1;
        }
    }
}

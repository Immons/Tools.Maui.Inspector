using System.Text.Json.Nodes;

namespace Immons.Tools.Maui.Inspector.Features.Memory.Web;

/// <summary>The runtime-switchable memory options as the panel shows them.</summary>
internal static class MemorySettingsJson
{
    public static JsonObject Build()
    {
        var options = MauiInspector.Options.Memory;
        return new JsonObject
        {
            ["watch"] = options.WatchNavigation,
            ["watchDelayMs"] = (int)options.WatchDelay.TotalMilliseconds,
            ["disconnectHandlersOnPop"] = options.DisconnectHandlersOnPop,
            ["clearBindingContextOnPop"] = options.ClearBindingContextOnPop,
            ["tracking"] = options.TrackInstances,
        };
    }
}

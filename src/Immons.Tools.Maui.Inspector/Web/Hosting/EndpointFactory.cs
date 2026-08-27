using Immons.Tools.Maui.Inspector.Web.Endpoints;

namespace Immons.Tools.Maui.Inspector.Web.Hosting;

/// <summary>Wires the endpoint chain with its dependencies (web-side composition).</summary>
internal static class EndpointFactory
{
    public static IReadOnlyList<IHttpEndpoint> CreateAll()
    {
        IActiveInspectorProvider inspectors = new ActiveInspectorProvider();
        IMainThreadDispatcher mainThread = new MainThreadDispatcher(inspectors);
        var elements = InspectorServices.Current.Elements;
        var history = InspectorServices.Current.History;
        var xamlChanges = InspectorServices.Current.XamlChanges;
        ISyncTracker sync = InspectorServices.Current.Sync;

        ITreeJsonBuilder treeJson = new TreeJsonBuilder(inspectors, elements);
        IElementJsonBuilder elementJson = new ElementJsonBuilder(inspectors, elements, InspectorServices.Current.Properties);
        ISelectionJsonBuilder selectionJson = new SelectionJsonBuilder(inspectors, elements, history, xamlChanges, sync);
        var structure = InspectorServices.Current.Structure;
        IPropertyCommands commands = new PropertyCommands(inspectors, elements, InspectorServices.Current.Properties, history, structure);

        return
        [
            new StaticAssetsEndpoint(),
            new TreeEndpoint(mainThread, inspectors, treeJson),
            new SelectionEndpoint(mainThread, selectionJson),
            new ToggleEndpoint(mainThread, inspectors, xamlChanges),
            new ElementEndpoint(mainThread, inspectors, elements, elementJson, commands, structure, new AutomationIdBinder(elements, inspectors, xamlChanges)),
            new StructureEndpoint(mainThread, InspectorServices.Current.Catalog, structure, inspectors, elements),
            new BroadcastEndpoint(mainThread, inspectors, InspectorServices.Current.Properties, sync),
            new HistoryEndpoint(mainThread, history, commands),
            new NetworkEndpoint(InspectorServices.Current.Network),
            new MockRulesEndpoint(InspectorServices.Current.NetworkRules, InspectorServices.Current.Recorder),
            new InterceptEndpoint(InspectorServices.Current.Breakpoints),
            new LogsEndpoint(InspectorServices.Current.Logs),
            new Features.Editing.Web.ResourcesEndpoint(mainThread, InspectorServices.Current.ResourceScopes, xamlChanges, InspectorServices.Current.Cookbook),
            new ChangesEndpoint(xamlChanges, sync),
            new MirrorEndpoint(mainThread, inspectors),
            new MeasureEndpoint(mainThread, inspectors, elements),
            new Features.Cookbook.Web.CookbookEndpoint(mainThread, InspectorServices.Current.Cookbook,
                new Features.Cookbook.Web.CookbookJsonBuilder(InspectorServices.Current.Cookbook, elements),
                new Features.Cookbook.Web.TilePreviewer(mainThread, InspectorServices.Current.Cookbook)),
            new Features.Cookbook.Web.ThemeEndpoint(mainThread, InspectorServices.Current.Cookbook),
            new Features.Memory.Web.MemoryEndpoint(InspectorServices.Current.Memory, InspectorServices.Current.Tracker,
                InspectorServices.Current.TrackedInstances, InspectorServices.Current.HeapDumps, InspectorServices.Current.Snapshots, sync,
                InspectorServices.Current.Ledger, InspectorServices.Current.Leaks, InspectorServices.Current.Lifetimes),
            new Features.Memory.Web.MemoryControlEndpoint(mainThread, InspectorServices.Current.Ledger, InspectorServices.Current.Snapshots,
                InspectorServices.Current.TrackedInstances, InspectorServices.Current.Tracker),
            new Features.Memory.Web.SnapshotEndpoint(InspectorServices.Current.Snapshots, InspectorServices.Current.HeapDumps),
            new Features.Memory.Web.HeapDumpEndpoint(InspectorServices.Current.HeapDumps, InspectorServices.Current.Snapshots, sync),
        ];
    }
}

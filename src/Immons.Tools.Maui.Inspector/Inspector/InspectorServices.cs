using Microsoft.Extensions.DependencyInjection;

namespace Immons.Tools.Maui.Inspector.Inspector;

/// <summary>
/// Aggregate of the inspector's services, built by the app's MAUI container (see
/// <see cref="InspectorServiceRegistration"/>) and published through <see cref="Current"/>
/// when the app is built. The standalone fallback covers apps that reference inspector
/// types without calling UseMauiInspector — same registrations, private container.
/// </summary>
internal sealed class InspectorServices(
    IElementRegistry elements,
    IAddedElements added,
    IElementCatalog catalog,
    IXamlChangeLog xamlChanges,
    StructureReplay replay,
    IEditHistory history,
    IStructureCommands structure,
    INetworkLog network,
    ILogSink logs,
    IMockRules networkRules,
    IBreakpointGate breakpoints,
    IScenarioRecorder recorder,
    INetworkInterceptor interceptor,
    ISyncTracker sync,
    IAppliedExpressions expressions,
    IPropertyCollector properties,
    IResourceScopes resourceScopes,
    ICookbookCatalog cookbookCatalog,
    ICookbookHost cookbook,
    IInstanceTracker tracker,
    ITrackedInstances trackedInstances,
    ISnapshotRunner snapshots,
    IMemoryTimeline memory,
    IHeapDumpRequests heapDumps,
    INavigationLedger ledger,
    INavigationWatcher navigation,
    ILeakNotifier leaks,
    IServiceLifetimes lifetimes)
{
    static InspectorServices? _current;

    public static InspectorServices Current => _current ??= CreateStandalone();

    /// <summary>First registration wins — the app container's graph, when there is one.</summary>
    internal static void Use(InspectorServices services) => _current ??= services;

    static InspectorServices CreateStandalone() =>
        new ServiceCollection().AddMauiInspectorServices().BuildServiceProvider()
            .GetRequiredService<InspectorServices>();

    public IElementRegistry Elements { get; } = elements;

    public IAddedElements Added { get; } = added;

    public IElementCatalog Catalog { get; } = catalog;

    public IXamlChangeLog XamlChanges { get; } = xamlChanges;

    public StructureReplay Replay { get; } = replay;

    public IEditHistory History { get; } = history;

    public IStructureCommands Structure { get; } = structure;

    public INetworkLog Network { get; } = network;

    public ILogSink Logs { get; } = logs;

    public IMockRules NetworkRules { get; } = networkRules;

    public IBreakpointGate Breakpoints { get; } = breakpoints;

    public IScenarioRecorder Recorder { get; } = recorder;

    public INetworkInterceptor Interceptor { get; } = interceptor;

    public ISyncTracker Sync { get; } = sync;

    public IAppliedExpressions Expressions { get; } = expressions;

    public IPropertyCollector Properties { get; } = properties;

    public IResourceScopes ResourceScopes { get; } = resourceScopes;

    public ICookbookCatalog CookbookCatalog { get; } = cookbookCatalog;

    public ICookbookHost Cookbook { get; } = cookbook;

    public IInstanceTracker Tracker { get; } = tracker;

    public ITrackedInstances TrackedInstances { get; } = trackedInstances;

    public ISnapshotRunner Snapshots { get; } = snapshots;

    public IMemoryTimeline Memory { get; } = memory;

    public IHeapDumpRequests HeapDumps { get; } = heapDumps;

    public INavigationLedger Ledger { get; } = ledger;

    public INavigationWatcher Navigation { get; } = navigation;

    public ILeakNotifier Leaks { get; } = leaks;

    public IServiceLifetimes Lifetimes { get; } = lifetimes;
}

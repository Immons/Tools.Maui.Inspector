using Immons.Tools.Maui.Inspector.Web.Dispatch;
using Microsoft.Extensions.DependencyInjection;

namespace Immons.Tools.Maui.Inspector.Inspector;

/// <summary>
/// Registers the inspector's object graph in the app's own (MAUI) service container.
/// Everything is constructor-injected; <see cref="InspectorServices"/> is just the aggregate
/// the rest of the library reaches through.
/// </summary>
internal static class InspectorServiceRegistration
{
    public static IServiceCollection AddMauiInspectorServices(this IServiceCollection services)
    {
        services.AddSingleton<IElementRegistry, ElementRegistry>();
        services.AddSingleton<IAddedElements, AddedElements>();
        services.AddSingleton<IElementCatalog, ElementCatalog>();
        services.AddSingleton<IXamlChangeLog, XamlChangeLog>();
        services.AddSingleton<StructureReplay>();
        services.AddSingleton<IEditHistory, EditHistory>();
        services.AddSingleton<IActiveInspectorProvider, ActiveInspectorProvider>();
        services.AddSingleton<IStructureCommands, StructureCommands>();
        services.AddSingleton<INetworkLog, NetworkLog>();
        services.AddSingleton<ILogSink, LogSink>();
        services.AddSingleton<IMockRules, MockRules>();
        services.AddSingleton<IBreakpointGate, BreakpointGate>();
        services.AddSingleton<IScenarioRecorder, ScenarioRecorder>();
        services.AddSingleton<INetworkInterceptor, NetworkInterceptor>();
        services.AddSingleton<ISyncTracker, SyncTracker>();
        services.AddSingleton<IAppliedExpressions, PersistentAppliedExpressions>();
        services.AddSingleton<IPropertyCollector, PropertyCollector>();
        services.AddSingleton<IResourceScopes, ResourceScopes>();
        services.AddSingleton<ICookbookCatalog, CookbookCatalog>();
        services.AddSingleton<ICookbookHost, CookbookHost>();
        services.AddSingleton<IMainThreadDispatcher, MainThreadDispatcher>();
        services.AddSingleton<ITrackedInstances, TrackedInstances>();
        services.AddSingleton<IInstanceTracker, InstanceTracker>();
        services.AddSingleton<ISnapshotRunner, SnapshotRunner>();
        services.AddSingleton<IMemoryTimeline, MemoryTimeline>();
        services.AddSingleton<IHeapDumpRequests, HeapDumpRequests>();
        services.AddSingleton<INavigationLedger, NavigationLedger>();
        services.AddSingleton<INavigationWatcher, NavigationWatcher>();
        services.AddSingleton<ILeakNotifier, LeakNotifier>();
        services.AddSingleton<IHolderScanner, HolderScanner>();
        // The descriptors are read lazily — by then the collection holds everything the app registered.
        services.AddSingleton<IServiceLifetimes>(_ => new ServiceLifetimes(services));
        services.AddSingleton<InspectorServices>();
        // Resolves the graph the moment MauiApp.Build() completes — before any app code runs.
        services.AddSingleton<IMauiInitializeService, InspectorServicesInitializer>();
        return services;
    }

    sealed class InspectorServicesInitializer : IMauiInitializeService
    {
        public void Initialize(IServiceProvider services) =>
            InspectorServices.Use(services.GetRequiredService<InspectorServices>());
    }
}

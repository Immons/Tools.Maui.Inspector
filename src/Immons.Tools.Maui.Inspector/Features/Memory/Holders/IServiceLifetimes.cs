namespace Immons.Tools.Maui.Inspector.Features.Memory.Holders;

/// <summary>What the app registered as singletons — a singleton holding a transient page or view model is the usual DI leak.</summary>
internal interface IServiceLifetimes
{
    bool IsSingleton(Type type);

    IReadOnlyCollection<string> Singletons { get; }
}

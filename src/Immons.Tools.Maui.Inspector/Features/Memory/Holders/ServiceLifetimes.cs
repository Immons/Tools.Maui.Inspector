using Microsoft.Extensions.DependencyInjection;

namespace Immons.Tools.Maui.Inspector.Features.Memory.Holders;

/// <summary>Reads the service descriptors once, lazily — the collection is complete by the time the container resolves anything.</summary>
internal sealed class ServiceLifetimes(IServiceCollection services) : IServiceLifetimes
{
    readonly Lazy<HashSet<string>> _singletons = new(() => services
        .Where(d => d.Lifetime == ServiceLifetime.Singleton)
        .SelectMany(d => new[] { d.ServiceType.FullName, d.ImplementationType?.FullName })
        .OfType<string>()
        .ToHashSet());

    public bool IsSingleton(Type type) => type.FullName is { } name && _singletons.Value.Contains(name);

    public IReadOnlyCollection<string> Singletons => _singletons.Value;
}

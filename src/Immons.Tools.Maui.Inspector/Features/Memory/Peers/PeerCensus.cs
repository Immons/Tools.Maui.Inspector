namespace Immons.Tools.Maui.Inspector.Features.Memory.Peers;

internal sealed record PeerTypeCount(string Type, string Name, bool App, int Count);

/// <summary>Every live platform peer wrapper grouped by managed type; Supported = false where the runtime cannot list them.</summary>
internal sealed record PeerCensus(bool Supported, IReadOnlyList<PeerTypeCount> Types, int? GlobalRefs, int? WeakGlobalRefs);

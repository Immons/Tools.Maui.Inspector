namespace Immons.Tools.Maui.Inspector.Features.Memory.Holders;

/// <summary>Finds, in-process, what still points at the detached objects: static events and fields, events of long-lived objects.</summary>
internal interface IHolderScanner
{
    /// <summary>Holder descriptions per tracked-instance id; ids without holders are absent.</summary>
    Dictionary<int, List<string>> Scan(IReadOnlyList<(int Id, object Target)> suspects);
}

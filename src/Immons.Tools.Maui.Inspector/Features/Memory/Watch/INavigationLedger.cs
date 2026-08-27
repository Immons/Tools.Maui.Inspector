namespace Immons.Tools.Maui.Inspector.Features.Memory.Watch;

/// <summary>Every screen that appeared, when it left, and whether it was collected afterwards.</summary>
internal interface INavigationLedger
{
    IReadOnlyList<NavigationEntry> Entries { get; }

    /// <summary>A screen appeared: a Page seen in the visual tree, or any object the app reported.</summary>
    void Pushed(object screen, string? name = null, bool reported = false);

    void Popped(object screen);

    /// <summary>After a snapshot's collections: pending screens become Collected or Alive.</summary>
    void Judge();

    /// <summary>Cycles = screens that left since the app started — the unit of "per repetition".</summary>
    (int Open, int Pending, int Alive, int Cycles) Counts { get; }
}

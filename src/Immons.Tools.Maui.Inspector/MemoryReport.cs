namespace Immons.Tools.Maui.Inspector;

/// <summary>Objects of one type still alive after a snapshot's collections although no window uses them.</summary>
/// <param name="Type">Full type name.</param>
/// <param name="Role">Element, BindingContext, Handler or PlatformView.</param>
/// <param name="Count">How many instances.</param>
/// <param name="Holders">What the in-process scan found holding them: static events, static fields, events of long-lived objects.</param>
public sealed record LeakedObject(string Type, string Role, int Count, IReadOnlyList<string> Holders);

/// <summary>The outcome of <see cref="MauiInspector.TakeMemorySnapshotAsync"/>.</summary>
/// <param name="Time">When the snapshot was judged.</param>
/// <param name="Tracked">Objects the tracker knew about.</param>
/// <param name="Alive">Survivors of the collections.</param>
/// <param name="Attached">Survivors a window still uses.</param>
/// <param name="Detached">Survivors nothing uses — the suspects, all types.</param>
/// <param name="Leaks">The suspects of the app's own types, grouped by type and role.</param>
public sealed record MemoryReport(DateTime Time, int Tracked, int Alive, int Attached, int Detached, IReadOnlyList<LeakedObject> Leaks);

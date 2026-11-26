namespace Immons.Tools.Maui.Inspector.Features.Memory.Tracking;

/// <summary>Feeds the registry from a window's visual tree events.</summary>
internal interface IInstanceTracker
{
    bool Enabled { get; }

    void Attach(Window window);

    /// <summary>
    /// Turns the whole memory layer on or off at runtime. Off means: nothing is recorded, the
    /// registry is emptied and the per-element hooks return immediately — the app carries no
    /// inspector work at all. On means: the windows on screen are read again, so what is already
    /// there is known.
    /// </summary>
    void SetEnabled(bool enabled);

    /// <summary>A visual root outside the window's page tree, reported by the app: its elements count as attached until <see cref="UntrackRoot"/>.</summary>
    void TrackRoot(Element root);

    void UntrackRoot(Element root);

    /// <summary>Scans the platform windows for hosted overlay roots (main thread); runs on a timer and before every snapshot.</summary>
    void RefreshRoots();
}

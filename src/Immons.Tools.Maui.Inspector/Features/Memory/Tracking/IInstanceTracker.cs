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
}

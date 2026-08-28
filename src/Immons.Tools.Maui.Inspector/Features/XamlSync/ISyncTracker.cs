namespace Immons.Tools.Maui.Inspector.Features.XamlSync;

/// <summary>Tracks whether the sync tool is actively polling for changes.</summary>
internal interface ISyncTracker
{
    void MarkPolled();

    bool Connected { get; }
}

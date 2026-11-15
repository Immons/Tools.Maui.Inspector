namespace Immons.Tools.Maui.Inspector;

/// <summary>How the inspector overlay gets activated at runtime.</summary>
public enum InspectorActivation
{
    /// <summary>No gesture; call <see cref="MauiInspector.Show()"/> manually.</summary>
    None,

    /// <summary>A long press anywhere in the window opens the inspector on the pressed element.</summary>
    LongPress,
}

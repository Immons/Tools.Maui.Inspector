namespace Immons.Tools.Maui.Inspector.Features.Cookbook.Ui;

/// <summary>The layout the headless captures render in (see <see cref="CaptureStageHost"/>); children sit at its top-left.</summary>
internal sealed class CaptureStage : Grid
{
    public CaptureStage()
    {
        IsClippedToBounds = false;
        InputTransparent = true;
    }
}

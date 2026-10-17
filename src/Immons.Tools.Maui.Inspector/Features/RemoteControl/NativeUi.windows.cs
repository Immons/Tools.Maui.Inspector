namespace Immons.Tools.Maui.Inspector.Features.RemoteControl;

// WinUI: lookups stay in the MAUI tree; scrolling goes through ScrollView.ScrollToAsync.
internal static partial class NativeUi
{
    public static partial List<NativeHit> Find(ElementQuery query) => [];

    public static partial bool? Reaches(VisualElement element, Point windowPoint) => null;

    public static partial NativeScrollable? FindScrollable(VisualElement? container) => null;

    public static partial NativeScrollable? ScrollableAround(VisualElement element) => null;

    public static partial ScrollOutcome ScrollBy(NativeScrollable scrollable, double dx, double dy) => default;

    public static partial bool Activate(object nativeView) => false;

    public static partial double Scale() => DeviceDisplay.Current.MainDisplayInfo.Density;
}

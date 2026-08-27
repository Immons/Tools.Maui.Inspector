using Foundation;
using UIKit;

namespace Immons.Tools.Maui.Inspector.Features.Memory.Metrics;

internal static partial class MemoryEvents
{
    static NSObject? _observer;

    static partial void StartPlatform() =>
        _observer = UIApplication.Notifications.ObserveDidReceiveMemoryWarning((_, _) => Record("warning", "iOS memory warning"));
}

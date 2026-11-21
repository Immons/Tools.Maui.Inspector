using Foundation;
using UIKit;

namespace Immons.Tools.Maui.Inspector.Web.Hosting;

internal static partial class ServerLifecycle
{
    static readonly List<NSObject> Observers = [];

    // WillEnterForeground fires only after a real trip to the background (not for Control Center
    // or a notification banner) — exactly when iOS may have reclaimed the listening socket.
    // Both families are observed: scene-based apps (required with the iOS 27 SDK) get the UIScene
    // notifications, older app-delegate apps the UIApplication ones. OnForeground ignores repeats.
    static partial void StartPlatform()
    {
        var center = NSNotificationCenter.DefaultCenter;
        Observers.Add(center.AddObserver(UIScene.DidEnterBackgroundNotification, _ => OnSceneBackground()));
        Observers.Add(center.AddObserver(UIScene.WillEnterForegroundNotification, _ => OnForeground()));
        Observers.Add(center.AddObserver(UIApplication.DidEnterBackgroundNotification, _ => OnBackground()));
        Observers.Add(center.AddObserver(UIApplication.WillEnterForegroundNotification, _ => OnForeground()));
    }

    /// <summary>On iPad one window can go to the background while another stays on screen — the
    /// app counts as in the background only when no scene is in the foreground any more.</summary>
    static void OnSceneBackground()
    {
        try
        {
            foreach (var scene in UIApplication.SharedApplication.ConnectedScenes)
            {
                if (scene is UIScene { ActivationState: UISceneActivationState.ForegroundActive or UISceneActivationState.ForegroundInactive })
                    return;
            }
        }
        catch
        {
            // fall through: treat as background
        }

        OnBackground();
    }
}

using Foundation;

namespace SampleApp;

/// <summary>
/// UIKit scene lifecycle — required for apps built with the iOS 27 SDK (Info.plist →
/// UIApplicationSceneManifest → UISceneDelegateClassName). MAUI's base class creates the window.
/// </summary>
[Register("SceneDelegate")]
public class SceneDelegate : MauiUISceneDelegate
{
}

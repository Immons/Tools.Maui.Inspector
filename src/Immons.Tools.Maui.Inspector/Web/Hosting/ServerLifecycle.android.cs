using Android.App;
using Android.OS;

namespace Immons.Tools.Maui.Inspector.Web.Hosting;

internal static partial class ServerLifecycle
{
    static StartedActivities? _callbacks;

    // The app is in the foreground while at least one of its activities is started.
    static partial void StartPlatform()
    {
        if (Android.App.Application.Context is not Android.App.Application application)
            return;
        _callbacks = new StartedActivities();
        application.RegisterActivityLifecycleCallbacks(_callbacks);
    }

    sealed class StartedActivities : Java.Lang.Object, Android.App.Application.IActivityLifecycleCallbacks
    {
        int _started;

        public void OnActivityStarted(Activity activity)
        {
            // 0 → 1 is the way back from the background; OnForeground ignores it when the app never
            // left (the activity that started the inspector may get its OnStart after registration).
            if (Interlocked.Increment(ref _started) == 1)
                OnForeground();
        }

        public void OnActivityStopped(Activity activity)
        {
            // Clamped: an activity started before registration stops without a matching start.
            if (Interlocked.Decrement(ref _started) > 0)
                return;
            Interlocked.Exchange(ref _started, 0);
            OnBackground();
        }

        public void OnActivityCreated(Activity activity, Bundle? savedInstanceState)
        {
        }

        public void OnActivityDestroyed(Activity activity)
        {
        }

        public void OnActivityPaused(Activity activity)
        {
        }

        public void OnActivityResumed(Activity activity)
        {
        }

        public void OnActivitySaveInstanceState(Activity activity, Bundle outState)
        {
        }
    }
}

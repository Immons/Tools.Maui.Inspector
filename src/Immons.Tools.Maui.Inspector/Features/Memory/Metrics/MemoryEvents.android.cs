using Android.Content;
using Android.Content.Res;

namespace Immons.Tools.Maui.Inspector.Features.Memory.Metrics;

internal static partial class MemoryEvents
{
    static TrimCallbacks? _callbacks;

    static partial void StartPlatform()
    {
        _callbacks = new TrimCallbacks();
        Android.App.Application.Context.RegisterComponentCallbacks(_callbacks);
    }

    sealed class TrimCallbacks : Java.Lang.Object, IComponentCallbacks2
    {
        public void OnTrimMemory(TrimMemory level) => Record("trim", $"Android onTrimMemory {level}");

        public void OnLowMemory() => Record("low", "Android onLowMemory");

        public void OnConfigurationChanged(Configuration newConfig)
        {
        }
    }
}

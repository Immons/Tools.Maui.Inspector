using System.Net;

namespace Immons.Tools.Maui.Inspector.Features.Mirroring;

/// <summary>GET /api/screenshot (PNG) and POST /api/select-at (mirror click hit-test).</summary>
internal sealed class MirrorEndpoint(
    IMainThreadDispatcher mainThread,
    IActiveInspectorProvider inspectors) : IHttpEndpoint
{
    public async Task<bool> TryHandle(HttpListenerContext context, string method, string path)
    {
        if (method == HttpVerbs.Get && path == ApiRoutes.Mirror.Screenshot)
        {
            // Single-flight: captures must never pile up behind a slow encode — a poll that
            // arrives while one is running gets the previous frame instead of queueing work.
            byte[] bytes;
            if (await _captureGate.WaitAsync(0).ConfigureAwait(false))
            {
                try
                {
                    bytes = await CaptureFrame().ConfigureAwait(false);
                    KeepFrame(bytes);
                }
                finally
                {
                    _captureGate.Release();
                }
            }
            else
            {
                bytes = _lastFrame ?? await CaptureFrame().ConfigureAwait(false);
            }
            await HttpResponse.WriteBytes(context, "image/jpeg", bytes).ConfigureAwait(false);
            return true;
        }

        if (method == HttpVerbs.Post && path == ApiRoutes.Mirror.Tap)
        {
            var node = await RequestBody.ReadJson(context).ConfigureAwait(false);
            var x = node?["x"]?.GetValue<double>() ?? 0;
            var y = node?["y"]?.GetValue<double>() ?? 0;
            var ok = await mainThread.RunAsync(() =>
                inspectors.Current?.RemoteTapAt(new Point(x, y)) ?? false).ConfigureAwait(false);
            await HttpResponse.WriteOk(context, ok).ConfigureAwait(false);
            return true;
        }

        if (method == HttpVerbs.Post && path == ApiRoutes.Mirror.Key)
        {
            var node = await RequestBody.ReadJson(context).ConfigureAwait(false);
            var text = node?["text"]?.GetValue<string>();
            var keyName = node?["key"]?.GetValue<string>();
            var ok = await mainThread.RunAsync(() =>
                inspectors.Current?.RemoteKey(text, keyName) ?? false).ConfigureAwait(false);
            await HttpResponse.WriteOk(context, ok).ConfigureAwait(false);
            return true;
        }

        if (method == HttpVerbs.Post && path == ApiRoutes.Mirror.SelectAt)
        {
            var node = await RequestBody.ReadJson(context).ConfigureAwait(false);
            var x = node?["x"]?.GetValue<double>() ?? 0;
            var y = node?["y"]?.GetValue<double>() ?? 0;
            var ok = await mainThread.RunAsync(() =>
                inspectors.Current?.RemoteSelectAt(new Point(x, y)) ?? false).ConfigureAwait(false);
            await HttpResponse.WriteOk(context, ok).ConfigureAwait(false);
            return true;
        }

        return false;
    }

    /// <summary>A screenshot is hundreds of kilobytes; the fallback copy goes away once nobody mirrors.</summary>
    static readonly TimeSpan FrameKeptFor = TimeSpan.FromSeconds(10);

    readonly SemaphoreSlim _captureGate = new(1, 1);
    volatile byte[]? _lastFrame;
    Timer? _frameExpiry;

    void KeepFrame(byte[] frame)
    {
        _lastFrame = frame;
        _frameExpiry ??= new Timer(_ => _lastFrame = null);
        _frameExpiry.Change(FrameKeptFor, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Only the platform capture itself runs on the UI thread; the encode — the expensive
    /// part (UIImage.AsPNG used to stall the app every mirror tick) — runs on the pool,
    /// and as JPEG, which encodes several times faster than PNG for a full screen.
    /// Inspector-composited capture first — Essentials' Screenshot misses the separate
    /// windows Android hosts modal pages in.
    /// </summary>
    async Task<byte[]> CaptureFrame()
    {
        var composited = await mainThread.RunAsync(() => inspectors.Current?.CapturePng()).ConfigureAwait(false);
        if (composited != null)
            return composited;

        var shot = await mainThread.RunTaskAsync(() => Screenshot.Default.CaptureAsync()).ConfigureAwait(false);
        return await Task.Run(async () =>
        {
            using var stream = await shot.OpenReadAsync(ScreenshotFormat.Jpeg, quality: 80).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer).ConfigureAwait(false);
            return buffer.ToArray();
        }).ConfigureAwait(false);
    }
}

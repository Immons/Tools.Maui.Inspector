using System.Diagnostics;
using System.Text.Json.Nodes;

namespace Immons.Tools.Maui.Inspector.Features.NetworkInspection;

/// <summary>
/// One call in the Network tab. Starts pending (method, URL, start time, live duration, bytes sent
/// so far) and is filled in as the call progresses; bodies copied in transit arrive whenever the
/// transport has moved them, possibly after the status.
/// </summary>
internal sealed class NetworkCall(long seq, string method, string url, long? requestLength)
{
    readonly object _gate = new();
    readonly long _started = Stopwatch.GetTimestamp();
    long _sent;
    long _received;

    public long Seq { get; } = seq;

    public string Time { get; } = DateTime.Now.ToString("HH:mm:ss");

    public string Method { get; } = method;

    public string Url { get; } = url;

    public long? RequestLength { get; } = requestLength;

    public bool Pending { get; private set; } = true;

    public int Status { get; private set; }

    public double Ms { get; private set; }

    public long? Bytes { get; private set; }

    public string? Error { get; private set; }

    public string? Tag { get; private set; }

    public string? RequestBody { get; private set; }

    public string? ResponseBody { get; private set; }

    public long Sent => Interlocked.Read(ref _sent);

    public long Received => Interlocked.Read(ref _received);

    public double ElapsedMs => Pending ? Stopwatch.GetElapsedTime(_started).TotalMilliseconds : Ms;

    public void ReportSent(long bytes) => Interlocked.Exchange(ref _sent, bytes);

    public void ReportReceived(long bytes) => Interlocked.Exchange(ref _received, bytes);

    public void SetRequestBody(string? body)
    {
        lock (_gate)
            RequestBody = body;
    }

    public void SetResponseBody(string? body)
    {
        lock (_gate)
            ResponseBody = body;
    }

    /// <summary>Status known (or the call failed). Null bodies keep what a transit copy already set.</summary>
    public void Complete(int status, double ms, long? bytes, string? error, string? tag, string? requestBody, string? responseBody)
    {
        lock (_gate)
        {
            Status = status;
            Ms = ms;
            Bytes = bytes;
            Error = error;
            Tag = tag;
            RequestBody = requestBody ?? RequestBody;
            ResponseBody = responseBody ?? ResponseBody;
            Pending = false;
        }
    }

    internal JsonObject ToJson()
    {
        lock (_gate)
        {
            var json = new JsonObject
            {
                ["seq"] = Seq,
                ["time"] = Time,
                ["method"] = Method,
                // A pending call shows the URL without its query — presigned storage URLs carry the
                // signature there, and the row only needs to say where the bytes go.
                ["url"] = Pending ? WithoutQuery(Url) : Url,
                ["status"] = Status,
                ["ms"] = Math.Round(ElapsedMs, 1),
                ["bytes"] = Bytes,
                ["error"] = Error,
                ["tag"] = Tag,
                ["hasBody"] = RequestBody != null || ResponseBody != null,
            };
            if (Pending)
                json["pending"] = true;
            if (RequestLength != null)
                json["requestLength"] = RequestLength;
            if (Sent > 0)
                json["sent"] = Sent;
            if (Received > 0)
                json["received"] = Received;
            return json;
        }
    }

    internal static string WithoutQuery(string url)
    {
        var query = url.IndexOf('?');
        return query < 0 ? url : url[..query] + "?…";
    }
}

/// <summary>Ring buffer of HTTP requests shown in the web inspector's Network tab.</summary>
internal sealed class NetworkLog : INetworkLog
{
    readonly RingLog<NetworkCall> _log = new(limit: 200);

    public NetworkCall Begin(string method, string url, long? requestLength)
    {
        NetworkCall? call = null;
        _log.Add(seq => call = new NetworkCall(seq, method, url, requestLength));
        return call!;
    }

    public void Record(string method, string url, int status, double ms, long? bytes,
        string? error = null, string? tag = null, string? requestBody = null, string? responseBody = null) =>
        Begin(method, url, null).Complete(status, ms, bytes, error, tag, requestBody, responseBody);

    public void Clear() => _log.Clear();

    public string ToJson()
    {
        var array = new JsonArray();
        foreach (var call in _log.NewestFirst())
            array.Add(call.ToJson());
        return new JsonObject { ["entries"] = array }.ToJsonString();
    }

    public string? BodiesJson(long seq)
    {
        if (_log.Find(e => e.Seq == seq) is not { } entry)
            return null;

        return new JsonObject
        {
            ["seq"] = entry.Seq,
            ["method"] = entry.Method,
            ["url"] = entry.Pending ? NetworkCall.WithoutQuery(entry.Url) : entry.Url,
            ["status"] = entry.Status,
            ["request"] = entry.RequestBody,
            ["response"] = entry.ResponseBody,
        }.ToJsonString();
    }
}

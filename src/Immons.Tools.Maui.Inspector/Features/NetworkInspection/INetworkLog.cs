namespace Immons.Tools.Maui.Inspector.Features.NetworkInspection;

/// <summary>Sink for HTTP requests captured by <see cref="MauiInspectorHttpHandler"/>.</summary>
internal interface INetworkLog
{
    /// <summary>Lists the call right away as pending — a 300 MB upload is visible while it runs,
    /// not only after it finished. Complete it with <see cref="NetworkCall.Complete"/>.</summary>
    NetworkCall Begin(string method, string url, long? requestLength);

    /// <summary>A finished call in one go.</summary>
    void Record(string method, string url, int status, double ms, long? bytes,
        string? error = null, string? tag = null, string? requestBody = null, string? responseBody = null);

    string ToJson();

    /// <summary>Drops all recorded entries (the panel's "clear list" button).</summary>
    void Clear();

    /// <summary>Captured bodies of one entry as {"request":…,"response":…}; null when unknown seq.</summary>
    string? BodiesJson(long seq);
}

using Microsoft.AspNetCore.SignalR.Client;

namespace Immons.Tools.Maui.Inspector.SignalR;

/// <summary>
/// Asks the app's own reconnect policy and reports its answer: attempt number, the delay before
/// it (or that it gave up) and why the previous attempt ended. The decision is never altered.
/// </summary>
internal sealed class InspectingRetryPolicy(IRetryPolicy inner, HubSession session) : IRetryPolicy
{
    public TimeSpan? NextRetryDelay(RetryContext retryContext)
    {
        var delay = inner.NextRetryDelay(retryContext);
        session.OnRetryScheduled(retryContext, delay);
        return delay;
    }
}

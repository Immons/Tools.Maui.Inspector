using System.Diagnostics;
using System.Net;

namespace Immons.Tools.Maui.Inspector.Features.NetworkInspection;

/// <summary>
/// Order per call: listed as pending → request breakpoint → rule (delay/fail/request body) →
/// network or short-circuit → rule response overrides → response breakpoint → completed with bodies.
/// Bodies are never buffered ahead of the transfer unless they are textual and known to fit under
/// <see cref="MauiInspectorOptions.MaxCapturedBodyBytes"/>; anything else streams through
/// <see cref="ObservedContent"/>, which counts the bytes for the live progress and copies textual
/// bodies of unknown length on the way.
/// </summary>
internal sealed class NetworkInterceptor(
    INetworkLog log,
    IMockRules rules,
    IBreakpointGate breakpoints,
    IScenarioRecorder recorder) : INetworkInterceptor
{
    public async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send,
        CancellationToken cancellationToken)
    {
        var method = request.Method.Method;
        var url = request.RequestUri?.ToString() ?? "";
        var max = HttpBodyCapture.MaxBytes;
        var watch = Stopwatch.StartNew();
        var call = log.Begin(method, url, LengthOf(request.Content));
        string? tag = null;

        var requestPlan = HttpBodyCapture.PlanFor(request.Content, max);
        var requestBody = requestPlan == CapturePlan.Buffer
            ? await HttpBodyCapture.ReadBufferedAsync(request.Content!, max).ConfigureAwait(false)
            : null;
        if (requestBody != null)
            call.SetRequestBody(requestBody); // visible on the pending row already

        try
        {
            // 1. Request breakpoint — the panel may edit the outgoing body or abort.
            if (breakpoints.ShouldCapture(InterceptPhase.Request, url))
            {
                if (requestPlan == CapturePlan.Tee && request.Content != null)
                {
                    (request.Content, requestBody) = await HttpBodyCapture.PeekAsync(request.Content, max, cancellationToken).ConfigureAwait(false);
                    requestPlan = CapturePlan.None;
                }

                var decision = await breakpoints.WaitAsync(new InterceptedCall
                {
                    Id = breakpoints.NextId(),
                    Phase = InterceptPhase.Request,
                    Method = method,
                    Url = url,
                    Body = requestBody ?? "",
                }, cancellationToken).ConfigureAwait(false);

                if (decision is { Abort: true })
                    throw new HttpRequestException("Aborted from the MauiInspector breakpoint panel");
                if (decision?.Body != null && decision.Body != requestBody)
                {
                    request.Content = HttpBodyCapture.Replace(request.Content, decision.Body);
                    requestBody = decision.Body;
                    requestPlan = CapturePlan.None;
                    tag = "✋ edited";
                }
            }

            // 2. First matching rule: delay / simulated failure / request-body swap.
            var rule = rules.Match(method, url);
            if (rule != null)
            {
                tag = rule.Tag;
                if (rule.DelayMs > 0)
                    await Task.Delay(rule.DelayMs, cancellationToken).ConfigureAwait(false);
                if (rule.FailMode == MockRule.FailTimeout)
                    throw new TaskCanceledException($"Mocked timeout ({rule.Tag})");
                if (rule.FailMode == MockRule.FailError)
                    throw new HttpRequestException($"Mocked network error ({rule.Tag})");
                if (rule.RequestBody != null)
                {
                    request.Content = HttpBodyCapture.Replace(request.Content, rule.RequestBody);
                    requestBody = rule.RequestBody;
                    requestPlan = CapturePlan.None;
                }
            }

            // 3. Network call — or a fully mocked response without touching the server.
            HttpResponseMessage response;
            if (rule is { ShortCircuit: true })
            {
                response = new HttpResponseMessage((HttpStatusCode)(rule.Status ?? 200))
                {
                    Content = HttpBodyCapture.Replace(null, rule.ResponseBody ?? ""),
                    RequestMessage = request,
                };
            }
            else
            {
                if (request.Content != null)
                {
                    // Upload progress for the pending row; a textual body of unknown length is
                    // copied while the handler reads it.
                    var tee = requestPlan == CapturePlan.Tee;
                    request.Content = new ObservedContent(request.Content,
                        new BodyObserver(tee ? max : 0, call.ReportSent, tee ? call.SetRequestBody : null));
                }

                response = await send(request, cancellationToken).ConfigureAwait(false);
            }

            // 4. Rule overrides on a real response.
            if (rule is { ShortCircuit: false })
            {
                if (rule.Status is { } status)
                    response.StatusCode = (HttpStatusCode)status;
                if (rule.ResponseBody != null)
                    response.Content = HttpBodyCapture.Replace(response.Content, rule.ResponseBody);
            }

            var responsePlan = HttpBodyCapture.PlanFor(response.Content, max);
            var responseBody = responsePlan == CapturePlan.Buffer
                ? await HttpBodyCapture.ReadBufferedAsync(response.Content!, max).ConfigureAwait(false)
                : null;

            // 5. Response breakpoint — the panel may edit body/status or abort.
            if (breakpoints.ShouldCapture(InterceptPhase.Response, url))
            {
                if (responsePlan == CapturePlan.Tee && response.Content != null)
                {
                    (response.Content, responseBody) = await HttpBodyCapture.PeekAsync(response.Content, max, cancellationToken).ConfigureAwait(false);
                    responsePlan = CapturePlan.None;
                }

                var decision = await breakpoints.WaitAsync(new InterceptedCall
                {
                    Id = breakpoints.NextId(),
                    Phase = InterceptPhase.Response,
                    Method = method,
                    Url = url,
                    Body = responseBody ?? "",
                    Status = (int)response.StatusCode,
                }, cancellationToken).ConfigureAwait(false);

                if (decision is { Abort: true })
                    throw new HttpRequestException("Aborted from the MauiInspector breakpoint panel");
                if (decision != null)
                {
                    if (decision.Status is { } status)
                        response.StatusCode = (HttpStatusCode)status;
                    if (decision.Body != null && decision.Body != responseBody)
                    {
                        response.Content = HttpBodyCapture.Replace(response.Content, decision.Body);
                        responseBody = decision.Body;
                        responsePlan = CapturePlan.Buffer;
                    }
                    tag = "✋ edited";
                }
            }

            var finalStatus = (int)response.StatusCode;

            // 6. A body that still has to stream (download, chunked JSON): count it, and copy a
            //    textual one while the app reads it — the scenario recorder gets it at the end.
            if (responsePlan != CapturePlan.Buffer && response.Content != null && responseBody == null)
            {
                var tee = responsePlan == CapturePlan.Tee;
                response.Content = new ObservedContent(response.Content, new BodyObserver(
                    tee ? max : 0,
                    call.ReportReceived,
                    tee ? body =>
                    {
                        call.SetResponseBody(body);
                        recorder.Capture(method, url, finalStatus, body);
                    } : null));
                if (!tee)
                    recorder.Capture(method, url, finalStatus, null);
            }
            else
            {
                recorder.Capture(method, url, finalStatus, responseBody);
            }

            watch.Stop();
            call.Complete(finalStatus, watch.Elapsed.TotalMilliseconds, LengthOf(response.Content),
                error: null, tag, requestBody, responseBody);
            return response;
        }
        catch (Exception ex)
        {
            watch.Stop();
            call.Complete(0, watch.Elapsed.TotalMilliseconds, null, ex.GetType().Name, tag, requestBody, null);
            throw;
        }
    }

    static long? LengthOf(HttpContent? content)
    {
        try
        {
            return content?.Headers.ContentLength;
        }
        catch
        {
            return null;
        }
    }
}

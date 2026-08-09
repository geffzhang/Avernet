using Microsoft.AspNetCore.Http;

namespace Ocb.Gateway.Sse;

/// <summary>
/// Static handler that turns an HTTP GET request into a long-lived
/// Server-Sent Events stream.  Admission is controlled by a shared
/// <see cref="SseAdmissionGate"/> that rejects excess concurrent
/// streams with 429 before the response starts.
/// </summary>
public static class GatewaySseEndpoint
{
    private const string SseContentType = "text/event-stream";

    /// <summary>
    /// Handle an SSE request.  The <paramref name="pump"/> is typically
    /// created per-request and tied to a specific session, so that the
    /// stream dispatch subsystem can write to it from a different thread.
    /// </summary>
    public static async Task HandleAsync(
        HttpContext context,
        SseAdmissionGate admission,
        SseBackpressurePump pump)
    {
        if (!admission.TryAcquire(out var lease))
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            return;
        }

        using (lease!) // synchronous dispose — locks should not hop threads
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.Headers.ContentType = SseContentType;
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers["X-Accel-Buffering"] = "no";

            // Eagerly flush headers so the client can observe 200
            // before the first event arrives.  SSE comments (":…") are
            // no-ops for compliant clients.
            await context.Response.WriteAsync(":ok\n", context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);

            try
            {
                await foreach (var evt in pump.ReadAllAsync(context.RequestAborted))
                {
                    await context.Response.WriteAsync($"event:{evt.Event}\n", context.RequestAborted);
                    await context.Response.WriteAsync($"correlation_id:{evt.CorrelationId}\n", context.RequestAborted);
                    await context.Response.WriteAsync($"data:{evt.Data}\n\n", context.RequestAborted);
                    await context.Response.Body.FlushAsync(context.RequestAborted);
                }
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                // Client cancelled — no further writes possible.
                // The admission lease is released when this block exits.
            }
        }
    }
}

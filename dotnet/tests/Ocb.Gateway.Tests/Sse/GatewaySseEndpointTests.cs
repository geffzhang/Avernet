using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Ocb.Gateway.Sse;

namespace Ocb.Gateway.Tests.Sse;

/// <summary>
/// SSE 背压、取消与 correlation 透传集成测试。
/// </summary>
public sealed class GatewaySseEndpointTests : IAsyncDisposable
{
    /// <summary>
    /// Shared pump reference so the test can write events that the
    /// SSE endpoint on the other side will read and stream back.
    /// </summary>
    private readonly SseBackpressurePump _sharedPump;

    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public GatewaySseEndpointTests()
    {
        _sharedPump = new SseBackpressurePump(capacity: 256);

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("IntegrationTest");

                builder.ConfigureTestServices(services =>
                {
                    // Replace admission gate with one that allows only 1 stream
                    services.AddSingleton(new SseAdmissionGate(maxActiveStreams: 1));
                    // Share a single pump so the test can write to it
                    services.AddSingleton(_sharedPump);
                });
            });

        _client = _factory.CreateClient();
    }

    /// <summary>
    /// RED → GREEN: 流容量耗尽时，在响应开始前返回 429。
    /// </summary>
    [Fact]
    public async Task AdmissionReturns429BeforeResponseStartsWhenStreamCapacityIsExhausted()
    {
        // Stream 1 — holds the only admission slot.
        // HttpCompletionOption.ResponseHeadersRead ensures we read headers
        // but keep the response body stream open, so the slot stays occupied.
        using var first = await _client.GetAsync(
            "/openapi/v1/chat/messages/stream",
            HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // Stream 2 — should be rejected with 429.
        var second = await _client.GetAsync("/openapi/v1/chat/messages/stream");

        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        // Verify the response is NOT an SSE stream (no Content-Type header or wrong type)
        Assert.False(
            second.Content.Headers.ContentType?.MediaType == "text/event-stream");
    }

    /// <summary>
    /// RED → GREEN: SSE 流包含 correlation_id，且取消后停止写入。
    /// </summary>
    [Fact]
    public async Task StreamIncludesCorrelationIdAndStopsOnCancellation()
    {
        // Write a few events to the shared pump BEFORE the request arrives.
        _ = await _sharedPump.TryWriteAsync(
            new SseEvent("message", "hello", "corr-42"),
            CancellationToken.None);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var response = await _client.GetAsync(
            "/openapi/v1/chat/messages/stream",
            HttpCompletionOption.ResponseHeadersRead,
            cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        // Read SSE text until cancellation.
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cts.Token));
        var transcript = new StringBuilder();

        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cts.Token);
                if (line is null) break;
                transcript.AppendLine(line);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected — client-side cancellation.
        }

        var text = transcript.ToString();

        Assert.Contains("event:message", text);
        Assert.Contains("correlation_id:corr-42", text);

        // After client disconnects, the pump should not still be receiving
        // writes (backpressure/cancellation should stop the stream).
        Assert.Equal(0, _sharedPump.Count);
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        _sharedPump.Dispose();
        _factory.Dispose();
    }
}

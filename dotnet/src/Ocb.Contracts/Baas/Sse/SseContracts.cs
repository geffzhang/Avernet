using System.Text.Json;

namespace Ocb.Contracts.Baas.Sse;

/// <summary>
/// SSE (Server-Sent Events) contracts for streaming responses.
/// </summary>
public interface ISseServiceContract
{
    /// <summary>
    /// Converts stream chunks into SSE events for text/event-stream output.
    /// </summary>
    IAsyncEnumerable<SseEvent> ConvertToSseAsync(CallerContext caller, IAsyncEnumerable<StreamChunk> chunks, string runId, CancellationToken ct = default);
}

public sealed record SseEvent(
    string Event,
    string? Data,
    string? Id);

public sealed record StreamChunk(
    string RunId,
    string Content,
    bool IsFinished,
    string? ErrorCode);

public sealed record ChatStreamRequest(
    string BotId,
    string Message,
    JsonElement? Config);

public sealed record SseRunInfo(
    string RunId,
    string BotId,
    DateTimeOffset StartedAt);

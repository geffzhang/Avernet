using System.Runtime.CompilerServices;
using System.Text.Json;
using Ocb.Contracts;
using Ocb.Contracts.Baas.Sse;

namespace Ocb.Baas.Core.Sse;

/// <summary>
/// Converts stream chunks into SSE (Server-Sent Events) format.
/// Emits: ready → data* → done → heartbeat (on idle).
/// </summary>
public sealed class SseEventConverter : ISseServiceContract
{
    public async IAsyncEnumerable<SseEvent> ConvertToSseAsync(
        CallerContext caller,
        IAsyncEnumerable<StreamChunk> chunks,
        string runId,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Emit ready event first
        yield return new SseEvent("ready", JsonSerializer.Serialize(new { run_id = runId }), null);

        await foreach (var chunk in chunks.WithCancellation(ct))
        {
            if (chunk.IsFinished)
            {
                yield return new SseEvent("done", JsonSerializer.Serialize(new { run_id = chunk.RunId }), chunk.RunId);
                yield break;
            }

            if (chunk.ErrorCode is not null)
            {
                yield return new SseEvent("error", JsonSerializer.Serialize(new { error = chunk.ErrorCode, run_id = chunk.RunId }), chunk.RunId);
                yield break;
            }

            yield return new SseEvent("data", chunk.Content, chunk.RunId);
        }
    }
}

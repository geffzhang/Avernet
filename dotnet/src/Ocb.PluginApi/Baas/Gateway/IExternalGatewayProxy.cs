namespace Ocb.PluginApi.Baas.Gateway;

/// <summary>
/// Proxy interface for external API gateway message routing.
/// </summary>
public interface IExternalGatewayProxy
{
    /// <summary>
    /// Sends a chat message through the external gateway.
    /// </summary>
    Task<GatewayResponse> SendChatAsync(GatewayChatRequest request, CancellationToken ct = default);

    /// <summary>
    /// Sends a streaming chat message through the external gateway.
    /// Returns an async enumerable of stream chunks.
    /// </summary>
    IAsyncEnumerable<GatewayStreamChunk> SendChatStreamAsync(GatewayChatRequest request, CancellationToken ct = default);
}

public sealed record GatewayChatRequest(
    string BotId,
    string Message,
    string? ConversationId,
    IReadOnlyDictionary<string, string>? Options);

public sealed record GatewayResponse(
    string MessageId,
    string Content,
    string ConversationId,
    DateTimeOffset RespondedAt);

public sealed record GatewayStreamChunk(
    string RunId,
    string Content,
    bool IsFinished,
    string? ErrorCode);

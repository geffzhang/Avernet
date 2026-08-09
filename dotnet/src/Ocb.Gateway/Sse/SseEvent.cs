namespace Ocb.Gateway.Sse;

/// <summary>
/// A single Server-Sent Event frame with event type, data payload,
/// and a correlation identifier for end-to-end tracing.
/// </summary>
public sealed record SseEvent(string Event, string Data, string CorrelationId);

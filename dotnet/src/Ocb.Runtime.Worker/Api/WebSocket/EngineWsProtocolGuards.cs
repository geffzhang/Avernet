namespace Ocb.Runtime.Worker.Api.WebSocket;

/// <summary>
/// Protocol-level guards for the engine WebSocket endpoint.
/// Enforces method whitelist, protocol version negotiation, and
/// error semantics matching the parity corpus.
/// </summary>
public static class EngineWsProtocolGuards
{
    /// <summary>
    /// Server protocol version advertised during connect challenge.
    /// Must match the parity corpus PROTOCOL_VERSION.
    /// </summary>
    public const int ProtocolVersion = 3;

    /// <summary>
    /// Whitelist of allowed method names per the engine WebSocket protocol.
    /// Unknown methods receive INVALID_REQUEST error.
    /// </summary>
    private static readonly HashSet<string> AllowedMethods = new(StringComparer.Ordinal)
    {
        "connect",
        "session.new",
        "sessions.list",
        "sessions.patch",
        "sessions.delete",
        "sessions.reset",
        "chat.send",
        "chat.history",
        "chat.abort",
        "interaction.resolve",
        "interaction.pending.list",
        "health.claude",
        "providers.available",
        "models.list",
    };

    /// <summary>
    /// Returns true if the method name is in the whitelist.
    /// </summary>
    public static bool IsAllowed(string method) => AllowedMethods.Contains(method);

    /// <summary>
    /// Validates protocol version during handshake.
    /// Returns an error frame if the client requires a higher protocol version,
    /// or null if the version is acceptable.
    /// </summary>
    public static WsResponseFrame? ValidateProtocolVersion(int? minProtocol)
    {
        if (minProtocol.HasValue && minProtocol.Value > ProtocolVersion)
        {
            return WsResponseFrame.ErrorResponse(
                id: null!,
                code: "PROTOCOL_VERSION_MISMATCH",
                message: $"Client requires min_protocol={minProtocol.Value}, server supports {ProtocolVersion}");
        }

        return null;
    }

    /// <summary>
    /// Builds a standard INVALID_REQUEST error frame for unknown methods.
    /// </summary>
    public static WsResponseFrame RejectUnknown(string id, string method) =>
        WsResponseFrame.ErrorResponse(id, "INVALID_REQUEST", $"Unknown method: {method}");
}

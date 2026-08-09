using Ocb.Contracts.Sessions;
using Ocb.Runtime.Worker.Application;
using Ocb.Runtime.Worker.Infra.Clients.Models;

namespace Ocb.Runtime.Worker.Infra.Clients.ClaudeCode;

/// <summary>
/// Anti-corruption typed client for the Claude Code relay (engine) API.
/// All responses are validated against the parity-corpus contract shapes;
/// unknown shapes throw <see cref="ContractMappingException"/> (fail-closed).
///
/// Full HTTP integration is deferred until the engine backend is available
/// for end-to-end testing.
/// </summary>
public sealed class ClaudeCodeRelayTypedClient : IEngineSessionPort
{
    public Task<IReadOnlyList<SessionSummaryDto>> ListSessionsAsync(
        SessionListQuery query, CancellationToken ct)
    {
        throw new ContractMappingException(
            "ENGINE_PAYLOAD_SHAPE_MISMATCH",
            "Sessions list payload shape not yet integrated.");
    }

    public Task<SessionDetailDto?> GetSessionAsync(
        string sessionId, CancellationToken ct)
    {
        throw new ContractMappingException(
            "ENGINE_PAYLOAD_SHAPE_MISMATCH",
            "Session detail payload shape not yet integrated.");
    }

    public Task<ResetSessionResultDto> ResetSessionAsync(
        string sessionId, CancellationToken ct)
    {
        throw new ContractMappingException(
            "ENGINE_PAYLOAD_SHAPE_MISMATCH",
            "Session reset payload shape not yet integrated.");
    }

    public Task<SessionDetailDto?> UpdateSessionAsync(
        string sessionId, SessionUpdateRequest update, CancellationToken ct)
    {
        throw new ContractMappingException(
            "ENGINE_PAYLOAD_SHAPE_MISMATCH",
            "Session update payload shape not yet integrated.");
    }

    public Task<IReadOnlyList<SessionMessageDto>> ListMessagesAsync(
        string sessionId, CancellationToken ct)
    {
        throw new ContractMappingException(
            "ENGINE_PAYLOAD_SHAPE_MISMATCH",
            "Session messages payload shape not yet integrated.");
    }

    public Task DeleteSessionAsync(string sessionId, CancellationToken ct)
    {
        throw new ContractMappingException(
            "ENGINE_PAYLOAD_SHAPE_MISMATCH",
            "Session delete payload shape not yet integrated.");
    }
}

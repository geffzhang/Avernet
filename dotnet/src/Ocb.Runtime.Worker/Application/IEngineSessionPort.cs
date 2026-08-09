using Ocb.Contracts.Sessions;

namespace Ocb.Runtime.Worker.Application;

/// <summary>
/// Anti-corruption port for the engine session API.
/// Implementations are typed clients that enforce fail-closed mapping:
/// unknown payload shapes throw <see cref="Infra.Clients.Models.ContractMappingException"/>.
/// </summary>
public interface IEngineSessionPort
{
    Task<IReadOnlyList<SessionSummaryDto>> ListSessionsAsync(
        SessionListQuery query, CancellationToken ct);

    Task<SessionDetailDto?> GetSessionAsync(
        string sessionId, CancellationToken ct);

    Task<ResetSessionResultDto> ResetSessionAsync(
        string sessionId, CancellationToken ct);

    Task<SessionDetailDto?> UpdateSessionAsync(
        string sessionId, SessionUpdateRequest update, CancellationToken ct);

    Task<IReadOnlyList<SessionMessageDto>> ListMessagesAsync(
        string sessionId, CancellationToken ct);

    Task DeleteSessionAsync(string sessionId, CancellationToken ct);
}

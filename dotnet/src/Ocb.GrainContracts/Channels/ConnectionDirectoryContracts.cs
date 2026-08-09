using Ocb.Contracts;

namespace Ocb.GrainContracts.Channels;

/// <summary>
/// Tenant-scoped connection directory grain. Each tenant has its own
/// directory grain keyed <c>directory/{tenantId}</c>, avoiding a
/// single-grain hotspot.
/// </summary>
[Alias("Ocb.GrainContracts.Channels.IConnectionDirectoryGrain")]
public interface IConnectionDirectoryGrain : IGrainWithStringKey
{
    /// <summary>
    /// Register a new connection or update an existing one.
    /// </summary>
    Task RegisterAsync(
        string gatewayInstanceId,
        string connectionId,
        CallerContext caller,
        string sessionId,
        DateTimeOffset leaseExpiryUtc);

    /// <summary>
    /// Extend the lease for an existing connection.
    /// </summary>
    Task RenewLeaseAsync(string connectionId, DateTimeOffset leaseExpiryUtc);

    /// <summary>
    /// Remove a connection from the directory.
    /// </summary>
    Task RemoveAsync(string connectionId);

    /// <summary>
    /// Resolve a connection route by its session identifier.
    /// Returns <c>null</c> if the session is not registered.
    /// </summary>
    Task<ConnectionRoute?> ResolveBySessionAsync(string sessionId);
}

/// <summary>
/// Describes the routing information needed to reach a Gateway
/// connection from another node in the cluster.
/// </summary>
[Alias("Ocb.GrainContracts.Channels.ConnectionRoute")]
[GenerateSerializer]
public sealed record ConnectionRoute(
    string GatewayInstanceId,
    string ConnectionId,
    string TenantId,
    string SubjectId,
    string SessionId,
    DateTimeOffset LeaseExpiryUtc
);

/// <summary>
/// A message that the cluster needs to deliver to a specific
/// Gateway connection.
/// </summary>
[Alias("Ocb.GrainContracts.Channels.OutboundGatewayMessage")]
[GenerateSerializer]
public sealed record OutboundGatewayMessage(
    string TenantId,
    string SessionId,
    string ConnectionId,
    byte[] Payload,
    string CorrelationId
);

namespace Ocb.Gateway.Routing;

/// <summary>
/// Configuration model for gateway domain routing.
/// Holds the ordered list of domain routes loaded from application
/// configuration or the parity corpus baseline.
/// </summary>
public sealed record GatewayRoutingConfig(IReadOnlyList<DomainRoute> Routes);

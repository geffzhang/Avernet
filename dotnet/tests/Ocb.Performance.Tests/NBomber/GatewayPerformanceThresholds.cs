namespace Ocb.Performance.Tests.NBomber;

/// <summary>
/// P95 latency and error-rate ceilings for the Gateway benchmark.
/// These are check-in gates — any commit that exceeds them must
/// either fix the regression or intentionally raise the ceiling
/// with an architectural decision record.
/// </summary>
public sealed record GatewayPerformanceThresholds(
    double HttpP95Ms,
    double WsConnectP95Ms,
    double SseFirstEventP95Ms,
    double ErrorRateUpperBound)
{
    /// <summary>
    /// Default thresholds suitable for local singlebox development.
    /// CI and production should use tighter or environment-specific values.
    /// </summary>
    public static readonly GatewayPerformanceThresholds Singlebox = new(
        HttpP95Ms: 120,
        WsConnectP95Ms: 200,
        SseFirstEventP95Ms: 250,
        ErrorRateUpperBound: 0.01);
}

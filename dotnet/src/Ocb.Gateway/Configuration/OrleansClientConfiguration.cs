namespace Ocb.Gateway.Configuration;

public static class OrleansClientConfiguration
{
    public static void Configure(
        IServiceCollection services,
        IConfiguration configuration,
        string clusterConnectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(clusterConnectionString);

        // Minimum: register the connection string for downstream consumers.
        // Full Orleans Client build-out is in Gateway Task 7c.
        services.AddSingleton(new OrleansClientConnectionInfo(clusterConnectionString));
    }
}

public sealed record OrleansClientConnectionInfo(string ConnectionString);

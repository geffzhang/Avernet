using Microsoft.Extensions.Options;

namespace Ocb.Gateway.Configuration;

public static class GatewayBootstrap
{
    private static readonly HashSet<string> KnownKeys = new(StringComparer.Ordinal)
    {
        "HttpPort",
        "MaxConnections",
        "MaxConnectionsPerIp",
        "MessagesPerSecondPerConnection",
        "AllowedOrigins",
        "OrleansClusterId",
        "OrleansServiceId"
    };

    public static GatewayOptions ValidateAndBind(IConfiguration configuration)
    {
        var section = configuration.GetSection("Gateway");

        var unknown = section.GetChildren()
            .Select(c => c.Key)
            .Where(k => !KnownKeys.Contains(k))
            .ToArray();

        if (unknown.Length > 0)
        {
            throw new OptionsValidationException(nameof(GatewayOptions), typeof(GatewayOptions),
                [$"Unknown configuration key: {string.Join(",", unknown)}"]);
        }

        var options = section.Get<GatewayOptions>()
            ?? throw new OptionsValidationException(nameof(GatewayOptions), typeof(GatewayOptions),
                ["Gateway section missing"]);

        if (options.HttpPort <= 0
            || options.MaxConnections <= 0
            || options.MaxConnectionsPerIp <= 0
            || options.MessagesPerSecondPerConnection <= 0)
        {
            throw new OptionsValidationException(nameof(GatewayOptions), typeof(GatewayOptions),
                ["Gateway options must be positive"]);
        }

        return options;
    }
}

using Ocb.Gateway.Configuration;
using Ocb.Gateway.Readiness;

var builder = WebApplication.CreateBuilder(args);

// Validate Gateway options only when not in test mode.
// In test mode the test host injects configuration via WebApplicationFactory.
if (!builder.Environment.IsEnvironment("IntegrationTest"))
{
    var gatewayOptions = GatewayBootstrap.ValidateAndBind(builder.Configuration);
    builder.Services.AddSingleton(gatewayOptions);
}

builder.Services.AddHealthChecks()
    .AddCheck<GatewayReadinessCheck>("readiness");

var app = builder.Build();

app.MapHealthChecks("/health/ready");

app.Run();

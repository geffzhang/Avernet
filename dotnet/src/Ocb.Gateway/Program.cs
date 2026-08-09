using Ocb.Gateway.Configuration;
using Ocb.Gateway.Readiness;
using Ocb.Gateway.Sse;

var builder = WebApplication.CreateBuilder(args);

// Validate Gateway options only when not in test mode.
// In test mode the test host injects configuration via WebApplicationFactory.
if (!builder.Environment.IsEnvironment("IntegrationTest"))
{
    var gatewayOptions = GatewayBootstrap.ValidateAndBind(builder.Configuration);
    builder.Services.AddSingleton(gatewayOptions);
}

// SSE admission gate — limits concurrent SSE streams.
// Can be overridden in tests via ConfigureTestServices.
builder.Services.AddSingleton(
    _ => new SseAdmissionGate(maxActiveStreams: 128));

builder.Services.AddHealthChecks()
    .AddCheck<GatewayReadinessCheck>("readiness");

var app = builder.Build();

// Principal verification middleware — validates JWT bearer token
// together with X-Avernet-Principal header. Enabled once
// IPrincipalTokenVerifier plugin is registered in DI.
// app.UseMiddleware<PrincipalVerificationMiddleware>();

app.MapHealthChecks("/health/ready");

// SSE endpoint — each request gets its own backpressure pump unless
// a shared pump is explicitly registered in DI (test mode).
app.MapGet("/openapi/v1/chat/messages/stream", async (HttpContext context) =>
{
    var admission = context.RequestServices.GetRequiredService<SseAdmissionGate>();
    // In test mode a shared pump is registered as singleton; in production
    // each request gets a dedicated pump tied to its session.
    var pump = context.RequestServices.GetService<SseBackpressurePump>()
               ?? new SseBackpressurePump(capacity: 256);
    await GatewaySseEndpoint.HandleAsync(context, admission, pump);
});

app.Run();

using Ocb.Runtime.Worker.Api;
using Ocb.Runtime.Worker.Api.WebSocket;
using Ocb.Runtime.Worker.Application;
using Ocb.Runtime.Worker.Infra.Clients.ClaudeCode;

// Ocb.Runtime.Worker — manages local processes, workspaces, and ports.
// Coordinates with Orleans grains for desired/observed state.
var builder = WebApplication.CreateBuilder(args);

// Typed client for engine session API — fail-closed ACL.
builder.Services.AddSingleton<IEngineSessionPort, ClaudeCodeRelayTypedClient>();

// Connection admission gate — shared across all WS connections.
var admissionGate = new EngineWsConnectionAdmissionGate(maxConnections: 128);
builder.Services.AddSingleton(admissionGate);

var app = builder.Build();

app.UseWebSockets();

app.MapGet("/health/ready", () => Results.Ok(new { status = "ready" }));

// Engine Session HTTP API parity routes.
SessionParityController.MapSessionRoutes(app);

// Engine WebSocket API parity route.
EngineWsEndpoint.MapRoutes(app, admissionGate);

app.Run();

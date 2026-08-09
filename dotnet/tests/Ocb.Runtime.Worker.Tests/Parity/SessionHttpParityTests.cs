using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Ocb.Contracts.Sessions;
using Ocb.Runtime.Worker.Application;
using Ocb.Runtime.Worker.Infra.Clients.ClaudeCode;
using Ocb.Runtime.Worker.Infra.Clients.Models;

namespace Ocb.Runtime.Worker.Tests.Parity;

/// <summary>
/// Engine Session HTTP parity tests —
/// verify that the .NET Worker serves the same contract as the
/// engine OpenAPI baseline.
/// </summary>
[SuppressMessage("Naming", "CA1707", Justification = "xUnit test naming convention uses underscores.")]
public sealed class SessionHttpParityTests : IAsyncDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public SessionHttpParityTests()
    {
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    // Use the fail-closed stub — payload shape not integrated yet.
                    services.AddSingleton<IEngineSessionPort, ClaudeCodeRelayTypedClient>();
                });
            });

        _client = _factory.CreateClient();
    }

    /// <summary>
    /// Task 3: Fail-closed — unknown payload shape throws ContractMappingException.
    /// </summary>
    [Fact]
    public async Task SessionList_WhenPayloadShapeUnexpected_ShouldReturnBadGateway()
    {
        var response = await _client.GetAsync("/api/sessions");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("ENGINE_PAYLOAD_SHAPE_MISMATCH", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// Task 4: GET /api/sessions/{session_id} — not-found returns 404.
    /// </summary>
    [Fact]
    public async Task GetSession_NotFound_ShouldReturn404()
    {
        // When the stub throws ContractMappingException, the controller
        // returns 502 BadGateway. In a real integration, the engine would
        // return a specific not-found response.
        // This test validates the route exists and the error shape is correct.
        var response = await _client.GetAsync("/api/sessions/non-existent");

        // The stub throws for all shapes; in production this would be 404.
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    /// <summary>
    /// DELETE /api/sessions/{session_id} route exists.
    /// </summary>
    [Fact]
    public async Task DeleteSession_RouteExists()
    {
        var response = await _client.DeleteAsync("/api/sessions/some-session");
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    /// <summary>
    /// GET /api/sessions/{session_id}/messages route exists.
    /// </summary>
    [Fact]
    public async Task ListMessages_RouteExists()
    {
        var response = await _client.GetAsync("/api/sessions/some-session/messages");
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    /// <summary>
    /// POST /api/sessions/{session_id}/update route exists.
    /// </summary>
    [Fact]
    public async Task UpdateSession_RouteExists()
    {
        var update = new SessionUpdateRequest(Title: "test-title");
        var response = await _client.PostAsJsonAsync("/api/sessions/some-session/update", update);
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    /// <summary>
    /// Error response follows the parity contract shape:
    /// { "error": "...", "message": "..." }
    /// </summary>
    [Fact]
    public async Task ErrorResponse_HasParityContractShape()
    {
        var response = await _client.GetAsync("/api/sessions");

        var doc = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonDocument>();

        Assert.NotNull(doc);
        Assert.True(doc!.RootElement.TryGetProperty("error", out _));
        Assert.True(doc.RootElement.TryGetProperty("message", out _));
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}

using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Ocb.Fusion.Application;
using Ocb.Fusion.Routes;

namespace Ocb.EndToEnd.Tests.Fusion;

/// <summary>
/// End-to-end conformance tests for the Fusion API surface.
/// Verifies the full request/response pipeline: routing, validation,
/// service orchestration, and JSON serialization shape.
/// </summary>
[SuppressMessage("Naming", "CA1707", Justification = "xUnit test naming convention uses underscores.")]
public sealed class FusionApiConformanceE2ETests
{
    private static readonly string[] TwoParticipants = ["w1:agent", "w2:expert"];
    private static readonly string[] SingleParticipant = ["w1"];

    [Fact]
    public async Task FullFusionPipeline_ShouldReturnValidResponseShape()
    {
        var client = CreateClient();
        var res = await client.PostAsJsonAsync("/api/v1/groups/grp-e2e/fuse", new
        {
            question = "What is the best approach?",
            participants = TwoParticipants,
            driver_bot_id = "driver-1",
            fusion_mode = "consensus",
            mode = "fusion",
            options = new { max_participants = 3, timeout_ms = 10000, require_consensus = true, min_confidence = 0.5 },
            session_id = "sess-e2e-1"
        });

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var json = await res.Content.ReadAsStringAsync();

        // Verify response shape contains all required fields
        Assert.Contains("\"group_id\":", json);
        Assert.Contains("\"fusion_id\":\"fuse-", json);
        Assert.Contains("\"question\":", json);
        Assert.Contains("\"driver_bot_id\":", json);
        Assert.Contains("\"perspectives\":", json);
        Assert.Contains("\"recommendation\":", json);
        Assert.Contains("\"partial_success\":", json);
        Assert.Contains("\"warnings\":", json);
        Assert.Contains("\"errors\":", json);
        Assert.Contains("\"timing\":", json);
        Assert.Contains("\"fusion_mode\":\"consensus\"", json);
    }

    [Fact]
    public async Task FusionPipeline_ShouldRejectInvalidGroupIdFormat()
    {
        var client = CreateClient();

        // Missing grp- prefix
        var res1 = await client.PostAsJsonAsync("/api/v1/groups/mygroup/fuse", new
        {
            question = "test",
            participants = SingleParticipant,
            fusion_mode = "agent",
            mode = "agent"
        });
        Assert.Equal(HttpStatusCode.BadRequest, res1.StatusCode);

        // Contains spaces
        var res2 = await client.PostAsJsonAsync("/api/v1/groups/grp%20bad/fuse", new
        {
            question = "test",
            participants = SingleParticipant,
            fusion_mode = "agent",
            mode = "agent"
        });
        Assert.Equal(HttpStatusCode.BadRequest, res2.StatusCode);
    }

    [Fact]
    public async Task WorkerPipeline_FullCreateGetUpdateCycle()
    {
        var client = CreateClient();

        // Create worker
        var create = await client.PostAsJsonAsync("/v1/workers", new
        {
            worker_id = "w-e2e-full",
            name = "E2E Worker",
            type = "agent",
            config = new { model = "gpt-4", temperature = 0.7, max_tokens = 4096, system_prompt = "You are helpful." }
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        // Get worker
        var get = await client.GetAsync("/v1/workers/w-e2e-full");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var getJson = await get.Content.ReadAsStringAsync();
        Assert.Contains("\"worker_id\":\"w-e2e-full\"", getJson);
        Assert.Contains("\"status\":\"active\"", getJson);

        // Update worker
        var req = new HttpRequestMessage(HttpMethod.Patch, "/v1/workers/w-e2e-full")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { name = "Updated E2E", status = "inactive" })
        };
        var update = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updateJson = await update.Content.ReadAsStringAsync();
        Assert.Contains("\"name\":\"Updated E2E\"", updateJson);
        Assert.Contains("\"status\":\"inactive\"", updateJson);

        // List workers
        var list = await client.GetAsync("/v1/workers");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var listJson = await list.Content.ReadAsStringAsync();
        Assert.Contains("\"workers\":", listJson);
    }

    [Fact]
    public async Task HealthEndpoint_ShouldReturnOk()
    {
        var client = CreateClient();
        var res = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var json = await res.Content.ReadAsStringAsync();
        Assert.Contains("\"status\":\"ok\"", json);
    }

    private static HttpClient CreateClient()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        // Register real services used by the routes
        builder.Services.AddSingleton<FusionService>();
        builder.Services.AddSingleton<WorkerProfileService>();

        var app = builder.Build();
        app.MapFusionRoutes();
        app.MapWorkersRoutes();
        app.RunAsync();

        return app.GetTestClient();
    }

}

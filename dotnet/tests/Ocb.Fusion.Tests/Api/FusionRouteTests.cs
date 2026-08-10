using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Ocb.Fusion.Application;
using Ocb.Fusion.Routes;

namespace Ocb.Fusion.Tests.Api;

[SuppressMessage("Naming", "CA1707", Justification = "xUnit test naming convention uses underscores.")]
public sealed class FusionRouteTests : IDisposable
{
    private static readonly string[] SingleParticipant = ["w1:default"];
    private static readonly string[] TwoParticipants = ["w1:default", "w2:expert"];

    private readonly HttpClient _client;

    public FusionRouteTests()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<FusionService>();

        var app = builder.Build();
        app.MapFusionRoutes();
        app.RunAsync();

        _client = app.GetTestClient();
    }

    public void Dispose()
    {
        _client?.Dispose();
    }

    [Fact]
    public async Task PostFuse_ShouldRejectInvalidGroupId()
    {
        var res = await _client.PostAsJsonAsync("/api/v1/groups/invalid/fuse", new
        {
            question = "q",
            participants = SingleParticipant,
            fusion_mode = "consensus",
            mode = "agent"
        });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task PostFuse_ShouldAccept_ValidGroupId()
    {
        var res = await _client.PostAsJsonAsync("/api/v1/groups/grp-test123/fuse", new
        {
            question = "What is the weather?",
            participants = TwoParticipants,
            driver_bot_id = "driver-1",
            fusion_mode = "consensus",
            mode = "agent"
        });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("\"group_id\":\"grp-test123\"", body);
        Assert.Contains("\"fusion_id\":\"fuse-", body);
        Assert.Contains("\"fusion_mode\":\"consensus\"", body);
    }

    [Fact]
    public async Task PostFuse_ShouldReturn_FuseResponseDto_Shape()
    {
        var res = await _client.PostAsJsonAsync("/api/v1/groups/grp-shape-test/fuse", new
        {
            question = "test",
            participants = SingleParticipant,
            fusion_mode = "agent",
            mode = "agent"
        });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var json = await res.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);

        Assert.True(doc.RootElement.TryGetProperty("perspectives", out _));
        Assert.True(doc.RootElement.TryGetProperty("recommendation", out _));
        Assert.True(doc.RootElement.TryGetProperty("timing", out _));
        Assert.True(doc.RootElement.TryGetProperty("partial_success", out _));
    }

    [Fact]
    public async Task PostFuse_ShouldReject_Get_MethodNotAllowed()
    {
        var res = await _client.GetAsync("/api/v1/groups/grp-test123/fuse");
        Assert.Equal(HttpStatusCode.MethodNotAllowed, res.StatusCode);
    }

    [Fact]
    public async Task PostFuse_ShouldAccept_Request_WithOptions()
    {
        var res = await _client.PostAsJsonAsync("/api/v1/groups/grp-opts/fuse", new
        {
            question = "test",
            participants = SingleParticipant,
            fusion_mode = "agent",
            mode = "agent",
            options = new { max_participants = 3, timeout_ms = 10000, require_consensus = true, min_confidence = 0.5 },
            session_id = "sess-1"
        });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }
}

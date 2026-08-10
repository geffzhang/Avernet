using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Ocb.Fusion.Application;
using Ocb.Fusion.Routes;

namespace Ocb.Fusion.Tests.Api;

[SuppressMessage("Naming", "CA1707", Justification = "xUnit test naming convention uses underscores.")]
public sealed class FusionRequestValidationTests : IDisposable
{
    private static readonly string[] SingleParticipant = ["p1"];
    private static readonly string[] NoParticipants = Array.Empty<string>();

    private readonly HttpClient _client;

    public FusionRequestValidationTests()
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
    public async Task PostFuse_EmptyParticipants_ShouldStillSucceed()
    {
        var res = await _client.PostAsJsonAsync("/api/v1/groups/grp-empty/fuse", new
        {
            question = "q",
            participants = NoParticipants,
            fusion_mode = "agent",
            mode = "agent"
        });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task PostFuse_MissingParticipantField_ShouldSucceedWithDefaults()
    {
        // Records with init-only properties allow missing fields (null defaults).
        // The service handles null gracefully with sensible fallbacks.
        var res = await _client.PostAsJsonAsync("/api/v1/groups/grp-missing/fuse", new
        {
            question = "q",
            fusion_mode = "agent",
            mode = "agent",
            participants = (string[]?)null
        });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("\"perspectives\":[]", body);
    }

    [Fact]
    public async Task PostFuse_GroupIdWithSpaces_ShouldReject()
    {
        var res = await _client.PostAsJsonAsync("/api/v1/groups/not%20valid/fuse", new
        {
            question = "q",
            participants = SingleParticipant,
            fusion_mode = "agent",
            mode = "agent"
        });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task PostFuse_GroupIdWithoutPrefix_ShouldReject()
    {
        var res = await _client.PostAsJsonAsync("/api/v1/groups/mygroup/fuse", new
        {
            question = "q",
            participants = SingleParticipant,
            fusion_mode = "agent",
            mode = "agent"
        });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task PostFuse_GroupIdWithSpecials_ShouldReject()
    {
        var res = await _client.PostAsJsonAsync("/api/v1/groups/grp-@bad!/fuse", new
        {
            question = "q",
            participants = SingleParticipant,
            fusion_mode = "agent",
            mode = "agent"
        });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}

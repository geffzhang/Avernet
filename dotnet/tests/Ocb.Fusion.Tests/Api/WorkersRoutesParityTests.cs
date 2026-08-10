using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Ocb.Contracts.Fusion;
using Ocb.Fusion.Application;
using Ocb.Fusion.Routes;

namespace Ocb.Fusion.Tests.Api;

[SuppressMessage("Naming", "CA1707", Justification = "xUnit test naming convention uses underscores.")]
public sealed class WorkersRoutesParityTests : IDisposable
{
    private readonly HttpClient _client;

    public WorkersRoutesParityTests()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<WorkerProfileService>();

        var app = builder.Build();
        app.MapWorkersRoutes();
        app.RunAsync();

        _client = app.GetTestClient();
    }

    public void Dispose()
    {
        _client?.Dispose();
    }

    [Theory]
    [InlineData("POST", "/v1/workers")]
    [InlineData("GET", "/v1/workers")]
    [InlineData("GET", "/health")]
    public async Task Routes_ShouldRespond(string method, string path)
    {
        HttpRequestMessage request = method switch
        {
            "POST" => new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = JsonContent.Create(new CreateWorkerRequest(
                    WorkerId: "w-test",
                    Name: "Test Worker",
                    Type: "agent",
                    Config: new WorkerConfigurationDto(Model: "gpt-4", Temperature: 0.7f, MaxTokens: 4096, SystemPrompt: null)))
            },
            _ => new HttpRequestMessage(HttpMethod.Get, path)
        };

        var res = await _client.SendAsync(request);
        Assert.True(res.IsSuccessStatusCode, $"Expected success for {method} {path}, got {(int)res.StatusCode}");
    }

    [Fact]
    public async Task PostCreateWorker_ShouldReturnCreated()
    {
        var req = new CreateWorkerRequest(
            WorkerId: "w-created",
            Name: "Created",
            Type: "agent",
            Config: new WorkerConfigurationDto(Model: "gpt-4", Temperature: 0.5f, MaxTokens: 2048, SystemPrompt: null));

        var res = await _client.PostAsJsonAsync("/v1/workers", req);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);

        var json = await res.Content.ReadAsStringAsync();
        Assert.Contains("\"worker_id\":\"w-created\"", json);
        Assert.Contains("\"status\":\"active\"", json);
    }

    [Fact]
    public async Task ListWorkers_ShouldReturnWorkers()
    {
        // Create one worker first
        await _client.PostAsJsonAsync("/v1/workers", new CreateWorkerRequest(
            WorkerId: "w-list", Name: "List", Type: "agent",
            Config: new WorkerConfigurationDto(Model: null, Temperature: 0, MaxTokens: 1024, SystemPrompt: null)));

        var res = await _client.GetAsync("/v1/workers");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var json = await res.Content.ReadAsStringAsync();
        Assert.Contains("\"workers\"", json);
        Assert.Contains("\"total\":1", json);
    }

    [Fact]
    public async Task GetWorker_Existing_ShouldReturnOk()
    {
        await _client.PostAsJsonAsync("/v1/workers", new CreateWorkerRequest(
            WorkerId: "w-get", Name: "Get", Type: "agent",
            Config: new WorkerConfigurationDto(Model: null, Temperature: 0, MaxTokens: 1024, SystemPrompt: null)));

        var res = await _client.GetAsync("/v1/workers/w-get");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var json = await res.Content.ReadAsStringAsync();
        Assert.Contains("\"worker_id\":\"w-get\"", json);
    }

    [Fact]
    public async Task GetWorker_NonExistent_ShouldReturnNotFound()
    {
        var res = await _client.GetAsync("/v1/workers/w-nonexistent");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);

        var json = await res.Content.ReadAsStringAsync();
        Assert.Contains("WORKER_NOT_FOUND", json);
    }

    [Fact]
    public async Task PatchWorker_Existing_ShouldUpdate()
    {
        await _client.PostAsJsonAsync("/v1/workers", new CreateWorkerRequest(
            WorkerId: "w-patch", Name: "Original", Type: "agent",
            Config: new WorkerConfigurationDto(Model: null, Temperature: 0, MaxTokens: 1024, SystemPrompt: null)));

        var patch = new UpdateWorkerRequest(Name: "Updated", Config: null, Status: "inactive");
        var req = new HttpRequestMessage(HttpMethod.Patch, "/v1/workers/w-patch")
        {
            Content = JsonContent.Create(patch)
        };

        var res = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var json = await res.Content.ReadAsStringAsync();
        Assert.Contains("\"name\":\"Updated\"", json);
        Assert.Contains("\"status\":\"inactive\"", json);
    }

    [Fact]
    public async Task PatchWorker_NonExistent_ShouldReturnNotFound()
    {
        var patch = new UpdateWorkerRequest(Name: "Nope", Config: null, Status: null);
        var req = new HttpRequestMessage(HttpMethod.Patch, "/v1/workers/w-missing")
        {
            Content = JsonContent.Create(patch)
        };

        var res = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Health_ShouldReturnOk()
    {
        var res = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var json = await res.Content.ReadAsStringAsync();
        Assert.Contains("\"status\":\"ok\"", json);
    }
}

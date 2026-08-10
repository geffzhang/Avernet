using System.Diagnostics.CodeAnalysis;
using Ocb.Contracts;
using Ocb.PluginApi.Baas.Sandbox;
using Ocb.Plugins.Sandbox.Docker;
using Ocb.Plugins.Sandbox.K8s;

namespace Ocb.Baas.Provider.Conformance.Tests;

/// <summary>
/// Shared conformance suite for sandbox providers.
/// Both Docker and K8s must satisfy the same contract.
/// </summary>
[SuppressMessage("Naming", "CA1707", Justification = "xUnit test naming convention uses underscores.")]
public sealed class SandboxProviderContractTests
{
    private static CallerContext TestCaller => new("test-tenant", "u-1", new HashSet<string> { "user" });

    private static ISandboxProvider CreateProvider(string profile)
    {
        return profile switch
        {
            "docker" => new DockerSandboxProvider(),
            "k8s" => new K8sSandboxProvider(),
            _ => throw new ArgumentException($"Unknown profile: {profile}"),
        };
    }

    [Theory]
    [InlineData("docker")]
    [InlineData("k8s")]
    public async Task Provider_MustSupport_Create_Destroy(string profile)
    {
        var provider = CreateProvider(profile);
        var request = new SandboxCreateRequest("bot-1", "tpl-1", profile, 10);

        var sandbox = await provider.CreateAsync(TestCaller, request);

        Assert.NotNull(sandbox);
        Assert.StartsWith(profile, sandbox.SandboxId, StringComparison.Ordinal);
        Assert.Equal("bot-1", sandbox.BotId);
        Assert.NotEmpty(sandbox.InternalEndpoint);

        await provider.DestroyAsync(TestCaller, sandbox.SandboxId);
    }

    [Theory]
    [InlineData("docker")]
    [InlineData("k8s")]
    public async Task Provider_MustSupport_GetStatus(string profile)
    {
        var provider = CreateProvider(profile);
        var request = new SandboxCreateRequest("bot-2", "tpl-1", profile, 5);

        var sandbox = await provider.CreateAsync(TestCaller, request);
        var status = await provider.GetStatusAsync(TestCaller, sandbox.SandboxId);

        Assert.Equal("running", status.Status);
        Assert.Equal(sandbox.SandboxId, status.SandboxId);
    }

    [Theory]
    [InlineData("docker")]
    [InlineData("k8s")]
    public async Task Provider_MustSupport_InvokeHttp(string profile)
    {
        var provider = CreateProvider(profile);
        var request = new SandboxCreateRequest("bot-3", "tpl-1", profile, 5);

        var sandbox = await provider.CreateAsync(TestCaller, request);
        var response = await provider.InvokeHttpAsync(TestCaller,
            new SandboxInvokeHttpRequest(sandbox.SandboxId, "GET", 8080, "/health", null, null));

        Assert.Equal(200, response.StatusCode);
    }

    [Fact]
    public void DockerProvider_Implements_Conformance()
    {
        var provider = new DockerSandboxProvider();
        Assert.IsAssignableFrom<ISandboxConformanceProvider>(provider);
    }

    [Fact]
    public void K8sProvider_Implements_Conformance()
    {
        var provider = new K8sSandboxProvider();
        Assert.IsAssignableFrom<ISandboxConformanceProvider>(provider);
    }

    [Fact]
    public void BothProfiles_AreDistinct()
    {
        var docker = new DockerSandboxProvider();
        var k8s = new K8sSandboxProvider();

        Assert.NotEqual(docker.Profile, k8s.Profile);
    }
}

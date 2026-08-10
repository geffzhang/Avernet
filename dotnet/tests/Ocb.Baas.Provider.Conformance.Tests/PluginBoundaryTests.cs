using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Ocb.PluginApi.Baas.Crypto;
using Ocb.PluginApi.Baas.Gateway;
using Ocb.PluginApi.Baas.Persistence;
using Ocb.PluginApi.Baas.Queue;
using Ocb.PluginApi.Baas.Sandbox;

namespace Ocb.Baas.Provider.Conformance.Tests;

/// <summary>
/// Verify Plugin API boundary isolation: no ASP.NET Core or EF Core
/// references in PluginApi, all expected interfaces present.
/// </summary>
[SuppressMessage("Naming", "CA1707", Justification = "xUnit test naming convention uses underscores.")]
public sealed class PluginBoundaryTests
{
    [Fact]
    public void PluginApi_MustNotReference_AspNetCore_Or_EfCore()
    {
        var asm = typeof(ISandboxProvider).Assembly;
        var refs = asm.GetReferencedAssemblies().Select(x => x.Name).ToArray();
        Assert.DoesNotContain("Microsoft.AspNetCore.Http", refs);
        Assert.DoesNotContain("Microsoft.EntityFrameworkCore", refs);
    }

    [Fact]
    public void SandboxProvider_MustExpose_Create_Destroy_InvokeHttp_GetStatus()
    {
        var type = typeof(ISandboxProvider);
        Assert.NotNull(type.GetMethod("CreateAsync"));
        Assert.NotNull(type.GetMethod("DestroyAsync"));
        Assert.NotNull(type.GetMethod("InvokeHttpAsync"));
        Assert.NotNull(type.GetMethod("GetStatusAsync"));
    }

    [Fact]
    public void Sm4CryptoProvider_MustExpose_Encrypt_Decrypt()
    {
        var type = typeof(ISm4CryptoProvider);
        Assert.NotNull(type.GetMethod("Encrypt"));
        Assert.NotNull(type.GetMethod("Decrypt"));
    }

    [Fact]
    public void Sm4KeyResolver_MustExpose_Resolve()
    {
        var type = typeof(ISm4KeyResolver);
        Assert.NotNull(type.GetMethod("Resolve"));
    }

    [Fact]
    public void BotRunQueueProvider_MustExpose_Enqueue_Lease_Ack_DeadLetter()
    {
        var type = typeof(IBotRunQueueProvider);
        Assert.NotNull(type.GetMethod("EnqueueAsync"));
        Assert.NotNull(type.GetMethod("LeaseAsync"));
        Assert.NotNull(type.GetMethod("AckAsync"));
        Assert.NotNull(type.GetMethod("DeadLetterAsync"));
    }

    [Fact]
    public void ExternalGatewayProxy_MustExpose_SendChat_And_Stream()
    {
        var type = typeof(IExternalGatewayProxy);
        Assert.NotNull(type.GetMethod("SendChatAsync"));
        Assert.NotNull(type.GetMethod("SendChatStreamAsync"));
    }

    [Fact]
    public void Repositories_MustExpose_Standard_Crud()
    {
        Assert.NotNull(typeof(ITenantRepository).GetMethod("GetByTenantIdAsync"));
        Assert.NotNull(typeof(ITemplateRepository).GetMethod("GetByTenantAndUuidAsync"));
        Assert.NotNull(typeof(IDeviceRepository).GetMethod("GetByIdAsync"));
        Assert.NotNull(typeof(IPublishRepository).GetMethod("GetByIdAsync"));
        Assert.NotNull(typeof(IBotRunQueueRepository).GetMethod("EnqueueAsync"));
    }
}

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Ocb.Contracts;
using Ocb.Contracts.Baas.Device;
using Ocb.Contracts.Baas.Health;
using Ocb.Contracts.Baas.Publish;
using Ocb.Contracts.Baas.Qpm;
using Ocb.Contracts.Baas.Sse;
using Ocb.Contracts.Baas.Template;
using Ocb.Contracts.Baas.Tenant;

namespace Ocb.Baas.Contracts.Tests;

/// <summary>
/// Verify BaaS service contract shape: CallerContext must be
/// an explicit parameter, no HttpContext dependency, all expected
/// methods present.
/// </summary>
[SuppressMessage("Naming", "CA1707", Justification = "xUnit test naming convention uses underscores.")]
public sealed class BaasContractShapeTests
{
    [Fact]
    public void DeviceContract_MustExpose_InvokeHttp_And_WsInfo()
    {
        var type = typeof(IDeviceServiceContract);
        var invokeMethod = type.GetMethod("InvokeHttpAsync");
        Assert.NotNull(invokeMethod);
        Assert.Contains(invokeMethod.GetParameters(),
            p => p.ParameterType == typeof(CallerContext));
        Assert.NotNull(type.GetMethod("ResolveWsInfoAsync"));
    }

    [Fact]
    public void DeviceContract_Methods_MustNotAcceptHttpContext()
    {
        var type = typeof(IDeviceServiceContract);
        foreach (var method in type.GetMethods())
        {
            Assert.DoesNotContain(method.GetParameters(),
                p => p.ParameterType == typeof(Microsoft.AspNetCore.Http.HttpContext));
        }
    }

    [Fact]
    public void AllServiceContracts_Methods_MustHaveCallerContext()
    {
        var contractTypes = new[]
        {
            typeof(IDeviceServiceContract),
            typeof(ITemplateServiceContract),
            typeof(ITenantServiceContract),
            typeof(IQpmServiceContract),
            typeof(IPublishServiceContract),
            typeof(IHealthServiceContract),
            typeof(ISseServiceContract),
        };

        foreach (var type in contractTypes)
        {
            foreach (var method in type.GetMethods())
            {
                Assert.Contains(method.GetParameters(),
                    p => p.ParameterType == typeof(CallerContext));
            }
        }
    }

    [Fact]
    public void HealthContract_MustExpose_GetHealth()
    {
        var type = typeof(IHealthServiceContract);
        var method = type.GetMethod("GetHealthAsync");
        Assert.NotNull(method);
        Assert.Contains(method.GetParameters(),
            p => p.ParameterType == typeof(CallerContext));
    }

    [Fact]
    public void TemplateContract_MustExpose_Crud_Operations()
    {
        var type = typeof(ITemplateServiceContract);
        Assert.NotNull(type.GetMethod("GetTemplateAsync"));
        Assert.NotNull(type.GetMethod("ListTemplatesAsync"));
        Assert.NotNull(type.GetMethod("CreateTemplateAsync"));
        Assert.NotNull(type.GetMethod("UpdateTemplateAsync"));
        Assert.NotNull(type.GetMethod("DeleteTemplateAsync"));
    }

    [Fact]
    public void TenantContract_MustExpose_Get_Update_Verify()
    {
        var type = typeof(ITenantServiceContract);
        Assert.NotNull(type.GetMethod("GetTenantAsync"));
        Assert.NotNull(type.GetMethod("UpdateTenantAsync"));
        Assert.NotNull(type.GetMethod("VerifyTenantAsync"));
    }

    [Fact]
    public void QpmContract_MustExpose_RateLimit_And_Usage()
    {
        var type = typeof(IQpmServiceContract);
        Assert.NotNull(type.GetMethod("GetQpmConfigAsync"));
        Assert.NotNull(type.GetMethod("UpdateQpmConfigAsync"));
        Assert.NotNull(type.GetMethod("CheckRateLimitAsync"));
        Assert.NotNull(type.GetMethod("RecordUsageAsync"));
    }

    [Fact]
    public void PublishContract_MustExpose_FullLifecycle()
    {
        var type = typeof(IPublishServiceContract);
        Assert.NotNull(type.GetMethod("CreatePublishAsync"));
        Assert.NotNull(type.GetMethod("GetPublishAsync"));
        Assert.NotNull(type.GetMethod("ApproveStageAsync"));
        Assert.NotNull(type.GetMethod("RejectPublishAsync"));
        Assert.NotNull(type.GetMethod("RevokePublishAsync"));
        Assert.NotNull(type.GetMethod("RetryPublishAsync"));
        Assert.NotNull(type.GetMethod("CompletePublishAsync"));
    }

    [Fact]
    public void SseContract_MustExpose_ConvertToSse()
    {
        var type = typeof(ISseServiceContract);
        var method = type.GetMethod("ConvertToSseAsync");
        Assert.NotNull(method);
        Assert.Contains(method.GetParameters(),
            p => p.ParameterType == typeof(CallerContext));
    }

    [Fact]
    public void PublishStatus_Constants_MustMatchExpected()
    {
        Assert.Equal("pending", PublishStatus.Pending);
        Assert.Equal("reviewing", PublishStatus.Reviewing);
        Assert.Equal("active", PublishStatus.Active);
        Assert.Equal("rejected", PublishStatus.Rejected);
        Assert.Equal("revoked", PublishStatus.Revoked);
        Assert.Equal("failed", PublishStatus.Failed);
        Assert.Equal("completed", PublishStatus.Completed);
    }
}

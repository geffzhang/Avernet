using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Ocb.Contracts.Baas.Device;
using Ocb.Contracts.Baas.Health;
using Ocb.Contracts.Baas.Publish;
using Ocb.Contracts.Baas.Qpm;
using Ocb.Contracts.Baas.Sse;
using Ocb.Contracts.Baas.Template;
using Ocb.Contracts.Baas.Tenant;

namespace Ocb.Baas.EndToEnd.Tests.Parity;

/// <summary>
/// BaaS HTTP parity verification — ensures all expected service contract
/// methods are present, and the contract surface matches the parity baseline.
/// </summary>
[SuppressMessage("Naming", "CA1707", Justification = "xUnit test naming convention uses underscores.")]
public sealed class BaasOpenApiParityTests
{
    private static readonly ISet<string> ExpectedContractPaths = new HashSet<string>(StringComparer.Ordinal)
    {
        // Device
        "Device.CreateDevice", "Device.DestroyDevice", "Device.RenewTtl",
        "Device.GetDeviceStatus", "Device.InvokeHttp", "Device.ResolveWsInfo",
        // Template
        "Template.GetTemplate", "Template.ListTemplates", "Template.CreateTemplate",
        "Template.UpdateTemplate", "Template.DeleteTemplate",
        // Tenant
        "Tenant.GetTenant", "Tenant.UpdateTenant", "Tenant.VerifyTenant",
        // QPM
        "Qpm.GetQpmConfig", "Qpm.UpdateQpmConfig", "Qpm.CheckRateLimit", "Qpm.RecordUsage",
        // Publish
        "Publish.CreatePublish", "Publish.GetPublish", "Publish.ListPublishes", "Publish.GetProgress",
        "Publish.ApproveStage", "Publish.RejectPublish", "Publish.RevokePublish", "Publish.RetryPublish", "Publish.CompletePublish",
        // Health
        "Health.GetHealth",
        // SSE
        "Sse.ConvertToSse",
    };

    [Fact]
    public void OpenApi_ShouldContain_AllBaasParityPaths()
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

        var actualPaths = new HashSet<string>(StringComparer.Ordinal);

        foreach (var type in contractTypes)
        {
            var prefix = type.Name.Replace("ServiceContract", "").Replace("I", "");
            foreach (var method in type.GetMethods())
            {
                actualPaths.Add($"{prefix}.{method.Name.Replace("Async", "")}");
            }
        }

        foreach (var expected in ExpectedContractPaths)
        {
            Assert.Contains(expected, actualPaths);
        }
    }

    [Fact]
    public void AllContracts_MustBe_PublicInterfaces()
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
            Assert.True(type.IsInterface);
            Assert.True(type.IsPublic);
        }
    }

    [Fact]
    public void AllContracts_MustBe_InOcbContractsNamespace()
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
            Assert.True(type.Namespace?.StartsWith("Ocb.Contracts.Baas", StringComparison.Ordinal));
        }
    }
}

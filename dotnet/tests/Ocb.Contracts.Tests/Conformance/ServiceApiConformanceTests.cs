using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Ocb.Contracts.Skills;
using Ocb.PluginApi;

namespace Ocb.Contracts.Tests.Conformance;

/// <summary>
/// Verifies that Service API types (Runtime materialization) live in
/// <c>Ocb.Contracts</c> and are not defined as Plugin API ports.
/// </summary>
[SuppressMessage("Naming", "CA1707", Justification = "xUnit test naming convention uses underscores.")]
public sealed class ServiceApiConformanceTests
{
    [Fact]
    public void RuntimeMaterialization_IsDefinedInContracts_NotPluginApi()
    {
        var serviceType = typeof(IRuntimeSkillMaterializationService);
        Assert.Equal("Ocb.Contracts", serviceType.Assembly.GetName().Name);

        var pluginApiTypes = typeof(IPluginContract).Assembly.GetTypes();
        Assert.DoesNotContain(pluginApiTypes, t => t.Name.Contains("Materialization", StringComparison.Ordinal));
    }

    [Fact]
    public void AllServiceApiDtos_AreInOcbContracts()
    {
        var contractsAssembly = typeof(SkillPublicationRecord).Assembly;

        var requiredTypes = new[]
        {
            typeof(SkillPublicationRecord),
            typeof(SkillVersionRef),
            typeof(SkillActivationRequest),
            typeof(ActivatedSkillVersion),
            typeof(MaterializationRequest),
            typeof(MaterializationResult),
        };

        foreach (var t in requiredTypes)
        {
            Assert.Equal("Ocb.Contracts", t.Assembly.GetName().Name);
        }
    }

    [Fact]
    public void Stage5PluginPorts_AllExtendIPluginContract()
    {
        // Only check Stage-5 plugin ports (Storage, Skills, Identity namespaces).
        var stage5NamespacePrefixes = new[] { "Ocb.PluginApi.Storage", "Ocb.PluginApi.Skills", "Ocb.PluginApi.Identity" };
        var pluginAssembly = typeof(IPluginContract).Assembly;
        var stage5Ports = pluginAssembly.GetTypes()
            .Where(t => t.IsInterface && t != typeof(IPluginContract))
            .Where(t => t.Name.EndsWith("Plugin", StringComparison.Ordinal))
            .Where(t => t.Namespace is not null
                && stage5NamespacePrefixes.Any(ns => t.Namespace.StartsWith(ns, StringComparison.Ordinal)));

        Assert.NotEmpty(stage5Ports);
        foreach (var iface in stage5Ports)
        {
            Assert.True(typeof(IPluginContract).IsAssignableFrom(iface),
                $"{iface.Name} must extend IPluginContract");
        }
    }

    [Fact]
    public void CallerContext_InContracts_HasTenantSubjectRoles()
    {
        var callerType = typeof(Ocb.Contracts.CallerContext);
        var properties = callerType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        Assert.Contains(properties, p => p.Name == "TenantId");
        Assert.Contains(properties, p => p.Name == "SubjectId");
        Assert.Contains(properties, p => p.Name == "Roles");
    }
}

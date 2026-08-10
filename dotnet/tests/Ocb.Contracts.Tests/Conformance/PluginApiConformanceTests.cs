using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Ocb.Contracts;
using Ocb.PluginApi;

namespace Ocb.Contracts.Tests.Conformance;

/// <summary>
/// Verifies that Plugin API ports are correctly structured and do not leak
/// implementation concerns into the contract layer.
/// </summary>
[SuppressMessage("Naming", "CA1707", Justification = "xUnit test naming convention uses underscores.")]
public sealed class PluginApiConformanceTests
{
    [Fact]
    public void PluginApiAssembly_HasNoEfCoreOrNpgsqlDependency()
    {
        var pluginAssembly = typeof(IPluginContract).Assembly;
        var refs = pluginAssembly.GetReferencedAssemblies()
            .Select(r => r.Name)
            .ToHashSet();

        Assert.DoesNotContain(refs, r => r!.Contains("EntityFramework", StringComparison.Ordinal));
        Assert.DoesNotContain(refs, r => r!.Contains("Npgsql", StringComparison.Ordinal));
        Assert.DoesNotContain(refs, r => r!.Contains("Minio", StringComparison.Ordinal));
        Assert.DoesNotContain(refs, r => r!.Contains("Orleans", StringComparison.Ordinal));
    }

    [Fact]
    public void AllPluginPorts_AreTenantScoped()
    {
        var pluginAssembly = typeof(IPluginContract).Assembly;
        var pluginTypes = pluginAssembly.GetTypes()
            .Where(t => t.IsInterface && typeof(IPluginContract).IsAssignableFrom(t) && t != typeof(IPluginContract))
            .ToList();

        Assert.NotEmpty(pluginTypes);

        // Stage-5 plugin ports must have async/awaitable return types.
        var stage5Namespaces = new[] { "Ocb.PluginApi.Storage", "Ocb.PluginApi.Skills", "Ocb.PluginApi.Identity" };
        foreach (var port in pluginTypes)
        {
            if (port.Namespace is null || !stage5Namespaces.Contains(port.Namespace, StringComparer.Ordinal))
                continue;

            var methods = port.GetMethods(BindingFlags.Public | BindingFlags.Instance);
            foreach (var m in methods)
            {
                Assert.True(
                    m.ReturnType.Name.StartsWith("Task", StringComparison.Ordinal)
                    || m.ReturnType.Name.StartsWith("ValueTask", StringComparison.Ordinal),
                    $"{port.Name}.{m.Name} should return Task or ValueTask");
            }
        }
    }

    [Fact]
    public void PluginApiPorts_DoNotExposeEFCoreOrDatabaseTypes()
    {
        var pluginAssembly = typeof(IPluginContract).Assembly;
        var allTypes = pluginAssembly.GetExportedTypes();
        var typeNames = allTypes.Select(t => t.Name).ToHashSet();

        Assert.DoesNotContain(typeNames, n => n.Contains("DbContext", StringComparison.Ordinal));
        Assert.DoesNotContain(typeNames, n => n.Contains("DbSet", StringComparison.Ordinal));
        Assert.DoesNotContain(typeNames, n => n.Contains("Migration", StringComparison.Ordinal));
        Assert.DoesNotContain(typeNames, n => n.Contains("Entity", StringComparison.Ordinal));
    }

    [Fact]
    public void Stage5PluginPorts_ExistAndAreCorrectlyNamespaced()
    {
        // Verify we have the three expected Stage-5 Plugin API ports
        var pluginAssembly = typeof(IPluginContract).Assembly;

        var storagePort = pluginAssembly.GetType("Ocb.PluginApi.Storage.IAssetObjectStoragePlugin");
        Assert.NotNull(storagePort);

        var skillsPort = pluginAssembly.GetType("Ocb.PluginApi.Skills.ISkillPublicationStorePlugin");
        Assert.NotNull(skillsPort);

        var identityPort = pluginAssembly.GetType("Ocb.PluginApi.Identity.ICallerIdentityRepositoryPlugin");
        Assert.NotNull(identityPort);
    }
}

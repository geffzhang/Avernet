using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Ocb.Backend.Tests.Skills;

/// <summary>
/// Verifies that Ocb.Backend follows architectural rules: it depends on
/// Contracts and PluginApi, not on infrastructure assemblies.
/// </summary>
[SuppressMessage("Naming", "CA1707", Justification = "xUnit test naming convention uses underscores.")]
public sealed class BackendArchitectureTests
{
    [Fact]
    public void BackendAssembly_ReferencesContracts_NotInfrastructure()
    {
        var backend = typeof(Ocb.Backend.Skills.Publication.SkillPublicationStateMachine).Assembly;
        var refs = backend.GetReferencedAssemblies().Select(r => r.Name).ToHashSet();

        Assert.DoesNotContain(refs, r => r!.Contains("PostgreSql", StringComparison.Ordinal));
        Assert.DoesNotContain(refs, r => r!.Contains("Minio", StringComparison.Ordinal));
        Assert.Contains(refs, r => r == "Ocb.Contracts");
        Assert.Contains(refs, r => r == "Ocb.PluginApi");
    }
}

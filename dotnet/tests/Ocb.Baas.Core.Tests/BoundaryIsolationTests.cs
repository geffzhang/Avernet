using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Ocb.Baas.Core.Common;
using Ocb.PluginApi.Baas.Crypto;

namespace Ocb.Baas.Core.Tests;

/// <summary>
/// Verify BaaS boundary isolation: Ocb.Baas must not reference
/// Backend or Fusion implementation assemblies.
/// </summary>
[SuppressMessage("Naming", "CA1707", Justification = "xUnit test naming convention uses underscores.")]
public sealed class BoundaryIsolationTests
{
    [Fact]
    public void OcbBaas_MustNotReference_Backend_Or_Fusion_Implementations()
    {
        var asm = typeof(TenantGuard).Assembly;
        var refs = asm.GetReferencedAssemblies().Select(a => a.Name).ToArray();

        Assert.DoesNotContain("Ocb.Backend", refs);
        Assert.DoesNotContain("Ocb.Fusion", refs);
    }

    [Fact]
    public void PluginApi_MustNotReference_Backend_Or_Fusion()
    {
        var asm = typeof(ISm4CryptoProvider).Assembly;
        var refs = asm.GetReferencedAssemblies().Select(a => a.Name).ToArray();

        Assert.DoesNotContain("Ocb.Backend", refs);
        Assert.DoesNotContain("Ocb.Fusion", refs);
    }

    [Fact]
    public void OcbBaas_DoesNot_DirectlyDepend_OnPlugins()
    {
        // BaaS Core should only depend on PluginApi interfaces, not plugin implementations
        var asm = typeof(TenantGuard).Assembly;
        var refs = asm.GetReferencedAssemblies().Select(a => a.Name).ToArray();

        // These plugin implementations should NOT be direct dependencies of BaaS Core
        Assert.DoesNotContain("Ocb.Plugins.Sandbox.Docker", refs);
        Assert.DoesNotContain("Ocb.Plugins.Sandbox.K8s", refs);
        Assert.DoesNotContain("Ocb.Plugins.Crypto.SM4", refs);
    }
}

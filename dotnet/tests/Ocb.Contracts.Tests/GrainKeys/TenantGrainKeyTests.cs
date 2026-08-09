using Ocb.GrainContracts.GrainKeys;

namespace Ocb.Contracts.Tests.GrainKeys;

public sealed class TenantGrainKeyTests
{
    [Fact]
    public void DirectoryKeyIsTenantScoped()
    {
        var key = TenantGrainKey.Directory("t-abc");
        Assert.StartsWith("directory/", key);
        Assert.EndsWith("t-abc", key);
    }
}

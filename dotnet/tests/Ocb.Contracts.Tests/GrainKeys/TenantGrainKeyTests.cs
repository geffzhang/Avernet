using Ocb.GrainContracts.Bot;
using Ocb.GrainContracts.Device;
using Ocb.GrainContracts.GrainKeys;
using Ocb.GrainContracts.Session;

namespace Ocb.Contracts.Tests.GrainKeys;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1707", Justification = "xUnit test naming convention uses underscores.")]
public sealed class TenantGrainKeyTests
{
    [Fact]
    public void DirectoryKeyIsTenantScoped()
    {
        var key = TenantGrainKey.Directory("t-abc");
        Assert.StartsWith("directory/", key);
        Assert.EndsWith("t-abc", key);
    }

    [Fact]
    public void TenantGrainKey_IsDeterministicAndTenantScoped()
    {
        var a = TenantGrainKey.Build("tenant-a", "bot-1");
        var b = TenantGrainKey.Build("tenant-b", "bot-1");

        Assert.NotEqual(a, b);
        Assert.Equal(a, TenantGrainKey.Build("tenant-a", "bot-1"));
    }

    [Fact]
    public void BotKeyIsTenantScoped()
    {
        var keyA = TenantGrainKey.Bot("tenant-a", "bot-1");
        var keyB = TenantGrainKey.Bot("tenant-b", "bot-1");

        Assert.NotEqual(keyA, keyB);
        Assert.StartsWith("bot/tenant-a/", keyA);
        Assert.Equal(keyA, TenantGrainKey.Bot("tenant-a", "bot-1"));
    }

    [Fact]
    public void SessionKeyIsTenantScoped()
    {
        var keyA = TenantGrainKey.Session("tenant-a", "session-1");
        var keyB = TenantGrainKey.Session("tenant-b", "session-1");

        Assert.NotEqual(keyA, keyB);
        Assert.StartsWith("session/tenant-a/", keyA);
    }

    [Fact]
    public void DeviceKeyIsDeterministic()
    {
        var key = TenantGrainKey.Device("dev-abc");
        Assert.StartsWith("device/", key);
        Assert.Equal(key, TenantGrainKey.Device("dev-abc"));
    }

    [Fact]
    public void IBotGrain_DeclaresReconcileSkillsMethod()
    {
        var method = typeof(IBotGrain).GetMethod(nameof(IBotGrain.ReconcileSkillsAsync))!;

        var parameters = method.GetParameters();
        Assert.Single(parameters);
        Assert.Equal(typeof(BotSkillReconciliationCommand), parameters[0].ParameterType);
    }

    [Fact]
    public void ISessionGrain_DeclaresAssetManagementMethods()
    {
        var recordAsset = typeof(ISessionGrain).GetMethod(nameof(ISessionGrain.RecordAssetAsync))!;
        Assert.NotNull(recordAsset);
        Assert.Equal(typeof(string), recordAsset.GetParameters()[2].ParameterType); // resourceId

        var getSnapshot = typeof(ISessionGrain).GetMethod(nameof(ISessionGrain.GetAssetSnapshotAsync))!;
        Assert.NotNull(getSnapshot);
    }

    [Fact]
    public void IDeviceGrain_DeclaresLifecycleMethods()
    {
        var register = typeof(IDeviceGrain).GetMethod(nameof(IDeviceGrain.RegisterAsync))!;
        Assert.NotNull(register);

        var getSnapshot = typeof(IDeviceGrain).GetMethod(nameof(IDeviceGrain.GetSnapshotAsync))!;
        Assert.NotNull(getSnapshot);
    }
}

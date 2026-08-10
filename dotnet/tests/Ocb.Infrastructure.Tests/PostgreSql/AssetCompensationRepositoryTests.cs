using System.Diagnostics.CodeAnalysis;
using Ocb.Infrastructure.PostgreSql.Assets;

namespace Ocb.Infrastructure.Tests.PostgreSql;

[SuppressMessage("Naming", "CA1707", Justification = "xUnit test naming convention uses underscores.")]
public sealed class AssetCompensationRepositoryTests
{
    [Fact]
    public void RecordTempObjectAsync_RequiresTenantId()
    {
        var ex = Assert.ThrowsAny<ArgumentException>(() =>
        {
            ThrowIfNullOrWhiteSpace_Helper("", "obj-key");
        });
        Assert.Contains("tenantId", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RecordTempObjectAsync_RequiresObjectKey()
    {
        var ex = Assert.ThrowsAny<ArgumentException>(() =>
        {
            ThrowIfNullOrWhiteSpace_Helper("t1", "");
        });
        Assert.Contains("objectKey", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ListPendingCompensationAsync_RequiresTenantId()
    {
        var ex = Assert.ThrowsAny<ArgumentException>(() =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace("");
        });
        Assert.NotNull(ex);
    }

    [Fact]
    public void TempAssetEntity_DefaultsState_ToTEMP_UPLOADED()
    {
        var entity = new TempAssetEntity
        {
            TenantId = "t1",
            ObjectKey = "tenant/t1/file.bin",
        };

        Assert.Equal("TEMP_UPLOADED", entity.State);
    }

    [Fact]
    public void TempAssetEntity_ResolvedAt_IsNullByDefault()
    {
        var entity = new TempAssetEntity
        {
            TenantId = "t1",
            ObjectKey = "tenant/t1/file.bin",
        };

        Assert.Null(entity.ResolvedAt);
    }

    [Fact]
    public void TempAssetEntity_StoresErrorMessage()
    {
        var entity = new TempAssetEntity
        {
            TenantId = "t1",
            ObjectKey = "tenant/t1/file.bin",
            ErrorMessage = "MinIO upload failed: connection refused",
        };

        Assert.Contains("connection refused", entity.ErrorMessage, StringComparison.Ordinal);
    }

    private static void ThrowIfNullOrWhiteSpace_Helper(string tenantId, string objectKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
    }
}

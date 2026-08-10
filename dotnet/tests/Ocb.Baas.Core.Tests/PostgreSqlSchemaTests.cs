using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Ocb.Infrastructure.PostgreSql.Baas;
using Ocb.Infrastructure.PostgreSql.Baas.Entities;

namespace Ocb.Baas.Core.Tests;

/// <summary>
/// Verify PostgreSQL ocfb_business schema: entity mappings,
/// table names, indexes, and unique constraints.
/// </summary>
[SuppressMessage("Naming", "CA1707", Justification = "xUnit test naming convention uses underscores.")]
public sealed class PostgreSqlSchemaTests
{
    private static DbContextOptions<OcbBusinessDbContext> CreateInMemoryOptions()
    {
        return new DbContextOptionsBuilder<OcbBusinessDbContext>()
            .UseInMemoryDatabase($"baas_{Guid.NewGuid():N}")
            .Options;
    }

    [Fact]
    public async Task Should_Create_Tenant_Template_Device_Publish_Queue_Tables_In_OcbBusiness()
    {
        await using var db = new OcbBusinessDbContext(CreateInMemoryOptions());
        var model = db.Model;

        var entityTypes = model.GetEntityTypes().Select(e => e.ShortName()).ToHashSet();

        Assert.Contains("TenantEntity", entityTypes);
        Assert.Contains("TemplateEntity", entityTypes);
        Assert.Contains("DeviceEntity", entityTypes);
        Assert.Contains("PublishEntity", entityTypes);
        Assert.Contains("BotRunQueueEntity", entityTypes);
    }

    [Fact]
    public async Task All_Tables_MustBe_In_OcbBusinessSchema()
    {
        // Verify the DbContext configures the default schema as "ocb_business".
        await using var db = new OcbBusinessDbContext(CreateInMemoryOptions());
        var model = db.Model;

        // The InMemory provider doesn't fully resolve schemas, but the
        // model metadata still carries the default schema setting.
        var defaultSchema = model.GetDefaultSchema();
        Assert.Equal("ocb_business", defaultSchema);
    }

    [Fact]
    public async Task Template_HasUnique_Tenant_Uuid_Index()
    {
        await using var db = new OcbBusinessDbContext(CreateInMemoryOptions());
        var entity = db.Model.FindEntityType(typeof(TemplateEntity))!;

        var indexes = entity.GetIndexes();
        Assert.Contains(indexes, idx =>
            idx.Properties.Any(p => p.Name == "TenantId") &&
            idx.Properties.Any(p => p.Name == "Uuid") &&
            idx.IsUnique);
    }

    [Fact]
    public async Task Tenant_HasStatus_Index()
    {
        await using var db = new OcbBusinessDbContext(CreateInMemoryOptions());
        var entity = db.Model.FindEntityType(typeof(TenantEntity))!;

        var indexes = entity.GetIndexes();
        Assert.Contains(indexes, idx =>
            idx.Properties.Any(p => p.Name == "Status"));
    }

    [Fact]
    public async Task Device_HasTenant_And_Bot_Indexes()
    {
        await using var db = new OcbBusinessDbContext(CreateInMemoryOptions());
        var entity = db.Model.FindEntityType(typeof(DeviceEntity))!;

        var indexes = entity.GetIndexes();
        Assert.Contains(indexes, idx =>
            idx.Properties.Any(p => p.Name == "TenantId"));
        Assert.Contains(indexes, idx =>
            idx.Properties.Any(p => p.Name == "BotId"));
    }

    [Fact]
    public async Task Publish_HasTenant_And_BotVersion_Indexes()
    {
        await using var db = new OcbBusinessDbContext(CreateInMemoryOptions());
        var entity = db.Model.FindEntityType(typeof(PublishEntity))!;

        var indexes = entity.GetIndexes();
        Assert.Contains(indexes, idx =>
            idx.Properties.Any(p => p.Name == "TenantId"));
        Assert.Contains(indexes, idx =>
            idx.Properties.Any(p => p.Name == "BotId") &&
            idx.Properties.Any(p => p.Name == "Version"));
    }

    [Fact]
    public async Task Publish_Id_IsIdentityGenerated()
    {
        await using var db = new OcbBusinessDbContext(CreateInMemoryOptions());
        var entity = db.Model.FindEntityType(typeof(PublishEntity))!;
        var key = entity.FindPrimaryKey()!;

        Assert.Contains(key.Properties, p => p.ValueGenerated == ValueGenerated.OnAdd);
    }

    [Fact]
    public async Task BotRunQueue_Id_IsIdentityGenerated()
    {
        await using var db = new OcbBusinessDbContext(CreateInMemoryOptions());
        var entity = db.Model.FindEntityType(typeof(BotRunQueueEntity))!;
        var key = entity.FindPrimaryKey()!;

        Assert.Contains(key.Properties, p => p.ValueGenerated == ValueGenerated.OnAdd);
    }

    [Fact]
    public async Task BotRunQueue_HasStatus_And_Priority_Indexes()
    {
        await using var db = new OcbBusinessDbContext(CreateInMemoryOptions());
        var entity = db.Model.FindEntityType(typeof(BotRunQueueEntity))!;

        var indexes = entity.GetIndexes();
        Assert.Contains(indexes, idx =>
            idx.Properties.Any(p => p.Name == "Status"));
        Assert.Contains(indexes, idx =>
            idx.Properties.Any(p => p.Name == "Priority"));
    }

    [Fact]
    public async Task Can_Insert_And_Query_Tenant()
    {
        await using var db = new OcbBusinessDbContext(CreateInMemoryOptions());
        var tenant = new TenantEntity
        {
            TenantId = "t1",
            DisplayName = "Test Tenant",
            Status = "active",
            MaxBots = 5,
            MaxDevices = 10,
            QuotaPerMinute = 100,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        var found = await db.Tenants.FirstOrDefaultAsync(t => t.TenantId == "t1");
        Assert.NotNull(found);
        Assert.Equal("Test Tenant", found.DisplayName);
    }
}

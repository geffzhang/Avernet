using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Ocb.Contracts.Identity;
using Ocb.Contracts.Resources;
using Ocb.Contracts.Skills;

namespace Ocb.Contracts.Tests;

[SuppressMessage("Naming", "CA1707", Justification = "xUnit test naming convention uses underscores.")]
public sealed class BackendSkillsContractSerializationTests
{
    [Fact]
    public void SkillPublicationRecord_UsesStableSnakeCaseNames()
    {
        var dto = new SkillPublicationRecord(
            TenantId: "tenant-a",
            BotId: "bot-1",
            SkillId: "skill-7",
            Version: new SkillVersionRef("center://uuid-1", "3", SkillSourceScheme.Center),
            PackageSha256: "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            PublicationState: "published",
            ManifestContractVersion: "skills-pool-p3-v1");

        var json = JsonSerializer.Serialize(dto, OcbJsonContext.Default.SkillPublicationRecord);
        using var doc = JsonDocument.Parse(json);

        Assert.Equal("tenant-a", doc.RootElement.GetProperty("tenant_id").GetString());
        Assert.Equal("skills-pool-p3-v1", doc.RootElement.GetProperty("manifest_contract_version").GetString());
        Assert.Equal("center://uuid-1", doc.RootElement.GetProperty("version").GetProperty("source_locator").GetString());
    }

    [Fact]
    public void SkillVersionRef_UsesCorrectSchemeEnum()
    {
        var git = new SkillVersionRef("git://repo", "abc123", SkillSourceScheme.Git);
        var json = JsonSerializer.Serialize(git, OcbJsonContext.Default.SkillVersionRef);

        Assert.Contains("\"scheme\":0", json); // Git = 0

        var center = new SkillVersionRef("center://id", "v1", SkillSourceScheme.Center);
        var json2 = JsonSerializer.Serialize(center, OcbJsonContext.Default.SkillVersionRef);

        Assert.Contains("\"scheme\":2", json2); // Center = 2
    }

    [Fact]
    public void SessionAssetRecord_UsesSnakeCaseNames()
    {
        var dto = new SessionAssetRecord(
            TenantId: "t1",
            BotId: "b1",
            SessionId: "s1",
            ResourceId: "r1",
            ObjectKey: "tenant/t1/bots/b1/obj.bin",
            SizeBytes: 1024,
            Sha256: "abc123");

        var json = JsonSerializer.Serialize(dto, OcbJsonContext.Default.SessionAssetRecord);
        using var doc = JsonDocument.Parse(json);

        Assert.Equal("t1", doc.RootElement.GetProperty("tenant_id").GetString());
        Assert.Equal("b1", doc.RootElement.GetProperty("bot_id").GetString());
        Assert.Equal("s1", doc.RootElement.GetProperty("session_id").GetString());
        Assert.Equal("r1", doc.RootElement.GetProperty("resource_id").GetString());
        Assert.Equal(1024, doc.RootElement.GetProperty("size_bytes").GetInt64());
    }

    [Fact]
    public void CallerIdentityBinding_UsesSnakeCaseNames()
    {
        var dto = new CallerIdentityBinding(
            TenantId: "t1",
            BotId: "b1",
            SubjectId: "u1",
            Roles: new HashSet<string>(StringComparer.Ordinal) { "admin", "user" });

        var json = JsonSerializer.Serialize(dto, OcbJsonContext.Default.CallerIdentityBinding);
        using var doc = JsonDocument.Parse(json);

        Assert.Equal("t1", doc.RootElement.GetProperty("tenant_id").GetString());
        Assert.Equal("b1", doc.RootElement.GetProperty("bot_id").GetString());
        Assert.Equal("u1", doc.RootElement.GetProperty("subject_id").GetString());
    }

    [Fact]
    public void SkillActivationRequest_SerializesCorrectly()
    {
        var dto = new SkillActivationRequest(
            Caller: new CallerContext("t1", "u1", new HashSet<string>(StringComparer.Ordinal) { "user" }),
            BotId: "b1",
            SkillIds: ["skill-1", "skill-2"],
            ManifestContractVersion: "skills-pool-p3-v1");

        var json = JsonSerializer.Serialize(dto, OcbJsonContext.Default.SkillActivationRequest);
        using var doc = JsonDocument.Parse(json);

        Assert.Equal("b1", doc.RootElement.GetProperty("bot_id").GetString());
        Assert.Equal("skills-pool-p3-v1", doc.RootElement.GetProperty("manifest_contract_version").GetString());
    }
}

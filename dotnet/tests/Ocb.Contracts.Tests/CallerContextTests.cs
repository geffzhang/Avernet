using System.Text.Json;
using Ocb.Contracts;

namespace Ocb.Contracts.Tests;

public sealed class CallerContextTests
{
    private static readonly HashSet<string> ExpectedRoles = new(StringComparer.Ordinal)
    {
        "admin",
        "operator",
    };

    [Fact]
    public void CallerContextRoundTripsWithStableWireNames()
    {
        var caller = new CallerContext("tenant-1", "user-7", new HashSet<string> { "admin", "operator" });

        var json = JsonSerializer.Serialize(caller, OcbJsonContext.Default.CallerContext);
        var roundTrip = JsonSerializer.Deserialize(json, OcbJsonContext.Default.CallerContext);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("tenant-1", root.GetProperty("tenant_id").GetString());
        Assert.Equal("user-7", root.GetProperty("subject_id").GetString());
        Assert.Equal(JsonValueKind.Array, root.GetProperty("roles").ValueKind);
        var roleValues = root.GetProperty("roles").EnumerateArray().Select(static x => x.GetString()).ToHashSet(StringComparer.Ordinal);
        Assert.True(roleValues.SetEquals(ExpectedRoles));

        Assert.Equal(caller.TenantId, roundTrip!.TenantId);
        Assert.Equal(caller.SubjectId, roundTrip.SubjectId);
        Assert.True(roundTrip.Roles.SetEquals(caller.Roles));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void ConstructorRejectsBlankTenantId(string tenantId)
    {
        var roles = new HashSet<string> { "reader" };

        Assert.Throws<ArgumentException>(() => new CallerContext(tenantId, "subject-1", roles));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void ConstructorRejectsBlankSubjectId(string subjectId)
    {
        var roles = new HashSet<string> { "reader" };

        Assert.Throws<ArgumentException>(() => new CallerContext("tenant-1", subjectId, roles));
    }

    [Fact]
    public void ConstructorRejectsNullRoles()
    {
        Assert.Throws<ArgumentNullException>(() => new CallerContext("tenant-1", "subject-1", roles: null!));
    }

    [Theory]
    [InlineData("{\"subject_id\":\"user-7\",\"roles\":[\"admin\"]}")]
    [InlineData("{\"tenant_id\":\"\",\"subject_id\":\"user-7\",\"roles\":[\"admin\"]}")]
    [InlineData("{\"tenant_id\":\" \",\"subject_id\":\"user-7\",\"roles\":[\"admin\"]}")]
    public void DeserializeRejectsMissingOrBlankTenantId(string json)
    {
        var ex = Assert.ThrowsAny<Exception>(() => JsonSerializer.Deserialize(json, OcbJsonContext.Default.CallerContext));

        Assert.True(ContainsException<ArgumentException>(ex) || ContainsException<JsonException>(ex));
    }

    [Theory]
    [InlineData("", "bot-1")]
    [InlineData("tenant-1", "")]
    public void TenantEntityKeyRejectsMissingRequiredParts(string tenantId, string entityId)
    {
        Assert.Throws<ArgumentException>(() => TenantEntityKey.Create(tenantId, entityId));
    }

    [Fact]
    public void TenantEntityKeyEncodingIsStableAndCollisionSafe()
    {
        Assert.Equal("dGVuYW50LTE.Ym90LTE", TenantEntityKey.Create("tenant-1", "bot-1").ToString());
        Assert.NotEqual(
            TenantEntityKey.Create("a:b", "c").ToString(),
            TenantEntityKey.Create("a", "b:c").ToString());
    }

    private static bool ContainsException<TException>(Exception exception)
        where TException : Exception
    {
        for (var current = exception; current is not null; current = current.InnerException!)
        {
            if (current is TException)
            {
                return true;
            }
        }

        return false;
    }
}

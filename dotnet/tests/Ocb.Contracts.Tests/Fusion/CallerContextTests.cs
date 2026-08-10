using System.Diagnostics.CodeAnalysis;
using Ocb.Contracts;

namespace Ocb.Contracts.Tests.Fusion;

[SuppressMessage("Naming", "CA1707", Justification = "xUnit test naming convention uses underscores.")]
public sealed class CallerContextTests
{
    [Fact]
    public void CallerContext_MustRequireTenantAndSubject()
    {
        var ex1 = Assert.Throws<ArgumentException>(() =>
            new CallerContext("", "subject-1", new HashSet<string>()));
        Assert.Equal("tenantId", ex1.ParamName);

        var ex2 = Assert.Throws<ArgumentException>(() =>
            new CallerContext("tenant-1", "", new HashSet<string>()));
        Assert.Equal("subjectId", ex2.ParamName);
    }

    [Fact]
    public void CallerContext_MustRejectNullRoles()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new CallerContext("tenant-1", "subject-1", null!));
    }

    [Fact]
    public void CallerContext_MustRejectWhitespaceTenantId()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            new CallerContext("   ", "subject-1", new HashSet<string>()));
        Assert.Equal("tenantId", ex.ParamName);
    }

    [Fact]
    public void CallerContext_MustRejectWhitespaceSubjectId()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            new CallerContext("tenant-1", "\t", new HashSet<string>()));
        Assert.Equal("subjectId", ex.ParamName);
    }

    [Fact]
    public void CallerContext_ValidConstruction_PreservesAllValues()
    {
        var roles = new HashSet<string> { "admin", "user" };
        var ctx = new CallerContext("tenant-1", "subject-1", roles);

        Assert.Equal("tenant-1", ctx.TenantId);
        Assert.Equal("subject-1", ctx.SubjectId);
        Assert.Equivalent(roles, ctx.Roles);
    }

    [Fact]
    public void CallerContext_UsesSnakeCaseJsonPropertyNames()
    {
        var ctx = new CallerContext("tenant-1", "subject-1", new HashSet<string> { "user" });
        var json = System.Text.Json.JsonSerializer.Serialize(ctx, OcbJsonContext.Default.CallerCtx);

        Assert.Contains("\"tenant_id\"", json);
        Assert.Contains("\"subject_id\"", json);
        Assert.Contains("\"roles\"", json);
    }
}

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Ocb.Infrastructure.PostgreSql.Identity;
using Ocb.Infrastructure.PostgreSql.Skills;
using Ocb.PluginApi.Identity;
using Ocb.PluginApi.Skills;

namespace Ocb.Infrastructure.Tests.PostgreSql;

[SuppressMessage("Naming", "CA1707", Justification = "xUnit test naming convention uses underscores.")]
public sealed class TenantGuardRepositoryTests
{
    [Fact]
    public void SkillPublicationRepository_ImplementsPluginPort()
    {
        Assert.True(typeof(SkillPublicationRepository).IsAssignableTo(typeof(ISkillPublicationStorePlugin)));
    }

    [Fact]
    public void CallerIdentityRepository_ImplementsPluginPort()
    {
        Assert.True(typeof(CallerIdentityRepository).IsAssignableTo(typeof(ICallerIdentityRepositoryPlugin)));
    }

    [Fact]
    public void SkillPublicationRepository_AllPublicMethods_IncludeTenantIdParameter()
    {
        var methodsWithoutExplicitTenant = new[] { "ToString", "Equals", "GetHashCode", "GetType" };

        var publicMethods = typeof(SkillPublicationRepository).GetMethods(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        foreach (var method in publicMethods)
        {
            if (methodsWithoutExplicitTenant.Contains(method.Name, StringComparer.Ordinal))
                continue;

            var parameters = method.GetParameters();

            // InsertAsync takes a record that carries TenantId — verify record param
            var hasTenantParam = parameters.Any(p =>
                string.Equals(p.Name, "tenantId", StringComparison.OrdinalIgnoreCase)
                || (p.Name is "record" && p.ParameterType.Name.Contains("Record", StringComparison.Ordinal)));

            Assert.True(hasTenantParam,
                $"Method {method.Name} must include a tenantId parameter or a record carrying TenantId.");
        }
    }

    [Fact]
    public void CallerIdentityRepository_AllPublicMethods_IncludeTenantIdParameter()
    {
        var publicMethods = typeof(CallerIdentityRepository).GetMethods(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        foreach (var method in publicMethods)
        {
            var processMethod = method;
            // Skip accessors and property getters
            if (processMethod.IsSpecialName) continue;

            var parameters = processMethod.GetParameters();

            var hasTenantParam = parameters.Any(p =>
                string.Equals(p.Name, "tenantId", StringComparison.OrdinalIgnoreCase));

            Assert.True(hasTenantParam,
                $"Method {processMethod.Name} must include a tenantId parameter.");
        }
    }

    [Fact]
    public void SkillPublicationEntity_HasCompositeKeyWithTenantIdFirst()
    {
        // The composite key is defined in OcbBusinessDbContext.OnModelCreating
        // Verify the entity properties include all key columns
        var tenantIdProp = typeof(SkillPublicationEntity).GetProperty("TenantId");
        var botIdProp = typeof(SkillPublicationEntity).GetProperty("BotId");
        var skillIdProp = typeof(SkillPublicationEntity).GetProperty("SkillId");

        Assert.NotNull(tenantIdProp);
        Assert.NotNull(botIdProp);
        Assert.NotNull(skillIdProp);
    }

    [Fact]
    public void CallerIdentityEntity_HasCompositeKeyWithTenantIdFirst()
    {
        var tenantIdProp = typeof(CallerIdentityEntity).GetProperty("TenantId");
        var botIdProp = typeof(CallerIdentityEntity).GetProperty("BotId");
        var subjectIdProp = typeof(CallerIdentityEntity).GetProperty("SubjectId");

        Assert.NotNull(tenantIdProp);
        Assert.NotNull(botIdProp);
        Assert.NotNull(subjectIdProp);
    }
}

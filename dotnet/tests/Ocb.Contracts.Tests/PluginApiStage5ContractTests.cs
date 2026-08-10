using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Ocb.Contracts;
using Ocb.PluginApi;
using Ocb.PluginApi.Identity;
using Ocb.PluginApi.Skills;
using Ocb.PluginApi.Storage;

namespace Ocb.Contracts.Tests;

[SuppressMessage("Naming", "CA1707", Justification = "xUnit test naming convention uses underscores.")]
public sealed class PluginApiStage5ContractTests
{
    [Fact]
    public void RuntimeMaterialization_IsServiceApi_NotPluginApi()
    {
        // IRuntimeSkillMaterializationService is in Ocb.Contracts, not Ocb.PluginApi
        var serviceType = typeof(Ocb.Contracts.Skills.IRuntimeSkillMaterializationService);
        Assert.Equal(typeof(CallerContext).Assembly, serviceType.Assembly);

        var pluginTypes = typeof(IPluginContract).Assembly.GetTypes();
        Assert.DoesNotContain(pluginTypes,
            type => type.Name.Contains("RuntimeSkillMaterializer", StringComparison.Ordinal));
    }

    [Fact]
    public void AllStage5PluginPorts_ExtendIPluginContract()
    {
        Assert.True(typeof(IAssetObjectStoragePlugin).IsAssignableTo(typeof(IPluginContract)));
        Assert.True(typeof(ISkillPublicationStorePlugin).IsAssignableTo(typeof(IPluginContract)));
        Assert.True(typeof(ICallerIdentityRepositoryPlugin).IsAssignableTo(typeof(IPluginContract)));
    }

    [Fact]
    public void SkillPublicationStorePlugin_IncludesTenantScopedOperations()
    {
        var methods = typeof(ISkillPublicationStorePlugin).GetMethods();

        var getBySkill = methods.Single(m => m.Name == "GetBySkillAsync");
        Assert.Contains(getBySkill.GetParameters(), p => p.Name == "tenantId");

        var listByBot = methods.Single(m => m.Name == "ListByBotAsync");
        Assert.Contains(listByBot.GetParameters(), p => p.Name == "tenantId");

        var insert = methods.Single(m => m.Name == "InsertAsync");
        var recordParam = insert.GetParameters().Single(p => p.Name == "record");
        Assert.Equal(typeof(Ocb.Contracts.Skills.SkillPublicationRecord), recordParam.ParameterType);

        var updateState = methods.Single(m => m.Name == "UpdatePublicationStateAsync");
        Assert.Contains(updateState.GetParameters(), p => p.Name == "tenantId");
        Assert.Contains(updateState.GetParameters(), p => p.Name == "publicationState");
    }

    [Fact]
    public void CallerIdentityRepository_ReturnType_UsesContractsBinding()
    {
        var method = typeof(ICallerIdentityRepositoryPlugin).GetMethod("GetCallerIdentityAsync")!;

        Assert.Equal(
            typeof(Ocb.Contracts.Identity.CallerIdentityBinding),
            method.ReturnType.GenericTypeArguments[0]);
    }

    [Fact]
    public void PluginApiPorts_DoNotLeakOrmOrInfraDependencies()
    {
        var pluginTypes = new[]
        {
            typeof(IAssetObjectStoragePlugin),
            typeof(ISkillPublicationStorePlugin),
            typeof(ICallerIdentityRepositoryPlugin),
        };

        foreach (var type in pluginTypes)
        {
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance);
            foreach (var method in methods)
            {
                var sig = method.ToString();
                Assert.DoesNotContain("DbContext", sig, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("SqlCommand", sig, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Npgsql", sig, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("MinioClient", sig, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}

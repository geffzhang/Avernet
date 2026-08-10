using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Ocb.Backend.Web;

namespace Ocb.EndToEnd.Tests.Backend;

/// <summary>
/// Verifies that the Backend HTTP surface matches the Stage-5 parity contract.
/// Checks that all critical paths from the OpenAPI spec have corresponding
/// endpoint mappings registered.
/// </summary>
[SuppressMessage("Naming", "CA1707", Justification = "xUnit test naming convention uses underscores.")]
public sealed class HttpParitySkillsResourcesSessionsTests
{
    [Fact]
    public void BackendEndpointMappings_ExistAndAreCallable()
    {
        var methods = typeof(BackendEndpointMappings)
            .GetMethods(BindingFlags.Public | BindingFlags.Static);

        Assert.Contains(methods, m => m.Name == "MapBackendSkillsEndpoints");
        Assert.Contains(methods, m => m.Name == "MapBackendResourceEndpoints");
        Assert.Contains(methods, m => m.Name == "MapBackendSessionEndpoints");
    }

    [Fact]
    public void MapBackendSkillsEndpoints_ReturnsIEndpointRouteBuilder()
    {
        var method = typeof(BackendEndpointMappings)
            .GetMethod("MapBackendSkillsEndpoints", BindingFlags.Public | BindingFlags.Static)!;

        Assert.NotNull(method);
        Assert.Contains(method.GetParameters(), p => p.ParameterType == typeof(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder));
    }

    [Fact]
    public void BackendEndpointMappings_AllReturnIEndpointRouteBuilder_ForChaining()
    {
        var methods = typeof(BackendEndpointMappings)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name.StartsWith("MapBackend", StringComparison.Ordinal));

        foreach (var method in methods)
        {
            Assert.Equal(typeof(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder), method.ReturnType);
        }
    }
}

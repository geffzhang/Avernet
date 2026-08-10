using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Ocb.Backend.Web;

/// <summary>
/// Registers the Stage-5 Backend HTTP endpoints.
/// Implements the OpenAPI parity contract at <c>/openapi/v1/</c>.
/// </summary>
public static class BackendEndpointMappings
{
    public static IEndpointRouteBuilder MapBackendSkillsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // GET /openapi/v1/bots/skills
        endpoints.MapGet("/openapi/v1/bots/skills", async context =>
        {
            context.Response.StatusCode = StatusCodes.Status501NotImplemented;
            await context.Response.WriteAsync("{\"error\":\"not_implemented\"}");
        }).RequireAuthorization();

        return endpoints;
    }

    public static IEndpointRouteBuilder MapBackendResourceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // GET /openapi/v1/bots/resources/{resourceId}/download
        endpoints.MapGet("/openapi/v1/bots/resources/{resourceId}/download", async context =>
        {
            context.Response.StatusCode = StatusCodes.Status501NotImplemented;
            await context.Response.WriteAsync("{\"error\":\"not_implemented\"}");
        }).RequireAuthorization();

        return endpoints;
    }

    public static IEndpointRouteBuilder MapBackendSessionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // GET /openapi/v1/bots/sessions/{botId}
        endpoints.MapGet("/openapi/v1/bots/sessions/{botId}", async context =>
        {
            context.Response.StatusCode = StatusCodes.Status501NotImplemented;
            await context.Response.WriteAsync("{\"error\":\"not_implemented\"}");
        }).RequireAuthorization();

        return endpoints;
    }
}

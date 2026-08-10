using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Ocb.Contracts;
using Ocb.Contracts.Fusion;
using Ocb.Fusion.Application;

namespace Ocb.Fusion.Routes;

/// <summary>
/// Fusion HTTP routes including POST /api/v1/groups/{group_id}/fuse.
/// </summary>
public static class FusionRoutes
{
    private static readonly Regex GroupIdPattern = new(
        "^grp-[A-Za-z0-9_-]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    /// <summary>
    /// Map all fusion routes onto the application builder.
    /// </summary>
    public static void MapFusionRoutes(this WebApplication app)
    {
        app.MapPost("/api/v1/groups/{group_id}/fuse", HandleFuseAsync);
    }

    internal static async Task<IResult> HandleFuseAsync(
        [FromRoute(Name = "group_id")] string groupId,
        FusionRequestDto body,
        HttpContext http,
        FusionService service,
        CancellationToken ct)
    {
        if (!GroupIdPattern.IsMatch(groupId))
            return Results.BadRequest(new { error = new { code = "INVALID_GROUP_ID" } });

        var caller = CallerContextFactory.FromHttp(http);

        var result = await service.FuseAsync(groupId, body, caller, ct);
        return Results.Ok(result);
    }
}

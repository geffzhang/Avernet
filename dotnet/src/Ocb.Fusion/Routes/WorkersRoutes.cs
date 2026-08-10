using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Ocb.Contracts.Fusion;
using Ocb.Fusion.Application;

namespace Ocb.Fusion.Routes;

/// <summary>
/// Worker and health routes aligned with bcsfuse OpenAPI parity.
/// </summary>
public static class WorkersRoutes
{
    public static void MapWorkersRoutes(this WebApplication app)
    {
        app.MapPost("/v1/workers", HandleCreateWorkerAsync);
        app.MapGet("/v1/workers", HandleListWorkersAsync);
        app.MapGet("/v1/workers/{workerId}", HandleGetWorkerAsync);
        app.MapPatch("/v1/workers/{workerId}", HandleUpdateWorkerAsync);
        app.MapGet("/health", HandleHealthAsync);
    }

    internal static async Task<IResult> HandleCreateWorkerAsync(
        CreateWorkerRequest body,
        WorkerProfileService svc,
        CancellationToken ct)
    {
        var result = await svc.CreateWorkerAsync(body, ct);
        return Results.Created($"/v1/workers/{result.WorkerId}", result);
    }

    internal static async Task<IResult> HandleListWorkersAsync(
        WorkerProfileService svc,
        CancellationToken ct)
    {
        var result = await svc.ListWorkersAsync(ct);
        return Results.Ok(result);
    }

    internal static async Task<IResult> HandleGetWorkerAsync(
        [FromRoute] string workerId,
        WorkerProfileService svc,
        CancellationToken ct)
    {
        try
        {
            var result = await svc.GetWorkerAsync(workerId, ct);
            return Results.Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound(new { error = new { code = "WORKER_NOT_FOUND" } });
        }
    }

    internal static async Task<IResult> HandleUpdateWorkerAsync(
        [FromRoute] string workerId,
        UpdateWorkerRequest body,
        WorkerProfileService svc,
        CancellationToken ct)
    {
        try
        {
            var result = await svc.UpdateWorkerAsync(workerId, body, ct);
            return Results.Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound(new { error = new { code = "WORKER_NOT_FOUND" } });
        }
    }

    internal static IResult HandleHealthAsync()
    {
        return Results.Ok(new { status = "ok" });
    }
}

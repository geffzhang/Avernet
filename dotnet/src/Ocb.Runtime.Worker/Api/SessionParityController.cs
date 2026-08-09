using System.Net;
using Microsoft.AspNetCore.Http;
using Ocb.Contracts.Sessions;
using Ocb.Runtime.Worker.Application;
using Ocb.Runtime.Worker.Infra.Clients.Models;

namespace Ocb.Runtime.Worker.Api;

/// <summary>
/// Engine Session HTTP API — parity with the engine OpenAPI baseline.
/// All paths match the parity-corpus contract exactly.
/// </summary>
public static class SessionParityController
{
    public static void MapSessionRoutes(WebApplication app)
    {
        var group = app.MapGroup("/api");

        group.MapGet("/sessions", ListSessionsAsync);
        group.MapGet("/sessions/{sessionId}", GetSessionAsync);
        group.MapDelete("/sessions/{sessionId}", DeleteSessionAsync);
        group.MapGet("/sessions/{sessionId}/messages", ListMessagesAsync);
        group.MapDelete("/sessions/{sessionId}/messages", DeleteMessagesAsync);
        group.MapPost("/sessions/{sessionId}/update", UpdateSessionAsync);
    }

    private static async Task<IResult> ListSessionsAsync(
        IEngineSessionPort sessionPort,
        string? tenantId, string? status, int? limit, int? offset,
        CancellationToken ct)
    {
        try
        {
            var query = new SessionListQuery(tenantId, status, limit, offset);
            var sessions = await sessionPort.ListSessionsAsync(query, ct);
            return Results.Ok(new { sessions });
        }
        catch (ContractMappingException ex)
        {
            return Results.Json(
                new { error = ex.Code, message = ex.Message },
                statusCode: (int)HttpStatusCode.BadGateway);
        }
    }

    private static async Task<IResult> GetSessionAsync(
        IEngineSessionPort sessionPort,
        string sessionId,
        CancellationToken ct)
    {
        try
        {
            var session = await sessionPort.GetSessionAsync(sessionId, ct);
            return session is null
                ? Results.Json(new { error = "NOT_FOUND", message = $"Session '{sessionId}' not found" },
                    statusCode: (int)HttpStatusCode.NotFound)
                : Results.Ok(session);
        }
        catch (ContractMappingException ex)
        {
            return Results.Json(
                new { error = ex.Code, message = ex.Message },
                statusCode: (int)HttpStatusCode.BadGateway);
        }
    }

    private static async Task<IResult> DeleteSessionAsync(
        IEngineSessionPort sessionPort,
        string sessionId,
        CancellationToken ct)
    {
        try
        {
            await sessionPort.DeleteSessionAsync(sessionId, ct);
            return Results.Ok(new { deleted = true, session_id = sessionId });
        }
        catch (ContractMappingException ex)
        {
            return Results.Json(
                new { error = ex.Code, message = ex.Message },
                statusCode: (int)HttpStatusCode.BadGateway);
        }
    }

    private static async Task<IResult> ListMessagesAsync(
        IEngineSessionPort sessionPort,
        string sessionId,
        CancellationToken ct)
    {
        try
        {
            var messages = await sessionPort.ListMessagesAsync(sessionId, ct);
            return Results.Ok(new { messages });
        }
        catch (ContractMappingException ex)
        {
            return Results.Json(
                new { error = ex.Code, message = ex.Message },
                statusCode: (int)HttpStatusCode.BadGateway);
        }
    }

    private static async Task<IResult> DeleteMessagesAsync(
        IEngineSessionPort sessionPort,
        string sessionId,
        CancellationToken ct)
    {
        try
        {
            await sessionPort.DeleteSessionAsync(sessionId, ct);
            return Results.Ok(new { cleared = true, session_id = sessionId });
        }
        catch (ContractMappingException ex)
        {
            return Results.Json(
                new { error = ex.Code, message = ex.Message },
                statusCode: (int)HttpStatusCode.BadGateway);
        }
    }

    private static async Task<IResult> UpdateSessionAsync(
        IEngineSessionPort sessionPort,
        string sessionId,
        SessionUpdateRequest update,
        CancellationToken ct)
    {
        try
        {
            var result = await sessionPort.UpdateSessionAsync(sessionId, update, ct);
            return result is null
                ? Results.Json(new { error = "NOT_FOUND", message = $"Session '{sessionId}' not found" },
                    statusCode: (int)HttpStatusCode.NotFound)
                : Results.Ok(result);
        }
        catch (ContractMappingException ex)
        {
            return Results.Json(
                new { error = ex.Code, message = ex.Message },
                statusCode: (int)HttpStatusCode.BadGateway);
        }
    }
}

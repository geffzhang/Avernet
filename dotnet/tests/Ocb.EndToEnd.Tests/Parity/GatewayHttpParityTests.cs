using System.Text.Json;

namespace Ocb.EndToEnd.Tests.Parity;

/// <summary>
/// HTTP parity: verify that the .NET Gateway OpenAPI surface
/// includes the same paths as the Python baseline corpus.
/// </summary>
public sealed class GatewayHttpParityTests
{
    /// <summary>
    /// The collaboration WebSocket path must appear in the served
    /// OpenAPI document — it is the primary real-time channel.
    /// </summary>
    [Fact]
    public async Task HttpParityIncludesCollaborationWsPathInServedOpenApi()
    {
        using var doc = await ParityCorpusLoader.LoadGatewayOpenApiAsync();

        var paths = doc.RootElement.GetProperty("paths");

        Assert.True(
            paths.TryGetProperty("/openapi/v1/collaboration/messages/ws", out _),
            "Gateway OpenAPI must include the collaboration WebSocket path.");
    }

    /// <summary>
    /// The chat stream path must appear — it is the SSE endpoint used
    /// by real-time AI chat consumers.
    /// </summary>
    [Fact]
    public async Task HttpParityIncludesChatStreamPathInServedOpenApi()
    {
        using var doc = await ParityCorpusLoader.LoadGatewayOpenApiAsync();

        var paths = doc.RootElement.GetProperty("paths");

        Assert.True(
            paths.TryGetProperty("/openapi/v1/chat/messages/stream", out _),
            "Gateway OpenAPI must include the chat SSE stream path.");
    }

    /// <summary>
    /// Core bot management paths must be present — these are the most
    /// frequently used REST endpoints.
    /// </summary>
    [Theory]
    [InlineData("/openapi/v1/bots")]
    [InlineData("/openapi/v1/chat/messages")]
    [InlineData("/openapi/v1/collaboration/sessions/{session_id}/messages")]
    public async Task HttpParityIncludesCorePaths(string expectedPath)
    {
        using var doc = await ParityCorpusLoader.LoadGatewayOpenApiAsync();

        var paths = doc.RootElement.GetProperty("paths");

        Assert.True(
            paths.TryGetProperty(expectedPath, out _),
            $"Gateway OpenAPI must include: {expectedPath}");
    }

    /// <summary>
    /// All paths in the gateway OpenAPI must have at least one HTTP method
    /// defined (not an empty path entry).
    /// </summary>
    [Fact]
    public async Task EveryPathHasAtLeastOneHttpMethod()
    {
        using var doc = await ParityCorpusLoader.LoadGatewayOpenApiAsync();

        var paths = doc.RootElement.GetProperty("paths");
        var emptyPaths = new List<string>();

        foreach (var path in paths.EnumerateObject())
        {
            var methods = path.Value.EnumerateObject().ToList();
            if (methods.Count == 0)
                emptyPaths.Add(path.Name);
        }

        Assert.Empty(emptyPaths);
    }
}

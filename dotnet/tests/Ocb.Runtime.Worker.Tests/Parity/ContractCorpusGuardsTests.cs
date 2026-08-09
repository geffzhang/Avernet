using System.Diagnostics.CodeAnalysis;
using Ocb.Runtime.Worker.Application.Assignment;

namespace Ocb.Runtime.Worker.Tests.Parity;

/// <summary>
/// Guards that ensure the internal reassign mechanism is never
/// exposed as a public API endpoint in the gateway or engine OpenAPI specs.
/// </summary>
[SuppressMessage("Naming", "CA1707", Justification = "xUnit test naming convention uses underscores.")]
public sealed class ContractCorpusGuardsTests
{
    private static readonly string CorpusDir = Path.Combine(
        RepositoryRoot(), "dotnet", "contracts", "parity-corpus");

    /// <summary>
    /// The <c>reassign</c> operation exists only on the internal
    /// coordinator interface — it must never appear in the public OpenAPI.
    /// </summary>
    [Fact]
    public void InternalCoordinator_ExposesReassignMethod()
    {
        var method = typeof(IRuntimeAssignmentCoordinator)
            .GetMethod("RequestInternalReassignAsync");

        Assert.NotNull(method);
        Assert.Equal(typeof(Task<Ocb.GrainContracts.Runtime.ReassignResult>), method!.ReturnType);
    }

    /// <summary>
    /// The gateway OpenAPI must not contain "reassign" — it is internal only.
    /// </summary>
    [Fact]
    public async Task GatewayOpenApi_MustNotContainReassign()
    {
        var openApiPath = Path.Combine(CorpusDir, "gateway.openapi.json");
        if (!File.Exists(openApiPath))
            return; // Corpus not present — skip.

        var content = await File.ReadAllTextAsync(openApiPath);

        Assert.False(
            content.Contains("reassign", StringComparison.OrdinalIgnoreCase),
            "gateway.openapi.json must never expose reassign (internal capability).");
    }

    /// <summary>
    /// The engine OpenAPI must not contain "reassign" — it is internal only.
    /// </summary>
    [Fact]
    public async Task EngineOpenApi_MustNotContainReassign()
    {
        var openApiPath = Path.Combine(CorpusDir, "engine.openapi.json");
        if (!File.Exists(openApiPath))
            return;

        var content = await File.ReadAllTextAsync(openApiPath);

        Assert.False(
            content.Contains("reassign", StringComparison.OrdinalIgnoreCase),
            "engine.openapi.json must never expose reassign (internal capability).");
    }

    /// <summary>
    /// The engine OpenAPI must include the session API paths that the
    /// Runtime Worker must serve.
    /// </summary>
    [Theory]
    [InlineData("/api/sessions")]
    [InlineData("/api/sessions/{session_id}")]
    [InlineData("/api/sessions/{session_id}/messages")]
    [InlineData("/api/session-files")]
    public async Task EngineOpenApi_IncludesSessionPaths(string expectedPath)
    {
        var openApiPath = Path.Combine(CorpusDir, "engine.openapi.json");
        if (!File.Exists(openApiPath))
            return;

        var content = await File.ReadAllTextAsync(openApiPath);

        Assert.True(
            content.Contains($"\"{expectedPath}\"", StringComparison.Ordinal),
            $"engine.openapi.json must include path: {expectedPath}");
    }

    private static string RepositoryRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, ".git")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException(
            "Cannot locate repository root from " + AppContext.BaseDirectory);
    }
}

using System.Text.Json;

namespace Ocb.EndToEnd.Tests.Parity;

/// <summary>
/// Loads artifacts from the parity-corpus directory so that tests
/// can validate that the .NET Gateway surface matches the established
/// Python Gateway contract.
/// </summary>
public static class ParityCorpusLoader
{
    private static readonly string CorpusDir = Path.Combine(
        RepositoryRoot(),
        "dotnet", "contracts", "parity-corpus");

    /// <summary>
    /// Load the gateway OpenAPI document from the parity corpus.
    /// </summary>
    public static async Task<JsonDocument> LoadGatewayOpenApiAsync()
    {
        var manifestPath = Path.Combine(CorpusDir, "manifest.json");
        using var manifest = JsonDocument.Parse(
            await File.ReadAllTextAsync(manifestPath));

        var gatewayFile = manifest.RootElement
            .GetProperty("artifacts")
            .EnumerateArray()
            .First(a =>
                a.GetProperty("service").GetString() == "gateway" &&
                a.GetProperty("kind").GetString() == "openapi")
            .GetProperty("file").GetString()!;

        var openApiPath = Path.Combine(CorpusDir, gatewayFile);
        return JsonDocument.Parse(await File.ReadAllTextAsync(openApiPath));
    }

    /// <summary>
    /// Walk up from the test assembly location to find the repository root
    /// (marked by the presence of .git).
    /// </summary>
    private static string RepositoryRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, ".git")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("Cannot locate repository root from " + AppContext.BaseDirectory);
    }
}

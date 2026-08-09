using System.Text.Json;

namespace Ocb.Gateway.Tests.OpenApi;

/// <summary>
/// Loads per-domain OpenAPI documents from the parity corpus directory
/// (<c>dotnet/contracts/parity-corpus</c>).
/// </summary>
public static class TestOpenApiInputs
{
    /// <summary>
    /// Read <c>manifest.json</c> and load every OpenAPI artifact into a
    /// domain→JsonDocument dictionary that can be merged by
    /// <see cref="Ocb.Gateway.OpenApi.ServedOpenApiBuilder"/>.
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, JsonDocument>> LoadFromManifestAsync(string manifestPath)
    {
        var directory = Path.GetDirectoryName(manifestPath)
            ?? throw new InvalidOperationException($"Invalid manifest path: {manifestPath}");

        var manifestJson = await File.ReadAllTextAsync(manifestPath);
        using var manifest = JsonDocument.Parse(manifestJson);

        var result = new Dictionary<string, JsonDocument>(StringComparer.Ordinal);

        foreach (var artifact in manifest.RootElement.GetProperty("artifacts").EnumerateArray())
        {
            var kind = artifact.GetProperty("kind").GetString();
            if (kind != "openapi") continue;

            var service = artifact.GetProperty("service").GetString()!;
            var file = artifact.GetProperty("file").GetString()!;
            var filePath = Path.Combine(directory, file);

            if (!File.Exists(filePath)) continue;

            // Skip non-JSON artifacts (e.g. YAML files, markdown protocol docs).
            if (!file.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                continue;

            var json = await File.ReadAllTextAsync(filePath);
            result[service] = JsonDocument.Parse(json);
        }

        return result;
    }
}

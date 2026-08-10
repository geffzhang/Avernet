using System.Text.RegularExpressions;

namespace Ocb.Fusion.OpenApi;

/// <summary>
/// Validates that the .NET fusion route surface covers the paths declared
/// in the bcsfuse OpenAPI baseline.
/// </summary>
public static partial class BcsfuseOpenApiParityValidator
{
    [GeneratedRegex(@"^\s+(/\S+):\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex OpenApiPathPattern();

    /// <summary>
    /// YAML-simple parser that extracts path: lines from the OpenAPI spec.
    /// For production use, replace with a full OpenAPI parsing library.
    /// </summary>
    public static IReadOnlyList<string> ReadBaselinePaths(string openApiFilePath)
    {
        if (!File.Exists(openApiFilePath))
            throw new FileNotFoundException($"OpenAPI baseline not found: {openApiFilePath}");

        var yaml = File.ReadAllText(openApiFilePath);
        var matches = OpenApiPathPattern().Matches(yaml);

        var paths = new List<string>();
        foreach (Match match in matches)
        {
            paths.Add(match.Groups[1].Value);
        }

        return paths;
    }

    /// <summary>
    /// Known bcsfuse path set reference — the minimum required paths
    /// that must be implemented in the .NET fusion layer.
    /// </summary>
    public static readonly IReadOnlySet<string> RequiredBcsfusePaths = new HashSet<string>(StringComparer.Ordinal)
    {
        "/v1/workers",
        "/v1/workers/{worker_id}",
        "/api/v1/groups/{group_id}/fuse",
        "/health",
    };

    /// <summary>
    /// Compare the baseline paths against the required set and return
    /// any missing paths.
    /// </summary>
    public static OpenApiParityDiffResult Compare(IReadOnlyList<string> baselinePaths)
    {
        var missing = RequiredBcsfusePaths
            .Where(required => !baselinePaths.Contains(required, StringComparer.Ordinal))
            .ToList();

        return new OpenApiParityDiffResult(
            RequiredPaths: RequiredBcsfusePaths,
            BaselinePaths: baselinePaths,
            MissingPaths: missing
        );
    }
}

/// <summary>
/// Result of an OpenAPI parity comparison.
/// </summary>
/// <param name="RequiredPaths">The paths that must be covered.</param>
/// <param name="BaselinePaths">The paths found in the OpenAPI baseline.</param>
/// <param name="MissingPaths">Paths in the required set but not in the baseline.</param>
public sealed record OpenApiParityDiffResult(
    IReadOnlySet<string> RequiredPaths,
    IReadOnlyList<string> BaselinePaths,
    IReadOnlyList<string> MissingPaths
);

using System.Text.Json;
using Ocb.Gateway.OpenApi;

namespace Ocb.Gateway.Tests.OpenApi;

public sealed class ServedOpenApiBuilderTests
{
    [Fact]
    public async Task ServedOpenApiContainsParityPaths()
    {
        // Resolve parity-corpus relative to the solution root.
        var manifestPath = FindManifestPath();

        var builder = new ServedOpenApiBuilder();
        var docs = await TestOpenApiInputs.LoadFromManifestAsync(manifestPath);

        using var served = builder.Build("gateway", "v1", docs);
        var paths = served.RootElement.GetProperty("paths");

        Assert.True(paths.TryGetProperty("/openapi/v1/collaboration/messages/ws", out _),
            "Expected /openapi/v1/collaboration/messages/ws path");
        Assert.True(paths.TryGetProperty("/openapi/v1/bots", out _),
            "Expected /openapi/v1/bots path");
    }

    [Fact]
    public void EmptyDocsProducesValidOpenApiDocument()
    {
        var builder = new ServedOpenApiBuilder();
        var empty = new Dictionary<string, JsonDocument>();

        using var served = builder.Build("test", "1.0", empty);

        Assert.Equal("3.1.0", served.RootElement.GetProperty("openapi").GetString());
        Assert.Equal("test", served.RootElement.GetProperty("info").GetProperty("title").GetString());
        Assert.Equal("1.0", served.RootElement.GetProperty("info").GetProperty("version").GetString());
    }

    [Fact]
    public void SingleDomainPathsArePreserved()
    {
        var domainDoc = CreateMinimalDoc(new Dictionary<string, string>
        {
            ["/api/test"] = "get",
            ["/api/items"] = "post"
        });

        var builder = new ServedOpenApiBuilder();
        var docs = new Dictionary<string, JsonDocument>(StringComparer.Ordinal)
        {
            ["test-domain"] = domainDoc
        };

        using var served = builder.Build("merged", "1.0", docs);
        var paths = served.RootElement.GetProperty("paths");

        Assert.True(paths.TryGetProperty("/api/test", out _));
        Assert.True(paths.TryGetProperty("/api/items", out _));
    }

    [Fact]
    public void MultipleDomainPathsAreMerged()
    {
        var docA = CreateMinimalDoc(new Dictionary<string, string> { ["/api/a"] = "get" });
        var docB = CreateMinimalDoc(new Dictionary<string, string> { ["/api/b"] = "post" });

        var builder = new ServedOpenApiBuilder();
        var docs = new Dictionary<string, JsonDocument>(StringComparer.Ordinal)
        {
            ["a"] = docA,
            ["b"] = docB
        };

        using var served = builder.Build("merged", "1.0", docs);
        var paths = served.RootElement.GetProperty("paths");

        Assert.True(paths.TryGetProperty("/api/a", out _));
        Assert.True(paths.TryGetProperty("/api/b", out _));
    }

    private static JsonDocument CreateMinimalDoc(IReadOnlyDictionary<string, string> paths)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        writer.WriteStartObject();
        writer.WriteString("openapi", "3.1.0");
        writer.WriteStartObject("info");
        writer.WriteString("title", "test");
        writer.WriteString("version", "1.0");
        writer.WriteEndObject();
        writer.WriteStartObject("paths");
        foreach (var (path, method) in paths)
        {
            writer.WriteStartObject(path);
            writer.WriteStartObject(method);
            writer.WriteStartObject("responses");
            writer.WriteStartObject("200");
            writer.WriteString("description", "ok");
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.Flush();

        stream.Position = 0;
        return JsonDocument.Parse(stream);
    }

    private static string FindManifestPath()
    {
        // Walk up from the test assembly location to find the repo root.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "dotnet", "contracts", "parity-corpus", "manifest.json");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            "Cannot locate dotnet/contracts/parity-corpus/manifest.json. " +
            "Ensure tests are run from the repository root.");
    }
}

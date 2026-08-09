using System.Text.Json;

namespace Ocb.Gateway.OpenApi;

/// <summary>
/// Merges per-domain OpenAPI documents into a single "served" OpenAPI 3.1
/// document that reflects the gateway's externally-visible surface.
/// </summary>
public sealed class ServedOpenApiBuilder
{
    /// <summary>
    /// Build a merged OpenAPI document.
    /// </summary>
    /// <param name="title">API title for the merged document.</param>
    /// <param name="version">API version.</param>
    /// <param name="domainDocs">Per-domain OpenAPI documents keyed by domain name.</param>
    public JsonDocument Build(string title, string version, IReadOnlyDictionary<string, JsonDocument> domainDocs)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        writer.WriteStartObject();                                          // root
        writer.WriteString("openapi", "3.1.0");

        writer.WriteStartObject("info");                                    // info
        writer.WriteString("title", title);
        writer.WriteString("version", version);
        writer.WriteEndObject();                                           // /info

        writer.WriteStartObject("paths");                                   // paths
        foreach (var doc in domainDocs.Values)
        {
            if (!doc.RootElement.TryGetProperty("paths", out var paths))
                continue;

            foreach (var path in paths.EnumerateObject())
            {
                path.WriteTo(writer);
            }
        }
        writer.WriteEndObject();                                           // /paths

        writer.WriteEndObject();                                           // /root
        writer.Flush();

        stream.Position = 0;
        return JsonDocument.Parse(stream);
    }
}

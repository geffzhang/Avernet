using System.Text.Json;
using Ocb.Contracts;

namespace Ocb.PluginApi;

/// <summary>
/// Plugin contract for loading a domain's OpenAPI schema document.
/// Each downstream service (bcs, bots, bcsfuse, engine) provides its own
/// schema; the gateway merges them into a single served OpenAPI document.
/// </summary>
public interface ISchemaCatalog : IPluginContract
{
    /// <summary>Return the full OpenAPI JSON document for the named domain.</summary>
    ValueTask<JsonDocument> GetCurrentAsync(string domain, CancellationToken cancellationToken);
}

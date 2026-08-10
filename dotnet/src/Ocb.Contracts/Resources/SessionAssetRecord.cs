using System.Text.Json.Serialization;

namespace Ocb.Contracts.Resources;

/// <summary>
/// Record of a session asset stored in object storage.
/// </summary>
public sealed record SessionAssetRecord(
    [property: JsonPropertyName("tenant_id")] string TenantId,
    [property: JsonPropertyName("bot_id")] string BotId,
    [property: JsonPropertyName("session_id")] string SessionId,
    [property: JsonPropertyName("resource_id")] string ResourceId,
    [property: JsonPropertyName("object_key")] string ObjectKey,
    [property: JsonPropertyName("size_bytes")] long SizeBytes,
    [property: JsonPropertyName("sha256")] string Sha256
);

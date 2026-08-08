using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ocb.Contracts;

public sealed record CallerContext
{
    [JsonConstructor]
    public CallerContext(string tenantId, string subjectId, IReadOnlySet<string> roles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);
        ArgumentNullException.ThrowIfNull(roles);

        TenantId = tenantId;
        SubjectId = subjectId;
        Roles = roles;
    }

    [JsonPropertyName("tenant_id")]
    public string TenantId { get; init; }

    [JsonPropertyName("subject_id")]
    public string SubjectId { get; init; }

    [JsonPropertyName("roles")]
    [JsonConverter(typeof(ReadOnlyStringSetJsonConverter))]
    public IReadOnlySet<string> Roles { get; init; }
}

internal sealed class ReadOnlyStringSetJsonConverter : JsonConverter<IReadOnlySet<string>>
{
    public override IReadOnlySet<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return JsonSerializer.Deserialize(ref reader, OcbJsonContext.Default.HashSetString)!;
    }

    public override void Write(Utf8JsonWriter writer, IReadOnlySet<string> value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);

        var serializable = value as HashSet<string> ?? new HashSet<string>(value, StringComparer.Ordinal);
        JsonSerializer.Serialize(writer, serializable, OcbJsonContext.Default.HashSetString);
    }
}

using System.Text.Json.Serialization;

namespace Ocb.Contracts;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(CallerContext))]
[JsonSerializable(typeof(DomainError))]
[JsonSerializable(typeof(HashSet<string>))]
public partial class OcbJsonContext : JsonSerializerContext;

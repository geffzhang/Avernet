using System.Text.Json.Serialization;
using Ocb.Contracts.Fusion;

namespace Ocb.Contracts;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(CallerContext))]
[JsonSerializable(typeof(DomainError))]
[JsonSerializable(typeof(HashSet<string>))]
[JsonSerializable(typeof(FusionRequestDto))]
[JsonSerializable(typeof(FuseResponseDto))]
public partial class OcbJsonContext : JsonSerializerContext;

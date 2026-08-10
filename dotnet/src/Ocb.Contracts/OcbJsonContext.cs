using System.Text.Json.Serialization;
using Ocb.Contracts.Fusion;
using Ocb.Contracts.Identity;
using Ocb.Contracts.Resources;
using Ocb.Contracts.Skills;

namespace Ocb.Contracts;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(CallerContext), TypeInfoPropertyName = "CallerCtx")]
[JsonSerializable(typeof(DomainError))]
[JsonSerializable(typeof(HashSet<string>))]
[JsonSerializable(typeof(FusionRequestDto))]
[JsonSerializable(typeof(FuseResponseDto))]
[JsonSerializable(typeof(CallerIdentityBinding))]
[JsonSerializable(typeof(SessionAssetRecord))]
[JsonSerializable(typeof(SkillActivationRequest))]
[JsonSerializable(typeof(SkillPublicationRecord))]
[JsonSerializable(typeof(SkillVersionRef))]
public partial class OcbJsonContext : JsonSerializerContext;

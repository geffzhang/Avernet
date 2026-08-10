using System.Text.Json.Serialization;

namespace Ocb.Contracts.Skills;

/// <summary>
/// Immutable reference to a specific skill version.
/// </summary>
public sealed record SkillVersionRef(
    [property: JsonPropertyName("source_locator")] string SourceLocator,
    [property: JsonPropertyName("immutable_version")] string ImmutableVersion,
    [property: JsonPropertyName("scheme")] SkillSourceScheme Scheme
);

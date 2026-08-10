namespace Ocb.Contracts.Skills;

/// <summary>
/// Scheme identifying the source of a skill version.
/// </summary>
public enum SkillSourceScheme
{
    /// <summary>Git repository source.</summary>
    Git,

    /// <summary>Local file system source.</summary>
    Local,

    /// <summary>Skill center registry source.</summary>
    Center,
}

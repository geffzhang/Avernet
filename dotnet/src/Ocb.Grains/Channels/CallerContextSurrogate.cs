using Ocb.Contracts;

namespace Ocb.Grains.Channels;

/// <summary>
/// Surrogate for <see cref="CallerContext"/> that enables Orleans
/// serialization without adding Orleans dependencies to
/// <c>Ocb.Contracts</c>.
/// </summary>
[GenerateSerializer]
internal struct CallerContextSurrogate
{
    [Id(0)]
    public string TenantId;

    [Id(1)]
    public string SubjectId;

    [Id(2)]
    public HashSet<string> Roles;
}

/// <summary>
/// Converts between <see cref="CallerContext"/> and its Orleans
/// serialization surrogate.
/// </summary>
[RegisterConverter]
internal sealed class CallerContextConverter : IConverter<CallerContext, CallerContextSurrogate>
{
    public CallerContext ConvertFromSurrogate(in CallerContextSurrogate surrogate)
    {
        return new CallerContext(surrogate.TenantId, surrogate.SubjectId, surrogate.Roles);
    }

    public CallerContextSurrogate ConvertToSurrogate(in CallerContext value)
    {
        return new CallerContextSurrogate
        {
            TenantId = value.TenantId,
            SubjectId = value.SubjectId,
            Roles = new HashSet<string>(value.Roles, StringComparer.Ordinal)
        };
    }
}

using System.Text;

namespace Ocb.Contracts;

public readonly record struct TenantEntityKey(string TenantId, string EntityId)
{
    public static TenantEntityKey Create(string tenantId, string entityId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);
        return new TenantEntityKey(tenantId, entityId);
    }

    public override string ToString() => $"{Encode(TenantId)}.{Encode(EntityId)}";

    private static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');
}

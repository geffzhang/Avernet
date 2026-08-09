namespace Ocb.Runtime.Worker.Infra.Clients.Models;

/// <summary>
/// Thrown when an upstream response payload does not match the
/// expected contract shape — fail-closed over guessing unknown fields.
/// </summary>
public sealed class ContractMappingException : Exception
{
    public string Code { get; }

    public ContractMappingException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public ContractMappingException(string code, string message, Exception inner)
        : base(message, inner)
    {
        Code = code;
    }
}

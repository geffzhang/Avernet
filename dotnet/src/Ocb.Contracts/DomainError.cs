namespace Ocb.Contracts;

public sealed record DomainError(string Code, string Message, bool Retryable = false);

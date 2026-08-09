namespace Ocb.PluginApi;

/// <summary>
/// Verifies a bearer JWT together with a signed X-Avernet-Principal header
/// and produces a <see cref="Ocb.Contracts.CallerContext"/>.
/// Full contract with <c>VerifyAsync</c> is defined in Gateway Task 2.
/// </summary>
public interface IPrincipalTokenVerifier : IPluginContract;

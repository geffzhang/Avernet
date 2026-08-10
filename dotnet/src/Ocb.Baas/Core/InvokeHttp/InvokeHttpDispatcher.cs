using Ocb.Contracts;
using Ocb.Contracts.Baas.Device;
using Ocb.PluginApi.Baas.Sandbox;

namespace Ocb.Baas.Core.InvokeHttp;

/// <summary>
/// Dispatches transparent HTTP invocations to bot device sandboxes,
/// stripping hop-by-hop headers and mapping errors.
/// </summary>
public sealed class InvokeHttpDispatcher
{
    private readonly IDeviceServiceContract _deviceService;

    private static readonly HashSet<string> HopByHopHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "connection", "keep-alive", "proxy-authenticate",
        "proxy-authorization", "te", "trailer", "transfer-encoding", "upgrade",
    };

    public InvokeHttpDispatcher(IDeviceServiceContract deviceService)
    {
        _deviceService = deviceService;
    }

    public async Task<InvokeHttpResult> DispatchAsync(
        CallerContext caller,
        string deviceId,
        string method,
        int port,
        string path,
        IReadOnlyDictionary<string, string>? headers,
        byte[]? body,
        CancellationToken ct)
    {
        // Strip hop-by-hop headers
        var cleanHeaders = headers?
            .Where(kv => !HopByHopHeaders.Contains(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

        var request = new InvokeHttpRequest(deviceId, method, port, path,
            cleanHeaders as IReadOnlyDictionary<string, string>, body);

        return await _deviceService.InvokeHttpAsync(caller, request, ct);
    }
}

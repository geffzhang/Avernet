using Ocb.PluginApi;

namespace Ocb.Gateway.Forwarding;

/// <summary>
/// Extension point for building a <see cref="ForwardRequest"/> from an
/// ASP.NET Core <see cref="HttpContext"/>.
/// </summary>
public static class ForwardRequestFactory
{
    /// <summary>
    /// Read the inbound request into a normalised forward request.
    /// The returned body stream is the raw request body, not copied into
    /// memory — callers are responsible for lifetime management.
    /// </summary>
    public static async ValueTask<ForwardRequest> FromHttpContextAsync(HttpContext context)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, values) in context.Request.Headers)
        {
            headers[name] = values.ToString();
        }

        return new ForwardRequest(
            Method: context.Request.Method,
            Path: context.Request.Path,
            Query: context.Request.QueryString.Value ?? string.Empty,
            Headers: headers,
            Body: context.Request.Body
        );
    }
}

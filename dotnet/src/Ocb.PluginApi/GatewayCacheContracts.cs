namespace Ocb.PluginApi;

public interface ICacheProvider : IPluginContract
{
    ValueTask<T?> GetAsync<T>(string key, CancellationToken ct);

    ValueTask SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct);

    ValueTask RemoveAsync(string key, CancellationToken ct);
}

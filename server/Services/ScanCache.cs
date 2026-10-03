using Microsoft.Extensions.Caching.Memory;

namespace UsuariosRetirados.Server.Services;

public interface IScanCache
{
    bool TryGet(string key, out FilterResult result);
    void Set(string key, FilterResult result);
}

/// <summary>
/// Short-lived in-memory cache of full scan results so a preview followed by a
/// process does not scan the ~165 MB padrón twice.
/// </summary>
public class ScanCache : IScanCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    private readonly IMemoryCache _cache;

    public ScanCache(IMemoryCache cache)
    {
        _cache = cache;
    }

    public bool TryGet(string key, out FilterResult result)
    {
        return _cache.TryGetValue(key, out result!);
    }

    public void Set(string key, FilterResult result)
    {
        _cache.Set(key, result, new MemoryCacheEntryOptions
        {
            SlidingExpiration = Ttl
        });
    }
}

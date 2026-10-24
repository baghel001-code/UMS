using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace Infrastructure.Services
{
    public class CacheService
    {
        //private readonly IMemoryCache _cache;
        //public CacheService(IMemoryCache cache) => _cache = cache;


        //public T? Get<T>(string key) => _cache.TryGetValue(key, out T val) ? val : default;


        //public void Set<T>(string key, T value, TimeSpan? ttl = null)
        //{
        //    var opts = new MemoryCacheEntryOptions();
        //    if (ttl.HasValue) opts.SetAbsoluteExpiration(ttl.Value);
        //    _cache.Set(key, value, opts);
        //}


        //public void Remove(string key) => _cache.Remove(key);
    }
}

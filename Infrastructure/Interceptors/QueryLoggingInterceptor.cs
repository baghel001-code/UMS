using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Interceptors
{
    public class QueryLoggingInterceptor
    {
        // This is a lightweight decorator idea — use AOP or explicit wrapper in production
        public async Task<T> TimeAsync<T>(Func<Task<T>> action, string name)
        {
            var sw = Stopwatch.StartNew();
            var result = await action();
            sw.Stop();
            Console.WriteLine($"[Query] {name} took {sw.ElapsedMilliseconds}ms");
            return result;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Interceptors
{
    public class AuditInterceptor
    {
        public Task AuditAsync(string action, object? data)
        {
            // write to audit store or log
            Console.WriteLine($"[Audit] {action} => {System.Text.Json.JsonSerializer.Serialize(data)}");
            return Task.CompletedTask;
        }
    }
}

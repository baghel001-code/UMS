using AMS.Infrastructure.Persistence;

namespace AMS.Web.Services
{
    /// <summary>Development convenience: fills an empty AMS database with demo data for the configured UMS tenant on first sign-in.</summary>
    public class DemoData
    {
        private static readonly SemaphoreSlim Lock = new(1, 1);
        private readonly AmsDbContextFactory _factory;
        private readonly IConfiguration _config;
        private readonly TimeProvider _time;

        public DemoData(AmsDbContextFactory factory, IConfiguration config, TimeProvider time)
        {
            _factory = factory;
            _config = config;
            _time = time;
        }

        public async Task EnsureAsync(Guid tenantId)
        {
            if (!bool.TryParse(_config["Ams:DemoData:Enabled"], out var on) || !on) return;
            if (Guid.TryParse(_config["Ams:DemoData:TenantId"], out var only) && only != tenantId) return;
            await Lock.WaitAsync();
            try
            {
                await using var db = _factory.CreateContext(tenantId);
                await AmsDemoSeeder.SeedIfEmptyAsync(db, _time.GetLocalNow().DateTime);
            }
            finally
            {
                Lock.Release();
            }
        }
    }
}

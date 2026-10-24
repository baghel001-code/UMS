using AMS.Core.Security;
using AMS.Core.Services;
using AMS.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AMS.Tests
{
    /// <summary>Stands in for UMS: answers with a rule and records every question it was asked.</summary>
    public sealed class FakeGuard : IAccessGuard
    {
        public Func<AccessCheck, bool> Rule { get; set; } = _ => true;
        public List<AccessCheck> Ensured { get; } = new();
        public List<AccessCheck> Asked { get; } = new();
        public Actor Actor { get; set; }

        public FakeGuard(Actor actor) => Actor = actor;

        public Task EnsureAsync(AccessCheck check, CancellationToken ct = default)
        {
            Ensured.Add(check);
            if (!Rule(check)) throw new AccessDeniedException(check.Key, $"denied {check.Key}");
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<bool>> CanAsync(IReadOnlyList<AccessCheck> checks, CancellationToken ct = default)
        {
            Asked.AddRange(checks);
            return Task.FromResult<IReadOnlyList<bool>>(checks.Select(Rule).ToList());
        }
    }

    public sealed class FakeDirectory : IPeopleDirectory
    {
        public static readonly List<Person> People = new()
        {
            new("VMM10002", "Priya Verma", "priya@vmm.example", "Store Operations", "Cashier", "S045", "North", "Permanent", "Active"),
            new("VMM10006", "Kavya Nair", "kavya@vmm.example", "Finance", "Analyst", "HO-GGN", "South", "Permanent", "Active"),
            new("VMM10007", "Imran Khan", "imran@vmm.example", "Supply Chain", "Picker", "DC-NR1", "West", "Contract", "Active"),
            new("VMM10010", "Rahul Agarwal", "rahul@vmm.example", "IT", "IT Asset Lead", "HO-GGN", "North", "Permanent", "Active"),
        };

        public Task<IReadOnlyList<Person>> SearchAsync(string? search, int take = 30, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Person>>(People.Where(p => search == null || p.FullName.Contains(search, StringComparison.OrdinalIgnoreCase)).Take(take).ToList());

        public Task<Person?> FindAsync(string empCode, CancellationToken ct = default) => Task.FromResult(People.FirstOrDefault(p => p.EmpCode == empCode));
    }

    /// <summary>SQLite in-memory AMS database with the demo data for one tenant, and services wired to a <see cref="FakeGuard"/>.</summary>
    public sealed class Harness : IAsyncDisposable
    {
        public static readonly Guid Tenant = Guid.Parse("9c931e4a-cf70-bb5b-0d66-217df75d70aa");
        public static readonly DateTime Now = new(2026, 10, 23, 11, 0, 0);

        private readonly SqliteConnection _conn;
        public AmsDbContextFactory Factory { get; }
        public FakeGuard Guard { get; } = new(new Actor(Guid.NewGuid(), Tenant, "VMM10010", "Rahul Agarwal"));
        public FakeDirectory People { get; } = new();
        public TimeProvider Time { get; } = new FixedTime(Now);

        private Harness(SqliteConnection conn, AmsDbContextFactory factory)
        {
            _conn = conn;
            Factory = factory;
        }

        public static async Task<Harness> CreateAsync(bool seed = true)
        {
            var conn = new SqliteConnection("Data Source=:memory:");
            await conn.OpenAsync();
            var factory = new AmsDbContextFactory(new DbContextOptionsBuilder<AmsDbContext>().UseSqlite(conn).Options);
            await using (var db = factory.CreateContext(Tenant))
            {
                await db.Database.EnsureCreatedAsync();
                if (seed) await AmsDemoSeeder.SeedIfEmptyAsync(db, Now);
            }
            return new Harness(conn, factory);
        }

        public AmsDbContext Db(Guid? tenant = null) => Factory.CreateContext(tenant ?? Tenant);

        public AssetService Assets => new(Guard, Factory, People, Time);
        public RequestService Requests => new(Guard, Factory, People, Assets, Time);
        public MaintenanceService Maintenance => new(Guard, Factory, Time);
        public LicenseService Licenses => new(Guard, Factory, People, Time);
        public InventoryService Inventory => new(Guard, Factory, People, Time);
        public LookupService Lookups => new(Guard, Factory, Time);
        public ReportService Reports => new(Guard, Factory, Time);

        public async ValueTask DisposeAsync() => await _conn.DisposeAsync();

        private sealed class FixedTime : TimeProvider
        {
            private readonly DateTimeOffset _now;
            public FixedTime(DateTime now) => _now = new DateTimeOffset(now, TimeSpan.Zero);
            public override DateTimeOffset GetUtcNow() => _now;
            public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
        }
    }
}

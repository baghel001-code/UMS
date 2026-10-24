using AMS.Application.Common;
using AMS.Application.Interfaces;
using AMS.Domain.Base;
using AMS.Domain.Requests;
using AMS.Infrastructure.Persistence;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using static AMS.Tests.TestData;

namespace AMS.Tests
{
    /// <summary>
    /// Runs only when AMS_TEST_SQL holds a SQL Server connection string (the database name in it is ignored), e.g.
    /// AMS_TEST_SQL="Server=localhost,1433;User Id=sa;Password=...;TrustServerCertificate=True" dotnet test
    /// </summary>
    public sealed class SqlFactAttribute : FactAttribute
    {
        public SqlFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AMS_TEST_SQL"))) Skip = "Set AMS_TEST_SQL to run the SQL Server tests.";
        }
    }

    /// <summary>A throw-away database built from database/*.sql and filled with the demo data, dropped afterwards.</summary>
    public sealed class SqlDatabase : IAsyncLifetime
    {
        public DapperHelper Db { get; private set; } = default!;
        private string? _master;
        private string _name = "";

        public async Task InitializeAsync()
        {
            var raw = Environment.GetEnvironmentVariable("AMS_TEST_SQL");
            if (string.IsNullOrWhiteSpace(raw)) return;
            _name = $"VMM_AMS_Test_{Guid.NewGuid():N}";
            var cs = new SqlConnectionStringBuilder(raw) { InitialCatalog = _name };
            _master = new SqlConnectionStringBuilder(raw) { InitialCatalog = "master" }.ConnectionString;
            var options = new AmsDatabaseOptions { ConnectionString = cs.ConnectionString };
            Db = new DapperHelper(options, NullLogger<DapperHelper>.Instance);
            await new DatabaseInitializer(options, Db, NullLogger<DatabaseInitializer>.Instance).InitialiseAsync(Path.Combine(AppContext.BaseDirectory, "database"));
            var clock = new BusinessClock(TimeProvider.System);
            await new DemoDataSeeder(Db, clock, NullLogger<DemoDataSeeder>.Instance).SeedIfEmptyAsync(Tenant);
        }

        public async Task DisposeAsync()
        {
            if (_master == null) return;
            SqlConnection.ClearAllPools();
            await using var conn = new SqlConnection(_master);
            await conn.ExecuteAsync($"ALTER DATABASE [{_name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_name}];");
        }
    }

    // The stored procedures enforce the data rules (tenant isolation, one holder per asset, unique tags) whatever the caller.
    public class SqlIntegrationTests : IClassFixture<SqlDatabase>
    {
        private readonly SqlDatabase _sql;
        public SqlIntegrationTests(SqlDatabase sql) => _sql = sql;

        private static WriteContext Write(Guid tenant) => new(tenant, DateTime.Now, "VMM10010", "Rahul Agarwal");

        [SqlFact]
        public async Task Each_tenant_sees_only_its_own_assets()
        {
            var assets = new AssetRepository(_sql.Db);

            Assert.NotEmpty(await assets.ListAsync(Tenant, new AssetFilterReq()));
            Assert.Empty(await assets.ListAsync(Guid.NewGuid(), new AssetFilterReq()));
        }

        [SqlFact]
        public async Task An_asset_cannot_be_checked_out_twice()
        {
            var assets = new AssetRepository(_sql.Db);
            var issued = (await assets.ListAsync(Tenant, new AssetFilterReq { CheckedOut = true })).First();

            var res = await assets.CheckoutAsync(Write(Tenant), issued.Id, Priya, null, null);

            Assert.Equal(ResponseCodes.RuleViolation, res.Code);
            Assert.Equal(issued.AssignedToEmpCode, (await assets.GetAsync(Tenant, issued.Id))!.AssignedToEmpCode);
        }

        [SqlFact]
        public async Task Checkout_and_checkin_round_trip_and_are_recorded_in_history()
        {
            var assets = new AssetRepository(_sql.Db);
            var free = (await assets.ListAsync(Tenant, new AssetFilterReq { CheckedOut = false }))
                .First(a => a.StatusType == Domain.Enums.StatusType.Deployable);

            Assert.True((await assets.CheckoutAsync(Write(Tenant), free.Id, Priya, null, "test")).Ok);
            Assert.Equal("VMM10002", (await assets.GetAsync(Tenant, free.Id))!.AssignedToEmpCode);
            Assert.Contains((await assets.MyItemsAsync(Tenant, "VMM10002")).Assets, a => a.Id == free.Id);

            Assert.True((await assets.CheckinAsync(Write(Tenant), free.Id, null, "back")).Ok);
            Assert.Null((await assets.GetAsync(Tenant, free.Id))!.AssignedToEmpCode);
            var history = await new HistoryRepository(_sql.Db).ListAsync(Tenant, Domain.Enums.ItemType.Asset, free.Id, null, null, null, 10);
            Assert.Contains(history, h => h.Action == Domain.Enums.HistoryAction.CheckedOut && h.Target!.Contains("Priya"));
            Assert.Contains(history, h => h.Action == Domain.Enums.HistoryAction.CheckedIn);
        }

        [SqlFact]
        public async Task Asset_tags_are_unique_within_a_tenant()
        {
            var assets = new AssetRepository(_sql.Db);
            var existing = (await assets.ListAsync(Tenant, new AssetFilterReq())).First();
            var copy = AssetSaveReq.From(existing);
            copy.Id = 0;

            var res = await assets.SaveAsync(Write(Tenant), copy);

            Assert.Equal(ResponseCodes.Duplicate, res.Code);
        }
    }
}

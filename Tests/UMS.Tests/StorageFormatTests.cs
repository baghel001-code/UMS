using DomainLayer.Entities;
using Infrastructure.Persistence;
using Infrastructure.Persistence.SqlServer;
using System.Text.Json;

namespace UMS.Tests
{
    // The SQL Server repositories store conditions, windows, scopes and settings as JSON; they must round-trip exactly.
    public class StorageFormatTests
    {
        private static readonly DemoSeed Seed = DemoSeed.Create(new DateTime(2026, 10, 24, 11, 0, 0));

        [Fact]
        public void Policy_conditions_round_trip()
        {
            foreach (var p in Seed.Policies)
            {
                var json = JsonSerializer.Serialize(p.Conditions, Sql.Json);
                var back = JsonSerializer.Deserialize<List<PolicyCondition>>(json, Sql.Json)!;
                Assert.Equal(p.Conditions, back);
            }
        }

        [Fact]
        public void Schedule_windows_round_trip()
        {
            foreach (var s in Seed.Schedules)
            {
                var back = JsonSerializer.Deserialize<List<TimeWindow>>(JsonSerializer.Serialize(s.Windows, Sql.Json), Sql.Json)!;
                Assert.Equal(s.Windows.Count, back.Count);
                for (var i = 0; i < back.Count; i++)
                {
                    Assert.Equal(s.Windows[i].Days, back[i].Days);
                    Assert.Equal(s.Windows[i].Start, back[i].Start);
                    Assert.Equal(s.Windows[i].End, back[i].End);
                }
            }
        }

        [Fact]
        public void Security_settings_round_trip() =>
            Assert.Equal(SecuritySettings.Default, JsonSerializer.Deserialize<SecuritySettings>(JsonSerializer.Serialize(SecuritySettings.Default, Sql.Json), Sql.Json));

        [Fact]
        public void Csv_helpers_round_trip() =>
            Assert.Equal(new[] { "View", "Approve" }, Sql.SplitCsv(Sql.Csv(new[] { "View", "Approve" })));
    }
}

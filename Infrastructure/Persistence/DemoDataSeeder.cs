using ApplicationLayer.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Persistence
{
    /// <summary>
    /// Writes the demo data into an empty SQL Server database through the repositories
    /// (Persistence:SeedDemoData = true). Does nothing when any tenant already exists.
    /// </summary>
    public static class DemoDataSeeder
    {
        public static async Task<bool> SeedIfEmptyAsync(IServiceProvider services, string? adminPassword, CancellationToken ct = default)
        {
            using var scope = services.CreateScope();
            var sp = scope.ServiceProvider;
            var tenants = sp.GetRequiredService<ITenantRepository>();
            if ((await tenants.ListAsync(ct)).Count > 0) return false;

            var seed = DemoSeed.Create();
            ApplyAdminPassword(seed, sp.GetRequiredService<IPasswordHasher>(), adminPassword);

            foreach (var t in seed.Tenants)
            {
                await tenants.SaveAsync(t, ct);
                await tenants.SaveSettingsAsync(t.Id, seed.Settings[t.Id], ct);
            }
            var apps = sp.GetRequiredService<IApplicationRepository>();
            foreach (var a in seed.Applications) await apps.SaveAsync(a, ct);
            var roles = sp.GetRequiredService<IRoleRepository>();
            foreach (var r in seed.Roles) await roles.SaveAsync(r, ct);
            var schedules = sp.GetRequiredService<IScheduleRepository>();
            foreach (var s in seed.Schedules) await schedules.SaveAsync(s, ct);
            foreach (var h in seed.Holidays) await schedules.SaveHolidayAsync(h, ct);
            var policies = sp.GetRequiredService<IPolicyRepository>();
            foreach (var p in seed.Policies) await policies.SaveAsync(p, ct);
            var users = sp.GetRequiredService<IUserAccountRepository>();
            foreach (var u in seed.Users) await users.SaveAsync(u, ct);
            var logs = sp.GetRequiredService<ILogRepository>();
            foreach (var l in seed.AuditLogs) await logs.AddAuditAsync(l, ct);
            foreach (var l in seed.ActionLogs) await logs.AddUserActionAsync(l, ct);
            foreach (var l in seed.ExceptionLogs) await logs.AddExceptionAsync(l, ct);
            return true;
        }

        /// <summary>The seeded UMS administrator (Ankit Saxena, VMM10009) can sign in when a password is configured.</summary>
        public static void ApplyAdminPassword(DemoSeed seed, IPasswordHasher hasher, string? adminPassword)
        {
            if (string.IsNullOrWhiteSpace(adminPassword)) return;
            seed.UserByEmpCode("VMM10009").SetPasswordHash(hasher.Hash(adminPassword));
        }
    }
}

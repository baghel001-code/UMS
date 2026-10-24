using ApplicationLayer.Interfaces;
using Dapper;
using DomainLayer.Entities;
using DomainLayer.Enumerations;
using DomainLayer.ValueObjects;
using Infrastructure.Database;
using System.Data;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Infrastructure.Persistence.SqlServer
{
    // Dapper repositories for database/001_create_access_control_schema.sql (Persistence:Provider = "SqlServer").
    // Child collections are deleted and re-inserted inside one transaction on save.

    internal static class Sql
    {
        public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

        public static string Csv(IEnumerable<string> values) => string.Join(",", values);
        public static List<string> SplitCsv(string? csv) =>
            string.IsNullOrWhiteSpace(csv) ? new List<string>() : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        public static T ParseEnum<T>(string value) where T : struct, Enum => Enum.Parse<T>(value, ignoreCase: true);
        public static DateOnly? ToDateOnly(DateTime? d) => d is null ? null : DateOnly.FromDateTime(d.Value);
        public static DateTime? ToDateTime(DateOnly? d) => d?.ToDateTime(TimeOnly.MinValue);

        public static async Task InTransactionAsync(IDbConnectionFactory factory, Func<IDbConnection, IDbTransaction, Task> work)
        {
            using var conn = factory.CreateConnection();
            conn.Open();
            using var tx = conn.BeginTransaction();
            await work(conn, tx);
            tx.Commit();
        }
    }

    // ---------------------------------------------------------------- tenants

    public sealed class SqlTenantRepository : ITenantRepository
    {
        private readonly IDbConnectionFactory _db;
        public SqlTenantRepository(IDbConnectionFactory db) => _db = db;

        private sealed class Row
        {
            public Guid Id { get; set; }
            public string Code { get; set; } = "";
            public string Name { get; set; } = "";
            public bool IsActive { get; set; }
            public DateTime CreatedOn { get; set; }
            public Tenant ToEntity() => new(Id, Code, Name, IsActive, DateOnly.FromDateTime(CreatedOn));
        }

        public async Task<IReadOnlyList<Tenant>> ListAsync(CancellationToken ct = default)
        {
            using var conn = _db.CreateConnection();
            return (await conn.QueryAsync<Row>("SELECT * FROM dbo.Tenants ORDER BY CreatedOn")).Select(r => r.ToEntity()).ToList();
        }

        public async Task<Tenant?> GetAsync(Guid id, CancellationToken ct = default)
        {
            using var conn = _db.CreateConnection();
            return (await conn.QueryFirstOrDefaultAsync<Row>("SELECT * FROM dbo.Tenants WHERE Id = @id", new { id }))?.ToEntity();
        }

        public async Task SaveAsync(Tenant t, CancellationToken ct = default)
        {
            using var conn = _db.CreateConnection();
            await conn.ExecuteAsync(@"
UPDATE dbo.Tenants SET Code = @Code, Name = @Name, IsActive = @IsActive WHERE Id = @Id;
IF @@ROWCOUNT = 0 INSERT INTO dbo.Tenants (Id, Code, Name, IsActive, CreatedOn) VALUES (@Id, @Code, @Name, @IsActive, @CreatedOn);",
                new { t.Id, t.Code, t.Name, t.IsActive, CreatedOn = t.CreatedOn.ToDateTime(TimeOnly.MinValue) });
        }

        public async Task<SecuritySettings?> GetSettingsAsync(Guid tenantId, CancellationToken ct = default)
        {
            using var conn = _db.CreateConnection();
            var json = await conn.QueryFirstOrDefaultAsync<string>("SELECT SettingsJson FROM dbo.TenantSettings WHERE TenantId = @tenantId", new { tenantId });
            return json is null ? null : JsonSerializer.Deserialize<SecuritySettings>(json, Sql.Json);
        }

        public async Task SaveSettingsAsync(Guid tenantId, SecuritySettings settings, CancellationToken ct = default)
        {
            using var conn = _db.CreateConnection();
            await conn.ExecuteAsync(@"
UPDATE dbo.TenantSettings SET SettingsJson = @json WHERE TenantId = @tenantId;
IF @@ROWCOUNT = 0 INSERT INTO dbo.TenantSettings (TenantId, SettingsJson) VALUES (@tenantId, @json);",
                new { tenantId, json = JsonSerializer.Serialize(settings, Sql.Json) });
        }
    }

    // ---------------------------------------------------------------- applications

    public sealed class SqlApplicationRepository : IApplicationRepository
    {
        private readonly IDbConnectionFactory _db;
        public SqlApplicationRepository(IDbConnectionFactory db) => _db = db;

        private sealed class AppRow
        {
            public Guid Id { get; set; }
            public string Code { get; set; } = "";
            public string Name { get; set; } = "";
            public string Type { get; set; } = "";
            public string Vendor { get; set; } = "";
            public string OwnerDept { get; set; } = "";
            public string Description { get; set; } = "";
            public bool IsActive { get; set; }
        }
        private sealed class LinkRow { public Guid ApplicationId { get; set; } public Guid TenantId { get; set; } }
        private sealed class ModuleRow { public Guid Id { get; set; } public Guid ApplicationId { get; set; } public string Code { get; set; } = ""; public string Name { get; set; } = ""; }
        private sealed class ResourceRow { public Guid Id { get; set; } public Guid ModuleId { get; set; } public string Code { get; set; } = ""; public string Name { get; set; } = ""; public string Actions { get; set; } = ""; }

        private async Task<IReadOnlyList<Application>> LoadAsync(string where, object param)
        {
            using var conn = _db.CreateConnection();
            using var grid = await conn.QueryMultipleAsync($@"
SELECT a.* FROM dbo.Applications a {where};
SELECT at.* FROM dbo.ApplicationTenants at JOIN dbo.Applications a ON a.Id = at.ApplicationId {where};
SELECT m.* FROM dbo.Modules m JOIN dbo.Applications a ON a.Id = m.ApplicationId {where} ORDER BY m.SortOrder;
SELECT r.* FROM dbo.Resources r JOIN dbo.Modules m ON m.Id = r.ModuleId JOIN dbo.Applications a ON a.Id = m.ApplicationId {where} ORDER BY r.SortOrder;", param);
            var apps = (await grid.ReadAsync<AppRow>()).ToList();
            var links = (await grid.ReadAsync<LinkRow>()).ToLookup(l => l.ApplicationId);
            var modules = (await grid.ReadAsync<ModuleRow>()).ToLookup(m => m.ApplicationId);
            var resources = (await grid.ReadAsync<ResourceRow>()).ToLookup(r => r.ModuleId);

            return apps.Select(a => new Application(a.Id, a.Code, a.Name, Sql.ParseEnum<AppType>(a.Type), a.Vendor, a.OwnerDept, a.Description, a.IsActive,
                links[a.Id].Select(l => l.TenantId),
                modules[a.Id].Select(m => new AppModule(m.Id, m.Code, m.Name,
                    resources[m.Id].Select(r => new AppResource(r.Id, r.Code, r.Name, Sql.SplitCsv(r.Actions).Select(Sql.ParseEnum<AccessAction>).ToList())).ToList()))))
                .ToList();
        }

        public Task<IReadOnlyList<Application>> ListAsync(Guid? tenantId = null, CancellationToken ct = default) =>
            tenantId is null
                ? LoadAsync("", new { })
                : LoadAsync("WHERE EXISTS (SELECT 1 FROM dbo.ApplicationTenants x WHERE x.ApplicationId = a.Id AND x.TenantId = @tenantId)", new { tenantId });

        public async Task<Application?> GetAsync(Guid id, CancellationToken ct = default) => (await LoadAsync("WHERE a.Id = @id", new { id })).FirstOrDefault();

        public Task SaveAsync(Application a, CancellationToken ct = default) => Sql.InTransactionAsync(_db, async (conn, tx) =>
        {
            await conn.ExecuteAsync(@"
UPDATE dbo.Applications SET Code=@Code, Name=@Name, Type=@Type, Vendor=@Vendor, OwnerDept=@OwnerDept, Description=@Description, IsActive=@IsActive WHERE Id=@Id;
IF @@ROWCOUNT = 0 INSERT INTO dbo.Applications (Id, Code, Name, Type, Vendor, OwnerDept, Description, IsActive)
                   VALUES (@Id, @Code, @Name, @Type, @Vendor, @OwnerDept, @Description, @IsActive);
DELETE FROM dbo.ApplicationTenants WHERE ApplicationId = @Id;
DELETE FROM dbo.Modules WHERE ApplicationId = @Id;",
                new { a.Id, a.Code, a.Name, Type = a.Type.ToString(), a.Vendor, a.OwnerDept, a.Description, a.IsActive }, tx);
            await conn.ExecuteAsync("INSERT INTO dbo.ApplicationTenants (ApplicationId, TenantId) VALUES (@ApplicationId, @TenantId)",
                a.TenantIds.Select(t => new { ApplicationId = a.Id, TenantId = t }), tx);
            await conn.ExecuteAsync("INSERT INTO dbo.Modules (Id, ApplicationId, Code, Name, SortOrder) VALUES (@Id, @ApplicationId, @Code, @Name, @SortOrder)",
                a.Modules.Select((m, i) => new { m.Id, ApplicationId = a.Id, m.Code, m.Name, SortOrder = i }), tx);
            await conn.ExecuteAsync("INSERT INTO dbo.Resources (Id, ModuleId, Code, Name, Actions, SortOrder) VALUES (@Id, @ModuleId, @Code, @Name, @Actions, @SortOrder)",
                a.Modules.SelectMany(m => m.Resources.Select((r, i) => new { r.Id, ModuleId = m.Id, r.Code, r.Name, Actions = Sql.Csv(r.Actions.Select(x => x.ToString())), SortOrder = i })), tx);
        });

        public async Task DeleteAsync(Guid id, CancellationToken ct = default)
        {
            using var conn = _db.CreateConnection();
            await conn.ExecuteAsync("DELETE FROM dbo.Applications WHERE Id = @id", new { id });
        }
    }

    // ---------------------------------------------------------------- roles

    public sealed class SqlRoleRepository : IRoleRepository
    {
        private readonly IDbConnectionFactory _db;
        public SqlRoleRepository(IDbConnectionFactory db) => _db = db;

        private sealed class Row
        {
            public Guid Id { get; set; }
            public Guid TenantId { get; set; }
            public string Code { get; set; } = "";
            public string Name { get; set; } = "";
            public string Description { get; set; } = "";
            public Guid? AppId { get; set; }
            public bool IsSystem { get; set; }
            public bool IsActive { get; set; }
        }
        private sealed class PermRow { public Guid RoleId { get; set; } public string PermissionKey { get; set; } = ""; }

        private async Task<IReadOnlyList<Role>> LoadAsync(string where, object param)
        {
            using var conn = _db.CreateConnection();
            using var grid = await conn.QueryMultipleAsync($@"
SELECT r.* FROM dbo.Roles r {where} ORDER BY r.Code;
SELECT p.* FROM dbo.RolePermissions p JOIN dbo.Roles r ON r.Id = p.RoleId {where};", param);
            var roles = (await grid.ReadAsync<Row>()).ToList();
            var perms = (await grid.ReadAsync<PermRow>()).ToLookup(p => p.RoleId);
            return roles.Select(r => new Role(r.Id, r.TenantId, r.Code, r.Name, r.Description, r.AppId, perms[r.Id].Select(p => p.PermissionKey), r.IsSystem, r.IsActive)).ToList();
        }

        public Task<IReadOnlyList<Role>> ListAsync(Guid tenantId, CancellationToken ct = default) => LoadAsync("WHERE r.TenantId = @tenantId", new { tenantId });
        public async Task<Role?> GetAsync(Guid id, CancellationToken ct = default) => (await LoadAsync("WHERE r.Id = @id", new { id })).FirstOrDefault();

        public Task SaveAsync(Role r, CancellationToken ct = default) => Sql.InTransactionAsync(_db, async (conn, tx) =>
        {
            await conn.ExecuteAsync(@"
UPDATE dbo.Roles SET Code=@Code, Name=@Name, Description=@Description, AppId=@AppId, IsSystem=@IsSystem, IsActive=@IsActive WHERE Id=@Id;
IF @@ROWCOUNT = 0 INSERT INTO dbo.Roles (Id, TenantId, Code, Name, Description, AppId, IsSystem, IsActive)
                   VALUES (@Id, @TenantId, @Code, @Name, @Description, @AppId, @IsSystem, @IsActive);
DELETE FROM dbo.RolePermissions WHERE RoleId = @Id;", new { r.Id, r.TenantId, r.Code, r.Name, r.Description, r.AppId, r.IsSystem, r.IsActive }, tx);
            await conn.ExecuteAsync("INSERT INTO dbo.RolePermissions (RoleId, PermissionKey) VALUES (@RoleId, @PermissionKey)",
                r.Permissions.Select(p => new { RoleId = r.Id, PermissionKey = p }), tx);
        });

        public async Task DeleteAsync(Guid id, CancellationToken ct = default)
        {
            using var conn = _db.CreateConnection();
            await conn.ExecuteAsync("DELETE FROM dbo.Roles WHERE Id = @id", new { id });
        }
    }

    // ---------------------------------------------------------------- users

    public sealed class SqlUserAccountRepository : IUserAccountRepository
    {
        private readonly IDbConnectionFactory _db;
        public SqlUserAccountRepository(IDbConnectionFactory db) => _db = db;

        private sealed class Row
        {
            public Guid Id { get; set; }
            public Guid TenantId { get; set; }
            public string EmpCode { get; set; } = "";
            public string FullName { get; set; } = "";
            public string Email { get; set; } = "";
            public string? PhoneCountryCode { get; set; }
            public string? PhoneNumber { get; set; }
            public string Department { get; set; } = "";
            public string Designation { get; set; } = "";
            public string Location { get; set; } = "";
            public string Region { get; set; } = "";
            public string EmploymentType { get; set; } = "";
            public string Status { get; set; } = "";
            public bool MfaEnabled { get; set; }
            public DateTime? LastLogin { get; set; }
            public DateTime? AccountExpiry { get; set; }
            public string? PasswordHash { get; set; }
        }
        private sealed class AttrRow { public Guid UserId { get; set; } public string AttributeKey { get; set; } = ""; public string AttributeValue { get; set; } = ""; }
        private sealed class AssignmentRow
        {
            public Guid UserId { get; set; }
            public Guid RoleId { get; set; }
            public string ScopeJson { get; set; } = "{}";
            public DateTime? ValidFrom { get; set; }
            public DateTime? ValidTo { get; set; }
            public string AssignedBy { get; set; } = "";
            public DateTime AssignedOn { get; set; }
        }

        private async Task<IReadOnlyList<User>> LoadAsync(string where, object param)
        {
            using var conn = _db.CreateConnection();
            using var grid = await conn.QueryMultipleAsync($@"
SELECT u.* FROM dbo.UserAccounts u {where} ORDER BY u.EmpCode;
SELECT a.* FROM dbo.UserAttributes a JOIN dbo.UserAccounts u ON u.Id = a.UserId {where};
SELECT ra.* FROM dbo.UserRoleAssignments ra JOIN dbo.UserAccounts u ON u.Id = ra.UserId {where};", param);
            var rows = (await grid.ReadAsync<Row>()).ToList();
            var attrs = (await grid.ReadAsync<AttrRow>()).ToLookup(a => a.UserId);
            var roles = (await grid.ReadAsync<AssignmentRow>()).ToLookup(a => a.UserId);

            return rows.Select(r =>
            {
                var phone = r.PhoneCountryCode != null && r.PhoneNumber != null ? PhoneNumber.Create(r.PhoneCountryCode, r.PhoneNumber) : null;
                var user = new User(r.Id, r.TenantId, r.EmpCode, r.FullName, new Email(r.Email), phone);
                user.UpdateProfile(r.EmpCode, r.FullName, user.Email, phone, r.Department, r.Designation, r.Location, r.Region,
                    Sql.ParseEnum<EmploymentType>(r.EmploymentType), Sql.ParseEnum<UserStatus>(r.Status), r.MfaEnabled, Sql.ToDateOnly(r.AccountExpiry));
                if (r.LastLogin is { } login) user.RecordLogin(login);
                if (r.PasswordHash != null) user.SetPasswordHash(r.PasswordHash);
                user.SetAttributes(attrs[r.Id].ToDictionary(a => a.AttributeKey, a => a.AttributeValue));
                user.SetRoleAssignments(roles[r.Id].Select(a => new RoleAssignment(a.RoleId,
                    JsonSerializer.Deserialize<Dictionary<string, string>>(a.ScopeJson, Sql.Json) ?? new Dictionary<string, string>(),
                    Sql.ToDateOnly(a.ValidFrom), Sql.ToDateOnly(a.ValidTo), a.AssignedBy, DateOnly.FromDateTime(a.AssignedOn))));
                user.ClearDomainEvents();
                return user;
            }).ToList();
        }

        public Task<IReadOnlyList<User>> ListAsync(Guid tenantId, CancellationToken ct = default) => LoadAsync("WHERE u.TenantId = @tenantId", new { tenantId });
        public async Task<User?> GetAsync(Guid id, CancellationToken ct = default) => (await LoadAsync("WHERE u.Id = @id", new { id })).FirstOrDefault();
        public async Task<User?> GetByEmailAsync(string email, CancellationToken ct = default) => (await LoadAsync("WHERE u.Email = @email", new { email })).FirstOrDefault();
        public async Task<User?> GetByEmpCodeAsync(string empCode, CancellationToken ct = default) => (await LoadAsync("WHERE u.EmpCode = @empCode", new { empCode })).FirstOrDefault();

        public async Task<int> CountWithRoleAsync(Guid roleId, CancellationToken ct = default)
        {
            using var conn = _db.CreateConnection();
            return await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.UserRoleAssignments WHERE RoleId = @roleId", new { roleId });
        }

        public Task SaveAsync(User u, CancellationToken ct = default) => Sql.InTransactionAsync(_db, async (conn, tx) =>
        {
            await conn.ExecuteAsync(@"
UPDATE dbo.UserAccounts SET EmpCode=@EmpCode, FullName=@FullName, Email=@Email, PhoneCountryCode=@PhoneCountryCode, PhoneNumber=@PhoneNumber,
    Department=@Department, Designation=@Designation, Location=@Location, Region=@Region, EmploymentType=@EmploymentType, Status=@Status,
    MfaEnabled=@MfaEnabled, LastLogin=@LastLogin, AccountExpiry=@AccountExpiry, PasswordHash=@PasswordHash
WHERE Id=@Id;
IF @@ROWCOUNT = 0 INSERT INTO dbo.UserAccounts (Id, TenantId, EmpCode, FullName, Email, PhoneCountryCode, PhoneNumber, Department, Designation, Location,
    Region, EmploymentType, Status, MfaEnabled, LastLogin, AccountExpiry, PasswordHash)
VALUES (@Id, @TenantId, @EmpCode, @FullName, @Email, @PhoneCountryCode, @PhoneNumber, @Department, @Designation, @Location,
    @Region, @EmploymentType, @Status, @MfaEnabled, @LastLogin, @AccountExpiry, @PasswordHash);
DELETE FROM dbo.UserAttributes WHERE UserId = @Id;
DELETE FROM dbo.UserRoleAssignments WHERE UserId = @Id;",
                new
                {
                    u.Id, u.TenantId, u.EmpCode, FullName = u.Name, Email = u.Email.Value, PhoneCountryCode = u.Phone?.CountryCode, PhoneNumber = u.Phone?.Number,
                    u.Department, u.Designation, u.Location, u.Region, EmploymentType = u.EmploymentType.ToString(), Status = u.Status.ToString(),
                    u.MfaEnabled, u.LastLogin, AccountExpiry = Sql.ToDateTime(u.AccountExpiry), u.PasswordHash
                }, tx);
            await conn.ExecuteAsync("INSERT INTO dbo.UserAttributes (UserId, AttributeKey, AttributeValue) VALUES (@UserId, @AttributeKey, @AttributeValue)",
                u.Attributes.Select(kv => new { UserId = u.Id, AttributeKey = kv.Key, AttributeValue = kv.Value }), tx);
            await conn.ExecuteAsync(@"INSERT INTO dbo.UserRoleAssignments (UserId, RoleId, ScopeJson, ValidFrom, ValidTo, AssignedBy, AssignedOn)
VALUES (@UserId, @RoleId, @ScopeJson, @ValidFrom, @ValidTo, @AssignedBy, @AssignedOn)",
                u.RoleAssignments.Select(a => new
                {
                    UserId = u.Id, a.RoleId, ScopeJson = JsonSerializer.Serialize(a.Scope, Sql.Json), ValidFrom = Sql.ToDateTime(a.ValidFrom),
                    ValidTo = Sql.ToDateTime(a.ValidTo), a.AssignedBy, AssignedOn = a.AssignedOn.ToDateTime(TimeOnly.MinValue)
                }), tx);
        });
    }

    // ---------------------------------------------------------------- policies

    public sealed class SqlPolicyRepository : IPolicyRepository
    {
        private readonly IDbConnectionFactory _db;
        public SqlPolicyRepository(IDbConnectionFactory db) => _db = db;

        private sealed class Row
        {
            public Guid Id { get; set; }
            public Guid TenantId { get; set; }
            public string Name { get; set; } = "";
            public string Description { get; set; } = "";
            public string Effect { get; set; } = "";
            public string TargetApp { get; set; } = "*";
            public string TargetModule { get; set; } = "*";
            public string TargetResource { get; set; } = "*";
            public string TargetActions { get; set; } = "*";
            public string TargetRoles { get; set; } = "";
            public string Match { get; set; } = "";
            public string ConditionsJson { get; set; } = "[]";
            public int Priority { get; set; }
            public bool IsActive { get; set; }
            public string CreatedBy { get; set; } = "";
            public DateTime CreatedAt { get; set; }

            public Policy ToEntity() => new(Id, TenantId, Name, Description, Sql.ParseEnum<PolicyEffect>(Effect),
                new PolicyTarget(TargetApp, TargetModule, TargetResource, Sql.SplitCsv(TargetActions), Sql.SplitCsv(TargetRoles)),
                Sql.ParseEnum<ConditionMatch>(Match), JsonSerializer.Deserialize<List<PolicyCondition>>(ConditionsJson, Sql.Json) ?? new List<PolicyCondition>(),
                Priority, IsActive, CreatedBy, CreatedAt);
        }

        public async Task<IReadOnlyList<Policy>> ListAsync(Guid tenantId, CancellationToken ct = default)
        {
            using var conn = _db.CreateConnection();
            return (await conn.QueryAsync<Row>("SELECT * FROM dbo.Policies WHERE TenantId = @tenantId ORDER BY Priority", new { tenantId })).Select(r => r.ToEntity()).ToList();
        }

        public async Task<Policy?> GetAsync(Guid id, CancellationToken ct = default)
        {
            using var conn = _db.CreateConnection();
            return (await conn.QueryFirstOrDefaultAsync<Row>("SELECT * FROM dbo.Policies WHERE Id = @id", new { id }))?.ToEntity();
        }

        public async Task SaveAsync(Policy p, CancellationToken ct = default)
        {
            using var conn = _db.CreateConnection();
            await conn.ExecuteAsync(@"
UPDATE dbo.Policies SET Name=@Name, Description=@Description, Effect=@Effect, TargetApp=@TargetApp, TargetModule=@TargetModule,
    TargetResource=@TargetResource, TargetActions=@TargetActions, TargetRoles=@TargetRoles, [Match]=@Match, ConditionsJson=@ConditionsJson,
    Priority=@Priority, IsActive=@IsActive
WHERE Id=@Id;
IF @@ROWCOUNT = 0 INSERT INTO dbo.Policies (Id, TenantId, Name, Description, Effect, TargetApp, TargetModule, TargetResource, TargetActions,
    TargetRoles, [Match], ConditionsJson, Priority, IsActive, CreatedBy, CreatedAt)
VALUES (@Id, @TenantId, @Name, @Description, @Effect, @TargetApp, @TargetModule, @TargetResource, @TargetActions,
    @TargetRoles, @Match, @ConditionsJson, @Priority, @IsActive, @CreatedBy, @CreatedAt);",
                new
                {
                    p.Id, p.TenantId, p.Name, p.Description, Effect = p.Effect.ToString(), TargetApp = p.Target.App, TargetModule = p.Target.Module,
                    TargetResource = p.Target.Resource, TargetActions = Sql.Csv(p.Target.Actions), TargetRoles = Sql.Csv(p.Target.Roles),
                    Match = p.Match.ToString(), ConditionsJson = JsonSerializer.Serialize(p.Conditions, Sql.Json), p.Priority, p.IsActive, p.CreatedBy, p.CreatedAt
                });
        }

        public async Task DeleteAsync(Guid id, CancellationToken ct = default)
        {
            using var conn = _db.CreateConnection();
            await conn.ExecuteAsync("DELETE FROM dbo.Policies WHERE Id = @id", new { id });
        }
    }

    // ---------------------------------------------------------------- schedules & holidays

    public sealed class SqlScheduleRepository : IScheduleRepository
    {
        private readonly IDbConnectionFactory _db;
        public SqlScheduleRepository(IDbConnectionFactory db) => _db = db;

        private sealed class Row
        {
            public Guid Id { get; set; }
            public Guid TenantId { get; set; }
            public string Code { get; set; } = "";
            public string Name { get; set; } = "";
            public string Timezone { get; set; } = "";
            public bool ExcludeHolidays { get; set; }
            public string WindowsJson { get; set; } = "[]";
            public Schedule ToEntity() => new(Id, TenantId, Code, Name, Timezone, ExcludeHolidays,
                JsonSerializer.Deserialize<List<TimeWindow>>(WindowsJson, Sql.Json) ?? new List<TimeWindow>());
        }
        private sealed class HolidayRow
        {
            public Guid Id { get; set; }
            public Guid TenantId { get; set; }
            public DateTime Date { get; set; }
            public string Name { get; set; } = "";
            public string Regions { get; set; } = "";
            public Holiday ToEntity() => new(Id, TenantId, DateOnly.FromDateTime(Date), Name, Sql.SplitCsv(Regions));
        }

        public async Task<IReadOnlyList<Schedule>> ListAsync(Guid tenantId, CancellationToken ct = default)
        {
            using var conn = _db.CreateConnection();
            return (await conn.QueryAsync<Row>("SELECT * FROM dbo.Schedules WHERE TenantId = @tenantId ORDER BY Name", new { tenantId })).Select(r => r.ToEntity()).ToList();
        }

        public async Task<Schedule?> GetAsync(Guid id, CancellationToken ct = default)
        {
            using var conn = _db.CreateConnection();
            return (await conn.QueryFirstOrDefaultAsync<Row>("SELECT * FROM dbo.Schedules WHERE Id = @id", new { id }))?.ToEntity();
        }

        public async Task SaveAsync(Schedule s, CancellationToken ct = default)
        {
            using var conn = _db.CreateConnection();
            await conn.ExecuteAsync(@"
UPDATE dbo.Schedules SET Code=@Code, Name=@Name, Timezone=@Timezone, ExcludeHolidays=@ExcludeHolidays, WindowsJson=@WindowsJson WHERE Id=@Id;
IF @@ROWCOUNT = 0 INSERT INTO dbo.Schedules (Id, TenantId, Code, Name, Timezone, ExcludeHolidays, WindowsJson)
                   VALUES (@Id, @TenantId, @Code, @Name, @Timezone, @ExcludeHolidays, @WindowsJson);",
                new { s.Id, s.TenantId, s.Code, s.Name, s.Timezone, s.ExcludeHolidays, WindowsJson = JsonSerializer.Serialize(s.Windows, Sql.Json) });
        }

        public async Task DeleteAsync(Guid id, CancellationToken ct = default)
        {
            using var conn = _db.CreateConnection();
            await conn.ExecuteAsync("DELETE FROM dbo.Schedules WHERE Id = @id", new { id });
        }

        public async Task<IReadOnlyList<Holiday>> ListHolidaysAsync(Guid tenantId, CancellationToken ct = default)
        {
            using var conn = _db.CreateConnection();
            return (await conn.QueryAsync<HolidayRow>("SELECT * FROM dbo.Holidays WHERE TenantId = @tenantId ORDER BY [Date]", new { tenantId })).Select(r => r.ToEntity()).ToList();
        }

        public async Task<Holiday?> GetHolidayAsync(Guid id, CancellationToken ct = default)
        {
            using var conn = _db.CreateConnection();
            return (await conn.QueryFirstOrDefaultAsync<HolidayRow>("SELECT * FROM dbo.Holidays WHERE Id = @id", new { id }))?.ToEntity();
        }

        public async Task SaveHolidayAsync(Holiday h, CancellationToken ct = default)
        {
            using var conn = _db.CreateConnection();
            await conn.ExecuteAsync(@"
UPDATE dbo.Holidays SET [Date]=@Date, Name=@Name, Regions=@Regions WHERE Id=@Id;
IF @@ROWCOUNT = 0 INSERT INTO dbo.Holidays (Id, TenantId, [Date], Name, Regions) VALUES (@Id, @TenantId, @Date, @Name, @Regions);",
                new { h.Id, h.TenantId, Date = h.Date.ToDateTime(TimeOnly.MinValue), h.Name, Regions = Sql.Csv(h.Regions) });
        }

        public async Task DeleteHolidayAsync(Guid id, CancellationToken ct = default)
        {
            using var conn = _db.CreateConnection();
            await conn.ExecuteAsync("DELETE FROM dbo.Holidays WHERE Id = @id", new { id });
        }
    }

    // ---------------------------------------------------------------- logs

    public sealed class SqlLogRepository : ILogRepository
    {
        private readonly IDbConnectionFactory _db;
        public SqlLogRepository(IDbConnectionFactory db) => _db = db;

        private sealed class AuditRow
        {
            public Guid Id { get; set; }
            public Guid TenantId { get; set; }
            public DateTime Timestamp { get; set; }
            public string Actor { get; set; } = "";
            public string Entity { get; set; } = "";
            public string EntityId { get; set; } = "";
            public string Action { get; set; } = "";
            public string Summary { get; set; } = "";
            public string? OldValues { get; set; }
            public string? NewValues { get; set; }
            public string Ip { get; set; } = "";
            public string Device { get; set; } = "";
        }
        private sealed class ActionRow
        {
            public Guid Id { get; set; }
            public Guid TenantId { get; set; }
            public DateTime Timestamp { get; set; }
            public Guid UserId { get; set; }
            public string UserName { get; set; } = "";
            public string App { get; set; } = "";
            public string Route { get; set; } = "";
            public string Action { get; set; } = "";
            public string Decision { get; set; } = "";
            public string Reason { get; set; } = "";
            public string Ip { get; set; } = "";
        }
        private sealed class ExceptionRow
        {
            public Guid Id { get; set; }
            public Guid TenantId { get; set; }
            public DateTime Timestamp { get; set; }
            public string App { get; set; } = "";
            public string Severity { get; set; } = "";
            public string Message { get; set; } = "";
            public string StackTrace { get; set; } = "";
            public string? UserName { get; set; }
            public string RequestPath { get; set; } = "";
            public string Ip { get; set; } = "";
            public bool Resolved { get; set; }
            public ExceptionLog ToEntity() => new(Id, TenantId, Timestamp, App, Sql.ParseEnum<ExceptionSeverity>(Severity), Message, StackTrace, UserName, RequestPath, Ip, Resolved);
        }

        public async Task AddAuditAsync(AuditLog a, CancellationToken ct = default)
        {
            using var conn = _db.CreateConnection();
            await conn.ExecuteAsync(@"INSERT INTO dbo.AuditLogs (Id, TenantId, [Timestamp], Actor, Entity, EntityId, Action, Summary, OldValues, NewValues, Ip, Device)
VALUES (@Id, @TenantId, @Timestamp, @Actor, @Entity, @EntityId, @Action, @Summary, @OldValues, @NewValues, @Ip, @Device)",
                new { a.Id, a.TenantId, a.Timestamp, a.Actor, a.Entity, a.EntityId, Action = a.Action.ToString(), a.Summary, a.OldValues, a.NewValues, a.Ip, a.Device });
        }

        public async Task<IReadOnlyList<AuditLog>> ListAuditAsync(Guid tenantId, int take, CancellationToken ct = default)
        {
            using var conn = _db.CreateConnection();
            var rows = await conn.QueryAsync<AuditRow>("SELECT TOP (@take) * FROM dbo.AuditLogs WHERE TenantId = @tenantId ORDER BY [Timestamp] DESC", new { tenantId, take });
            return rows.Select(r => new AuditLog(r.Id, r.TenantId, r.Timestamp, r.Actor, r.Entity, r.EntityId, Sql.ParseEnum<AuditAction>(r.Action), r.Summary,
                r.OldValues, r.NewValues, r.Ip, r.Device)).ToList();
        }

        public async Task AddUserActionAsync(UserActionLog a, CancellationToken ct = default)
        {
            using var conn = _db.CreateConnection();
            await conn.ExecuteAsync(@"INSERT INTO dbo.UserActions (Id, TenantId, [Timestamp], UserId, UserName, App, Route, Action, Decision, Reason, Ip)
VALUES (@Id, @TenantId, @Timestamp, @UserId, @UserName, @App, @Route, @Action, @Decision, @Reason, @Ip)",
                new { a.Id, a.TenantId, a.Timestamp, a.UserId, a.UserName, a.App, a.Route, a.Action, Decision = a.Decision.ToString(), a.Reason, a.Ip });
        }

        public async Task<IReadOnlyList<UserActionLog>> ListUserActionsAsync(Guid tenantId, int take, CancellationToken ct = default)
        {
            using var conn = _db.CreateConnection();
            var rows = await conn.QueryAsync<ActionRow>("SELECT TOP (@take) * FROM dbo.UserActions WHERE TenantId = @tenantId ORDER BY [Timestamp] DESC", new { tenantId, take });
            return rows.Select(r => new UserActionLog(r.Id, r.TenantId, r.Timestamp, r.UserId, r.UserName, r.App, r.Route, r.Action,
                Sql.ParseEnum<AccessDecisionResult>(r.Decision), r.Reason, r.Ip)).ToList();
        }

        public async Task AddExceptionAsync(ExceptionLog e, CancellationToken ct = default)
        {
            using var conn = _db.CreateConnection();
            await conn.ExecuteAsync(@"INSERT INTO dbo.ExceptionLogs (Id, TenantId, [Timestamp], App, Severity, Message, StackTrace, UserName, RequestPath, Ip, Resolved)
VALUES (@Id, @TenantId, @Timestamp, @App, @Severity, @Message, @StackTrace, @UserName, @RequestPath, @Ip, @Resolved)",
                new { e.Id, e.TenantId, e.Timestamp, e.App, Severity = e.Severity.ToString(), e.Message, e.StackTrace, e.UserName, e.RequestPath, e.Ip, e.Resolved });
        }

        public async Task<IReadOnlyList<ExceptionLog>> ListExceptionsAsync(Guid tenantId, int take, CancellationToken ct = default)
        {
            using var conn = _db.CreateConnection();
            return (await conn.QueryAsync<ExceptionRow>("SELECT TOP (@take) * FROM dbo.ExceptionLogs WHERE TenantId = @tenantId ORDER BY [Timestamp] DESC", new { tenantId, take }))
                .Select(r => r.ToEntity()).ToList();
        }

        public async Task<ExceptionLog?> GetExceptionAsync(Guid id, CancellationToken ct = default)
        {
            using var conn = _db.CreateConnection();
            return (await conn.QueryFirstOrDefaultAsync<ExceptionRow>("SELECT * FROM dbo.ExceptionLogs WHERE Id = @id", new { id }))?.ToEntity();
        }

        public async Task SaveExceptionAsync(ExceptionLog e, CancellationToken ct = default)
        {
            using var conn = _db.CreateConnection();
            await conn.ExecuteAsync("UPDATE dbo.ExceptionLogs SET Resolved = @Resolved WHERE Id = @Id", new { e.Id, e.Resolved });
        }
    }
}

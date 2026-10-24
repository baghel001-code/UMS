using AMS.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace AMS.Core.Services
{
    /// <summary>AMS data for one tenant (the implementation applies a tenant query filter and stamps TenantId on new rows).</summary>
    public interface IAmsDbContext : IAsyncDisposable
    {
        DbSet<Category> Categories { get; }
        DbSet<Manufacturer> Manufacturers { get; }
        DbSet<Supplier> Suppliers { get; }
        DbSet<Location> Locations { get; }
        DbSet<Department> Departments { get; }
        DbSet<StatusLabel> StatusLabels { get; }
        DbSet<AssetModel> Models { get; }
        DbSet<Asset> Assets { get; }
        DbSet<Maintenance> Maintenances { get; }
        DbSet<License> Licenses { get; }
        DbSet<LicenseSeat> LicenseSeats { get; }
        DbSet<Accessory> Accessories { get; }
        DbSet<AccessoryCheckout> AccessoryCheckouts { get; }
        DbSet<Consumable> Consumables { get; }
        DbSet<ConsumableIssue> ConsumableIssues { get; }
        DbSet<Component> Components { get; }
        DbSet<ComponentInstall> ComponentInstalls { get; }
        DbSet<AssetRequest> Requests { get; }
        DbSet<HistoryEntry> History { get; }

        Task<int> SaveChangesAsync(CancellationToken ct = default);
    }

    public interface IAmsDbContextFactory
    {
        IAmsDbContext Create(Guid tenantId);
    }

    /// <summary>A UMS colleague as AMS needs to know them (from the UMS directory).</summary>
    public sealed record Person(string EmpCode, string FullName, string Email, string Department, string Designation, string Location, string Region,
        string EmploymentType, string Status);

    /// <summary>UMS directory lookups (people to check items out to).</summary>
    public interface IPeopleDirectory
    {
        Task<IReadOnlyList<Person>> SearchAsync(string? search, int take = 30, CancellationToken ct = default);
        Task<Person?> FindAsync(string empCode, CancellationToken ct = default);
    }
}

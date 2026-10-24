using AMS.Core.Entities;
using AMS.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace AMS.Infrastructure.Persistence
{
    /// <summary>
    /// EF Core context for one UMS tenant: every query is filtered to <see cref="TenantId"/> and new rows are stamped with it,
    /// so a user can never read or write another tenant's assets.
    /// </summary>
    public class AmsDbContext : DbContext, IAmsDbContext
    {
        public Guid TenantId { get; }

        public AmsDbContext(DbContextOptions<AmsDbContext> options, Guid tenantId) : base(options) => TenantId = tenantId;

        public DbSet<Category> Categories => Set<Category>();
        public DbSet<Manufacturer> Manufacturers => Set<Manufacturer>();
        public DbSet<Supplier> Suppliers => Set<Supplier>();
        public DbSet<Location> Locations => Set<Location>();
        public DbSet<Department> Departments => Set<Department>();
        public DbSet<StatusLabel> StatusLabels => Set<StatusLabel>();
        public DbSet<AssetModel> Models => Set<AssetModel>();
        public DbSet<Asset> Assets => Set<Asset>();
        public DbSet<Maintenance> Maintenances => Set<Maintenance>();
        public DbSet<License> Licenses => Set<License>();
        public DbSet<LicenseSeat> LicenseSeats => Set<LicenseSeat>();
        public DbSet<Accessory> Accessories => Set<Accessory>();
        public DbSet<AccessoryCheckout> AccessoryCheckouts => Set<AccessoryCheckout>();
        public DbSet<Consumable> Consumables => Set<Consumable>();
        public DbSet<ConsumableIssue> ConsumableIssues => Set<ConsumableIssue>();
        public DbSet<Component> Components => Set<Component>();
        public DbSet<ComponentInstall> ComponentInstalls => Set<ComponentInstall>();
        public DbSet<AssetRequest> Requests => Set<AssetRequest>();
        public DbSet<HistoryEntry> History => Set<HistoryEntry>();

        protected override void OnModelCreating(ModelBuilder b)
        {
            b.Entity<Category>(e => { e.ToTable("Categories"); e.Property(x => x.Name).HasMaxLength(120); e.HasIndex(x => new { x.TenantId, x.Kind, x.Name }).IsUnique(); });
            b.Entity<Manufacturer>(e => { e.ToTable("Manufacturers"); e.Property(x => x.Name).HasMaxLength(120); });
            b.Entity<Supplier>(e => { e.ToTable("Suppliers"); e.Property(x => x.Name).HasMaxLength(160); });
            b.Entity<Location>(e => { e.ToTable("Locations"); e.Property(x => x.Code).HasMaxLength(30); e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique(); });
            b.Entity<Department>(e => e.ToTable("Departments"));
            b.Entity<StatusLabel>(e => e.ToTable("StatusLabels"));
            b.Entity<AssetModel>(e =>
            {
                e.ToTable("AssetModels");
                e.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.Manufacturer).WithMany().HasForeignKey(x => x.ManufacturerId).OnDelete(DeleteBehavior.Restrict);
            });
            b.Entity<Asset>(e =>
            {
                e.ToTable("Assets");
                e.Property(x => x.AssetTag).HasMaxLength(40);
                e.HasIndex(x => new { x.TenantId, x.AssetTag }).IsUnique();
                e.HasIndex(x => new { x.TenantId, x.AssignedToEmpCode });
                e.Property(x => x.PurchaseCost).HasPrecision(18, 2);
                e.HasOne(x => x.Model).WithMany().HasForeignKey(x => x.ModelId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.StatusLabel).WithMany().HasForeignKey(x => x.StatusLabelId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.Department).WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.Supplier).WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
                e.Ignore(x => x.IsCheckedOut);
                e.Ignore(x => x.WarrantyExpires);
            });
            b.Entity<Maintenance>(e =>
            {
                e.ToTable("Maintenances");
                e.Property(x => x.Cost).HasPrecision(18, 2);
                e.HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.Supplier).WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
            });
            b.Entity<License>(e =>
            {
                e.ToTable("Licenses");
                e.Property(x => x.PurchaseCost).HasPrecision(18, 2);
                e.HasMany(x => x.SeatAssignments).WithOne(x => x.License).HasForeignKey(x => x.LicenseId).OnDelete(DeleteBehavior.Cascade);
                e.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.Manufacturer).WithMany().HasForeignKey(x => x.ManufacturerId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.Supplier).WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
            });
            b.Entity<LicenseSeat>(e => { e.ToTable("LicenseSeats"); e.HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict); });

            void Stock<T>(string table) where T : StockItem => b.Entity<T>(e =>
            {
                e.ToTable(table);
                e.Property(x => x.UnitCost).HasPrecision(18, 2);
                e.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.Manufacturer).WithMany().HasForeignKey(x => x.ManufacturerId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
            });
            Stock<Accessory>("Accessories");
            Stock<Consumable>("Consumables");
            Stock<Component>("Components");
            b.Entity<Accessory>().HasMany(x => x.Checkouts).WithOne(x => x.Accessory).HasForeignKey(x => x.AccessoryId).OnDelete(DeleteBehavior.Cascade);
            b.Entity<AccessoryCheckout>().ToTable("AccessoryCheckouts");
            b.Entity<Consumable>().HasMany(x => x.Issues).WithOne(x => x.Consumable).HasForeignKey(x => x.ConsumableId).OnDelete(DeleteBehavior.Cascade);
            b.Entity<ConsumableIssue>().ToTable("ConsumableIssues");
            b.Entity<Component>().HasMany(x => x.Installs).WithOne(x => x.Component).HasForeignKey(x => x.ComponentId).OnDelete(DeleteBehavior.Cascade);
            b.Entity<ComponentInstall>(e => { e.ToTable("ComponentInstalls"); e.HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict); });
            b.Entity<AssetRequest>(e => { e.ToTable("AssetRequests"); e.HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict); });
            b.Entity<HistoryEntry>(e => { e.ToTable("History"); e.HasIndex(x => new { x.TenantId, x.At }); e.HasIndex(x => new { x.ItemType, x.ItemId }); });

            // tenant isolation on every entity
            b.Entity<Category>().HasQueryFilter(x => x.TenantId == TenantId);
            b.Entity<Manufacturer>().HasQueryFilter(x => x.TenantId == TenantId);
            b.Entity<Supplier>().HasQueryFilter(x => x.TenantId == TenantId);
            b.Entity<Location>().HasQueryFilter(x => x.TenantId == TenantId);
            b.Entity<Department>().HasQueryFilter(x => x.TenantId == TenantId);
            b.Entity<StatusLabel>().HasQueryFilter(x => x.TenantId == TenantId);
            b.Entity<AssetModel>().HasQueryFilter(x => x.TenantId == TenantId);
            b.Entity<Asset>().HasQueryFilter(x => x.TenantId == TenantId);
            b.Entity<Maintenance>().HasQueryFilter(x => x.TenantId == TenantId);
            b.Entity<License>().HasQueryFilter(x => x.TenantId == TenantId);
            b.Entity<LicenseSeat>().HasQueryFilter(x => x.TenantId == TenantId);
            b.Entity<Accessory>().HasQueryFilter(x => x.TenantId == TenantId);
            b.Entity<AccessoryCheckout>().HasQueryFilter(x => x.TenantId == TenantId);
            b.Entity<Consumable>().HasQueryFilter(x => x.TenantId == TenantId);
            b.Entity<ConsumableIssue>().HasQueryFilter(x => x.TenantId == TenantId);
            b.Entity<Component>().HasQueryFilter(x => x.TenantId == TenantId);
            b.Entity<ComponentInstall>().HasQueryFilter(x => x.TenantId == TenantId);
            b.Entity<AssetRequest>().HasQueryFilter(x => x.TenantId == TenantId);
            b.Entity<HistoryEntry>().HasQueryFilter(x => x.TenantId == TenantId);
        }

        public override Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            foreach (var entry in ChangeTracker.Entries<TenantEntity>())
            {
                if (entry.State == EntityState.Added && entry.Entity.TenantId == Guid.Empty) entry.Entity.TenantId = TenantId;
                if (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted && entry.Entity.TenantId != TenantId)
                    throw new InvalidOperationException("Cross-tenant write blocked.");
            }
            return base.SaveChangesAsync(ct);
        }
    }

    public class AmsDbContextFactory : IAmsDbContextFactory
    {
        private readonly DbContextOptions<AmsDbContext> _options;
        public AmsDbContextFactory(DbContextOptions<AmsDbContext> options) => _options = options;

        public IAmsDbContext Create(Guid tenantId) => new AmsDbContext(_options, tenantId);
        public AmsDbContext CreateContext(Guid tenantId) => new(_options, tenantId);
    }
}

namespace AMS.Core.Entities
{
    // Asset Management System data model (modelled on Snipe-IT). Every record belongs to a UMS tenant.

    public abstract class TenantEntity
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid TenantId { get; set; }
    }

    public enum CategoryKind { Asset = 1, License = 2, Accessory = 3, Consumable = 4, Component = 5 }

    /// <summary>Deployable = ready to issue · Pending = not ready yet (e.g. in repair) · Undeployable = broken/lost · Archived = retired.</summary>
    public enum StatusType { Deployable = 1, Pending = 2, Undeployable = 3, Archived = 4 }

    public enum MaintenanceType { Maintenance = 1, Repair = 2, Upgrade = 3, Calibration = 4, Inspection = 5 }

    public enum RequestStatus { Pending = 1, Approved = 2, Rejected = 3, Cancelled = 4 }

    public enum ItemType { Asset = 1, License = 2, Accessory = 3, Consumable = 4, Component = 5, Request = 6, Setup = 7 }

    public enum HistoryAction { Created = 1, Updated = 2, Deleted = 3, CheckedOut = 4, CheckedIn = 5, Audited = 6, Maintenance = 7, Requested = 8, Approved = 9, Rejected = 10, Cancelled = 11 }

    public class Category : TenantEntity
    {
        public string Name { get; set; } = "";
        public CategoryKind Kind { get; set; } = CategoryKind.Asset;
        /// <summary>Straight-line depreciation period for assets of this category (0 = not depreciated).</summary>
        public int DepreciationMonths { get; set; }
    }

    public class Manufacturer : TenantEntity
    {
        public string Name { get; set; } = "";
        public string? Url { get; set; }
        public string? SupportEmail { get; set; }
        public string? SupportPhone { get; set; }
    }

    public class Supplier : TenantEntity
    {
        public string Name { get; set; } = "";
        public string? ContactName { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
    }

    /// <summary>Store, DC or office. Code matches UMS user locations (e.g. S045, DC-NR1, HO-GGN) so UMS role scopes apply.</summary>
    public class Location : TenantEntity
    {
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public string Region { get; set; } = "";
        public string? Address { get; set; }
    }

    public class Department : TenantEntity
    {
        public string Name { get; set; } = "";
    }

    public class StatusLabel : TenantEntity
    {
        public string Name { get; set; } = "";
        public StatusType Type { get; set; } = StatusType.Deployable;
    }

    public class AssetModel : TenantEntity
    {
        public string Name { get; set; } = "";
        public string? ModelNumber { get; set; }
        public Guid CategoryId { get; set; }
        public Category? Category { get; set; }
        public Guid ManufacturerId { get; set; }
        public Manufacturer? Manufacturer { get; set; }
        /// <summary>End of life in months after purchase (0 = none).</summary>
        public int EolMonths { get; set; }
    }

    public class Asset : TenantEntity
    {
        public string AssetTag { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Serial { get; set; }
        public Guid ModelId { get; set; }
        public AssetModel? Model { get; set; }
        public Guid StatusLabelId { get; set; }
        public StatusLabel? StatusLabel { get; set; }
        public Guid LocationId { get; set; }
        public Location? Location { get; set; }
        public Guid? DepartmentId { get; set; }
        public Department? Department { get; set; }
        public Guid? SupplierId { get; set; }
        public Supplier? Supplier { get; set; }
        public DateOnly? PurchaseDate { get; set; }
        public decimal PurchaseCost { get; set; }
        public string? OrderNumber { get; set; }
        public int WarrantyMonths { get; set; }
        public bool Requestable { get; set; }
        public string? Notes { get; set; }

        // current holder (a UMS user); null = in stock at Location
        public string? AssignedToEmpCode { get; set; }
        public string? AssignedToName { get; set; }
        public DateTime? AssignedAt { get; set; }
        public DateOnly? ExpectedCheckin { get; set; }

        public DateOnly? LastAuditDate { get; set; }
        public DateOnly? NextAuditDate { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public bool IsCheckedOut => AssignedToEmpCode != null;
        public DateOnly? WarrantyExpires => PurchaseDate?.AddMonths(WarrantyMonths);
    }

    public class Maintenance : TenantEntity
    {
        public Guid AssetId { get; set; }
        public Asset? Asset { get; set; }
        public MaintenanceType Type { get; set; } = MaintenanceType.Maintenance;
        public string Title { get; set; } = "";
        public Guid? SupplierId { get; set; }
        public Supplier? Supplier { get; set; }
        public DateOnly StartDate { get; set; }
        public DateOnly? CompletionDate { get; set; }
        public decimal Cost { get; set; }
        public bool IsWarranty { get; set; }
        public string? Notes { get; set; }
    }

    public class License : TenantEntity
    {
        public string Name { get; set; } = "";
        public string? ProductKey { get; set; }
        public int Seats { get; set; } = 1;
        public Guid? ManufacturerId { get; set; }
        public Manufacturer? Manufacturer { get; set; }
        public Guid? CategoryId { get; set; }
        public Category? Category { get; set; }
        public Guid? SupplierId { get; set; }
        public Supplier? Supplier { get; set; }
        public DateOnly? PurchaseDate { get; set; }
        public decimal PurchaseCost { get; set; }
        public DateOnly? ExpirationDate { get; set; }
        public string? Notes { get; set; }
        public List<LicenseSeat> SeatAssignments { get; set; } = new();
    }

    /// <summary>One used seat of a license, given to a person and/or installed on an asset.</summary>
    public class LicenseSeat : TenantEntity
    {
        public Guid LicenseId { get; set; }
        public License? License { get; set; }
        public string? AssignedToEmpCode { get; set; }
        public string? AssignedToName { get; set; }
        public Guid? AssetId { get; set; }
        public Asset? Asset { get; set; }
        public DateTime AssignedAt { get; set; }
    }

    /// <summary>Common shape of quantity-tracked items (accessories, consumables, components).</summary>
    public abstract class StockItem : TenantEntity
    {
        public string Name { get; set; } = "";
        public Guid? CategoryId { get; set; }
        public Category? Category { get; set; }
        public Guid? ManufacturerId { get; set; }
        public Manufacturer? Manufacturer { get; set; }
        public Guid LocationId { get; set; }
        public Location? Location { get; set; }
        public string? ModelNumber { get; set; }
        /// <summary>Total quantity owned (including what is checked out / installed).</summary>
        public int Quantity { get; set; }
        public int MinQuantity { get; set; }
        public decimal UnitCost { get; set; }
        public string? Notes { get; set; }
    }

    public class Accessory : StockItem
    {
        public List<AccessoryCheckout> Checkouts { get; set; } = new();
    }

    public class AccessoryCheckout : TenantEntity
    {
        public Guid AccessoryId { get; set; }
        public Accessory? Accessory { get; set; }
        public string AssignedToEmpCode { get; set; } = "";
        public string AssignedToName { get; set; } = "";
        public DateTime AssignedAt { get; set; }
    }

    public class Consumable : StockItem
    {
        public List<ConsumableIssue> Issues { get; set; } = new();
    }

    /// <summary>Consumables are used up: an issue permanently reduces stock.</summary>
    public class ConsumableIssue : TenantEntity
    {
        public Guid ConsumableId { get; set; }
        public Consumable? Consumable { get; set; }
        public string IssuedToEmpCode { get; set; } = "";
        public string IssuedToName { get; set; } = "";
        public int Quantity { get; set; }
        public DateTime IssuedAt { get; set; }
    }

    public class Component : StockItem
    {
        public string? Serial { get; set; }
        public List<ComponentInstall> Installs { get; set; } = new();
    }

    /// <summary>Components (RAM, disks…) installed into an asset.</summary>
    public class ComponentInstall : TenantEntity
    {
        public Guid ComponentId { get; set; }
        public Component? Component { get; set; }
        public Guid AssetId { get; set; }
        public Asset? Asset { get; set; }
        public int Quantity { get; set; }
        public DateTime InstalledAt { get; set; }
    }

    public class AssetRequest : TenantEntity
    {
        public Guid AssetId { get; set; }
        public Asset? Asset { get; set; }
        public string RequestedByEmpCode { get; set; } = "";
        public string RequestedByName { get; set; } = "";
        public string Reason { get; set; } = "";
        public RequestStatus Status { get; set; } = RequestStatus.Pending;
        public DateTime RequestedAt { get; set; }
        public string? DecidedByEmpCode { get; set; }
        public string? DecidedByName { get; set; }
        public DateTime? DecidedAt { get; set; }
        public string? DecisionNote { get; set; }
    }

    /// <summary>Activity log of everything done in AMS (who, what, to whom).</summary>
    public class HistoryEntry : TenantEntity
    {
        public DateTime At { get; set; }
        public string ActorEmpCode { get; set; } = "";
        public string ActorName { get; set; } = "";
        public ItemType ItemType { get; set; }
        public Guid ItemId { get; set; }
        public string ItemName { get; set; } = "";
        /// <summary>Location code of the item at the time (lets UMS scope the activity report like the items themselves).</summary>
        public string? LocationCode { get; set; }
        public HistoryAction Action { get; set; }
        public string? Target { get; set; }
        public string? Note { get; set; }
    }
}

using AMS.Domain.Enums;

namespace AMS.Domain.Models
{
    // Read models returned by the stored procedures (column names = property names) and sent to the web portal.

    public class AssetView
    {
        public int Id { get; set; }
        public string AssetTag { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Serial { get; set; }
        public int ModelId { get; set; }
        public string ModelName { get; set; } = "";
        public string? ModelNumber { get; set; }
        public int CategoryId { get; set; }
        public string CategoryName { get; set; } = "";
        public int DepreciationMonths { get; set; }
        public int ManufacturerId { get; set; }
        public string ManufacturerName { get; set; } = "";
        public int StatusLabelId { get; set; }
        public string StatusName { get; set; } = "";
        public StatusType StatusType { get; set; }
        public int LocationId { get; set; }
        public string LocationCode { get; set; } = "";
        public string LocationName { get; set; } = "";
        public string Region { get; set; } = "";
        public int? DepartmentId { get; set; }
        public string? DepartmentName { get; set; }
        public int? SupplierId { get; set; }
        public string? SupplierName { get; set; }
        public DateTime? PurchaseDate { get; set; }
        public decimal PurchaseCost { get; set; }
        public string? OrderNumber { get; set; }
        public int WarrantyMonths { get; set; }
        public bool Requestable { get; set; }
        public string? Notes { get; set; }
        public string? AssignedToEmpCode { get; set; }
        public string? AssignedToName { get; set; }
        public string? AssignedToEmail { get; set; }
        public DateTime? AssignedAt { get; set; }
        public DateTime? ExpectedCheckin { get; set; }
        public DateTime? LastAuditDate { get; set; }
        public DateTime? NextAuditDate { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public bool IsCheckedOut => AssignedToEmpCode != null;
        public DateTime? WarrantyExpires => PurchaseDate?.AddMonths(WarrantyMonths);
        public string Display => $"{AssetTag} · {Name}";
    }

    /// <summary>Any setup record (category, manufacturer, model, supplier, location, department, status label); each kind fills its own fields.</summary>
    public class SetupItem
    {
        public int Id { get; set; }
        public SetupKind Kind { get; set; }
        public string Name { get; set; } = "";
        public string? Code { get; set; }
        public string? Region { get; set; }
        public string? Address { get; set; }
        public CategoryKind? CategoryKind { get; set; }
        public int DepreciationMonths { get; set; }
        public StatusType? StatusType { get; set; }
        public string? Url { get; set; }
        public string? ContactName { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? ModelNumber { get; set; }
        public int? CategoryId { get; set; }
        public string? CategoryName { get; set; }
        public int? ManufacturerId { get; set; }
        public string? ManufacturerName { get; set; }
        public int EolMonths { get; set; }
        /// <summary>Records that use it (it cannot be deleted while in use).</summary>
        public int UsedBy { get; set; }
        public bool CanEdit { get; set; }
        public bool CanDelete { get; set; }

        public SetupItem Clone() => (SetupItem)MemberwiseClone();
    }

    public class LicenseView
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string? ProductKey { get; set; }
        public int Seats { get; set; }
        public int? ManufacturerId { get; set; }
        public string? ManufacturerName { get; set; }
        public int? CategoryId { get; set; }
        public string? CategoryName { get; set; }
        public int? SupplierId { get; set; }
        public string? SupplierName { get; set; }
        public DateTime? PurchaseDate { get; set; }
        public decimal PurchaseCost { get; set; }
        public DateTime? ExpirationDate { get; set; }
        public string? Notes { get; set; }
        public int Used { get; set; }
        public int Free => Seats - Used;
    }

    public class LicenseSeatView
    {
        public int Id { get; set; }
        public int LicenseId { get; set; }
        public string LicenseName { get; set; } = "";
        public string? AssignedToEmpCode { get; set; }
        public string? AssignedToName { get; set; }
        public int? AssetId { get; set; }
        public string? AssetTag { get; set; }
        public string? AssetName { get; set; }
        public string? LocationCode { get; set; }
        public DateTime AssignedAt { get; set; }
    }

    public class StockItemView
    {
        public int Id { get; set; }
        public StockKind Kind { get; set; }
        public string Name { get; set; } = "";
        public int? CategoryId { get; set; }
        public string? CategoryName { get; set; }
        public int? ManufacturerId { get; set; }
        public string? ManufacturerName { get; set; }
        public int LocationId { get; set; }
        public string LocationCode { get; set; } = "";
        public string LocationName { get; set; } = "";
        public string Region { get; set; } = "";
        public string? ModelNumber { get; set; }
        public string? Serial { get; set; }
        /// <summary>Total owned; for consumables the quantity still in stock.</summary>
        public int Quantity { get; set; }
        public int MinQuantity { get; set; }
        public decimal UnitCost { get; set; }
        public string? Notes { get; set; }
        /// <summary>Units lent out / installed (always 0 for consumables, which are used up).</summary>
        public int InUse { get; set; }
        public int Available => Quantity - InUse;
        public bool Low => Available <= MinQuantity;
    }

    /// <summary>An accessory with a person, a consumable issued to a person, or a component installed in an asset.</summary>
    public class StockAssignmentView
    {
        public int Id { get; set; }
        public int StockItemId { get; set; }
        public string ItemName { get; set; } = "";
        public StockKind Kind { get; set; }
        public string? EmpCode { get; set; }
        public string? EmpName { get; set; }
        public int? AssetId { get; set; }
        public string? AssetTag { get; set; }
        public string? AssetName { get; set; }
        public int Quantity { get; set; }
        public DateTime AssignedAt { get; set; }
    }

    public class MaintenanceView
    {
        public int Id { get; set; }
        public int AssetId { get; set; }
        public MaintenanceType MaintenanceType { get; set; }
        public string Title { get; set; } = "";
        public int? SupplierId { get; set; }
        public string? SupplierName { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime? CompletionDate { get; set; }
        public decimal Cost { get; set; }
        public bool IsWarranty { get; set; }
        public string? Notes { get; set; }
        public string AssetTag { get; set; } = "";
        public string AssetName { get; set; } = "";
        public decimal AssetCost { get; set; }
        public string LocationCode { get; set; } = "";
        public string Region { get; set; } = "";
        public string CategoryName { get; set; } = "";
        public string StatusName { get; set; } = "";
        public StatusType StatusType { get; set; }
        public string? DepartmentName { get; set; }
        public string? AssignedToEmpCode { get; set; }
    }

    public class RequestView
    {
        public int Id { get; set; }
        public int AssetId { get; set; }
        public string RequestedByEmpCode { get; set; } = "";
        public string RequestedByName { get; set; } = "";
        public string? RequestedByEmail { get; set; }
        public string Reason { get; set; } = "";
        public RequestStatus Status { get; set; }
        public DateTime RequestedAt { get; set; }
        public string? DecidedByEmpCode { get; set; }
        public string? DecidedByName { get; set; }
        public DateTime? DecidedAt { get; set; }
        public string? DecisionNote { get; set; }
        public string AssetTag { get; set; } = "";
        public string AssetName { get; set; } = "";
        public decimal AssetCost { get; set; }
        public string LocationCode { get; set; } = "";
        public string Region { get; set; } = "";
        public string CategoryName { get; set; } = "";
        public string StatusName { get; set; } = "";
        public StatusType StatusType { get; set; }
        public string? DepartmentName { get; set; }
        public bool Requestable { get; set; }
    }

    public class HistoryEntry
    {
        public long Id { get; set; }
        public DateTime At { get; set; }
        public string ActorEmpCode { get; set; } = "";
        public string ActorName { get; set; } = "";
        public ItemType ItemType { get; set; }
        public int ItemId { get; set; }
        public string ItemName { get; set; } = "";
        public string? LocationCode { get; set; }
        public HistoryAction Action { get; set; }
        public string? Target { get; set; }
        public string? Note { get; set; }
    }

    /// <summary>A colleague from the UMS directory.</summary>
    public sealed record Person(string EmpCode, string FullName, string Email, string Department, string Designation, string Location, string Region,
        string EmploymentType, string Status)
    {
        public string Display => $"{FullName} ({EmpCode})";
    }

    public sealed record CountRow(string Label, int Count, decimal Value);
}

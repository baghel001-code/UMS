using AMS.Domain.Enums;
using AMS.Domain.Models;

namespace AMS.Domain.Requests
{
    // Request bodies of the AMS API (POST api/v1/{action}). Who is acting always comes from the UMS access token, never from the body.

    public class IdReq
    {
        public int Id { get; set; }
    }

    public class AssetFilterReq
    {
        public string? Search { get; set; }
        public int? CategoryId { get; set; }
        public int? LocationId { get; set; }
        public int? StatusLabelId { get; set; }
        /// <summary>null = all, true = checked out, false = in stock.</summary>
        public bool? CheckedOut { get; set; }
    }

    public class AssetSaveReq
    {
        public int Id { get; set; }
        public string AssetTag { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Serial { get; set; }
        public int ModelId { get; set; }
        public int StatusLabelId { get; set; }
        public int LocationId { get; set; }
        public int? DepartmentId { get; set; }
        public int? SupplierId { get; set; }
        public DateTime? PurchaseDate { get; set; }
        public decimal PurchaseCost { get; set; }
        public string? OrderNumber { get; set; }
        public int WarrantyMonths { get; set; }
        public bool Requestable { get; set; }
        public string? Notes { get; set; }

        public static AssetSaveReq From(AssetView a) => new()
        {
            Id = a.Id, AssetTag = a.AssetTag, Name = a.Name, Serial = a.Serial, ModelId = a.ModelId, StatusLabelId = a.StatusLabelId, LocationId = a.LocationId,
            DepartmentId = a.DepartmentId, SupplierId = a.SupplierId, PurchaseDate = a.PurchaseDate, PurchaseCost = a.PurchaseCost, OrderNumber = a.OrderNumber,
            WarrantyMonths = a.WarrantyMonths, Requestable = a.Requestable, Notes = a.Notes
        };
    }

    public class AssetCheckoutReq
    {
        public int Id { get; set; }
        public string EmpCode { get; set; } = "";
        public DateTime? ExpectedCheckin { get; set; }
        public string? Note { get; set; }
    }

    public class AssetCheckinReq
    {
        public int Id { get; set; }
        public int? StatusLabelId { get; set; }
        public string? Note { get; set; }
    }

    public class AssetAuditReq
    {
        public int Id { get; set; }
        public DateTime? NextAuditDate { get; set; }
        public string? Note { get; set; }
    }

    public class MaintenanceListReq
    {
        public bool OpenOnly { get; set; }
    }

    public class MaintenanceSaveReq
    {
        public int Id { get; set; }
        public int AssetId { get; set; }
        public MaintenanceType MaintenanceType { get; set; } = MaintenanceType.Maintenance;
        public string Title { get; set; } = "";
        public int? SupplierId { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? CompletionDate { get; set; }
        public decimal Cost { get; set; }
        public bool IsWarranty { get; set; }
        public string? Notes { get; set; }

        public static MaintenanceSaveReq From(MaintenanceView m) => new()
        {
            Id = m.Id, AssetId = m.AssetId, MaintenanceType = m.MaintenanceType, Title = m.Title, SupplierId = m.SupplierId, StartDate = m.StartDate,
            CompletionDate = m.CompletionDate, Cost = m.Cost, IsWarranty = m.IsWarranty, Notes = m.Notes
        };
    }

    public class RequestListReq
    {
        public RequestStatus? Status { get; set; }
    }

    public class RequestCreateReq
    {
        public int AssetId { get; set; }
        public string Reason { get; set; } = "";
    }

    public class RequestDecideReq
    {
        public int Id { get; set; }
        public bool Approve { get; set; }
        public string? Note { get; set; }
    }

    public class SearchReq
    {
        public string? Search { get; set; }
    }

    public class LicenseSaveReq
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string? ProductKey { get; set; }
        public int Seats { get; set; } = 1;
        public int? ManufacturerId { get; set; }
        public int? CategoryId { get; set; }
        public int? SupplierId { get; set; }
        public DateTime? PurchaseDate { get; set; }
        public decimal PurchaseCost { get; set; }
        public DateTime? ExpirationDate { get; set; }
        public string? Notes { get; set; }

        public static LicenseSaveReq From(LicenseView l) => new()
        {
            Id = l.Id, Name = l.Name, ProductKey = l.ProductKey, Seats = l.Seats, ManufacturerId = l.ManufacturerId, CategoryId = l.CategoryId,
            SupplierId = l.SupplierId, PurchaseDate = l.PurchaseDate, PurchaseCost = l.PurchaseCost, ExpirationDate = l.ExpirationDate, Notes = l.Notes
        };
    }

    public class LicenseCheckoutReq
    {
        public int LicenseId { get; set; }
        public string? EmpCode { get; set; }
        public int? AssetId { get; set; }
        public string? Note { get; set; }
    }

    public class LicenseCheckinReq
    {
        public int SeatId { get; set; }
        public string? Note { get; set; }
    }

    public class StockListReq
    {
        public StockKind Kind { get; set; }
        public string? Search { get; set; }
    }

    public class StockSaveReq
    {
        public int Id { get; set; }
        public StockKind Kind { get; set; }
        public string Name { get; set; } = "";
        public int? CategoryId { get; set; }
        public int? ManufacturerId { get; set; }
        public int LocationId { get; set; }
        public string? ModelNumber { get; set; }
        public string? Serial { get; set; }
        public int Quantity { get; set; } = 1;
        public int MinQuantity { get; set; }
        public decimal UnitCost { get; set; }
        public string? Notes { get; set; }

        public static StockSaveReq From(StockItemView s) => new()
        {
            Id = s.Id, Kind = s.Kind, Name = s.Name, CategoryId = s.CategoryId, ManufacturerId = s.ManufacturerId, LocationId = s.LocationId,
            ModelNumber = s.ModelNumber, Serial = s.Serial, Quantity = s.Quantity, MinQuantity = s.MinQuantity, UnitCost = s.UnitCost, Notes = s.Notes
        };
    }

    public class StockCheckoutReq
    {
        public int StockItemId { get; set; }
        public string? EmpCode { get; set; }
        public int? AssetId { get; set; }
        public int Quantity { get; set; } = 1;
        public string? Note { get; set; }
    }

    public class StockCheckinReq
    {
        public int StockItemId { get; set; }
        public int AssignmentId { get; set; }
        public string? Note { get; set; }
    }

    public class SetupListReq
    {
        public SetupKind Kind { get; set; }
    }

    public class SetupDeleteReq
    {
        public SetupKind Kind { get; set; }
        public int Id { get; set; }
    }

    public class ActivityReq
    {
        public ItemType? ItemType { get; set; }
        public string? Search { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
    }
}

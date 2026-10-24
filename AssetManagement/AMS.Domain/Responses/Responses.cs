using AMS.Domain.Base;
using AMS.Domain.Models;

namespace AMS.Domain.Responses
{
    // Responses of the AMS API. Rows carry what UMS allows the caller to do with each record (asked in one batch), so the portal
    // shows only permitted actions; the API asks UMS again when an action is actually performed.

    public sealed record AssetRow(AssetView Asset, bool CanEdit, bool CanDelete, bool CanCheckout, bool CanCheckin, bool CanAudit);

    public sealed class AssetListRes : BaseResponse
    {
        public List<AssetRow> Rows { get; set; } = new();
    }

    public sealed class AssetsRes : BaseResponse
    {
        public List<AssetView> Assets { get; set; } = new();
    }

    public sealed class AssetDetailRes : BaseResponse
    {
        public AssetRow? Asset { get; set; }
        public List<HistoryEntry> History { get; set; } = new();
        public List<MaintenanceView> Maintenances { get; set; } = new();
        public List<LicenseSeatView> Licenses { get; set; } = new();
        public List<StockAssignmentView> Components { get; set; } = new();
        public bool CanMaintain { get; set; }
        public decimal CurrentValue { get; set; }
    }

    public sealed class OptionsRes : BaseResponse
    {
        public List<SetupItem> Categories { get; set; } = new();
        public List<SetupItem> Manufacturers { get; set; } = new();
        public List<SetupItem> Models { get; set; } = new();
        public List<SetupItem> Suppliers { get; set; } = new();
        public List<SetupItem> Locations { get; set; } = new();
        public List<SetupItem> Departments { get; set; } = new();
        public List<SetupItem> Statuses { get; set; } = new();
    }

    public sealed class SetupListRes : BaseResponse
    {
        public List<SetupItem> Items { get; set; } = new();
    }

    public sealed record LicenseRow(LicenseView License, bool CanEdit, bool CanDelete, bool CanCheckout, bool CanCheckin);

    public sealed class LicenseListRes : BaseResponse
    {
        public List<LicenseRow> Rows { get; set; } = new();
    }

    /// <summary>A licence seat, lent accessory, issued consumable or installed component, with whether the caller may check it in.</summary>
    public sealed record HolderRow(int Id, string Holder, int Quantity, DateTime Since, bool CanCheckin);

    public sealed class HoldersRes : BaseResponse
    {
        public List<HolderRow> Holders { get; set; } = new();
    }

    public sealed record StockRow(StockItemView Item, bool CanEdit, bool CanDelete, bool CanCheckout, bool CanCheckin);

    public sealed class StockListRes : BaseResponse
    {
        public List<StockRow> Rows { get; set; } = new();
    }

    public sealed record MaintenanceRow(MaintenanceView Item, bool CanEdit, bool CanDelete);

    public sealed class MaintenanceListRes : BaseResponse
    {
        public List<MaintenanceRow> Rows { get; set; } = new();
    }

    public sealed record RequestRow(RequestView Request, bool CanApprove, bool CanCancel);

    public sealed class RequestListRes : BaseResponse
    {
        public List<RequestRow> Rows { get; set; } = new();
    }

    public sealed class MyItemsRes : BaseResponse
    {
        public List<AssetView> Assets { get; set; } = new();
        public List<LicenseSeatView> Licenses { get; set; } = new();
        public List<StockAssignmentView> Accessories { get; set; } = new();
        public List<StockAssignmentView> Consumables { get; set; } = new();
    }

    public sealed class PeopleRes : BaseResponse
    {
        public List<Person> People { get; set; } = new();
    }

    public sealed class ActivityRes : BaseResponse
    {
        public List<HistoryEntry> Entries { get; set; } = new();
    }

    /// <summary>A file produced by the API (CSV exports).</summary>
    public sealed class FileRes : BaseResponse
    {
        public string FileName { get; set; } = "";
        public string ContentType { get; set; } = "text/csv";
        public string Content { get; set; } = "";
    }

    /// <summary>Type-level permissions of the caller ("AMS.ASSETS.ASSET.CREATE"…), used by the portal to build its menu and buttons.</summary>
    public sealed class CapabilitiesRes : BaseResponse
    {
        public List<string> Allowed { get; set; } = new();

        public bool Can(string module, string resource, string action) =>
            Allowed.Contains(Domain.Security.Perm.Key(module, resource, action), StringComparer.OrdinalIgnoreCase);
    }

    public sealed class DashboardRes : BaseResponse
    {
        public int Assets { get; set; }
        public int CheckedOut { get; set; }
        public int ReadyToDeploy { get; set; }
        public int NotDeployable { get; set; }
        public decimal TotalCost { get; set; }
        public int LicenseSeats { get; set; }
        public int LicenseSeatsUsed { get; set; }
        public int LowStock { get; set; }
        public int PendingRequests { get; set; }
        public int MaintenanceOpen { get; set; }
        public List<CountRow> ByCategory { get; set; } = new();
        public List<CountRow> ByStatus { get; set; } = new();
        public List<CountRow> ByLocation { get; set; } = new();
        public List<AssetView> AuditDue { get; set; } = new();
        public List<AssetView> OverdueCheckins { get; set; } = new();
        public List<AssetView> WarrantyExpiring { get; set; } = new();
        public List<HistoryEntry> Recent { get; set; } = new();
        public bool ShowLicenses { get; set; }
        public bool ShowStock { get; set; }
        public bool ShowRequests { get; set; }
        public bool ShowActivity { get; set; }
    }

    public sealed record DepreciationRow(AssetView Asset, int Months, decimal CurrentValue)
    {
        public decimal Depreciated => Asset.PurchaseCost - CurrentValue;
    }

    public sealed class AssetReportRes : BaseResponse
    {
        public List<CountRow> ByCategory { get; set; } = new();
        public List<CountRow> ByLocation { get; set; } = new();
        public List<CountRow> ByStatus { get; set; } = new();
        public List<DepreciationRow> Depreciation { get; set; } = new();
        public List<AssetView> WarrantyExpiring { get; set; } = new();
        public List<AssetView> AuditDue { get; set; } = new();
        public List<AssetView> OverdueCheckins { get; set; } = new();
        public decimal TotalCost => Depreciation.Sum(d => d.Asset.PurchaseCost);
        public decimal TotalValue => Depreciation.Sum(d => d.CurrentValue);
    }
}

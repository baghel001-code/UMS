using AMS.Domain.Enums;
using AMS.Domain.Models;
using AMS.Domain.Requests;

namespace AMS.Application.Interfaces
{
    // Persistence ports, implemented in AMS.Infrastructure with Dapper over the stored procedures (database/002_ams_procedures.sql).
    // Writes return the procedure's @Error_Code/@Error_Message; reads are already filtered to the tenant.

    /// <summary>Who writes, for whom and when (business wall-clock time) - passed to every write procedure.</summary>
    public sealed record WriteContext(Guid TenantId, DateTime Now, string ActorEmpCode, string ActorName);

    /// <summary>Result of a write procedure. 0 = success.</summary>
    public sealed record DbResult(int Code, string Message, int RecordId = 0)
    {
        public bool Ok => Code == 0;
        public static DbResult Success(int id = 0) => new(0, "SUCCESS", id);
    }

    public interface ISetupRepository
    {
        Task<List<SetupItem>> ListAsync(Guid tenantId, SetupKind kind, CancellationToken ct = default);
        Task<SetupLists> OptionsAsync(Guid tenantId, CancellationToken ct = default);
        Task<DbResult> SaveAsync(WriteContext w, SetupItem item, CancellationToken ct = default);
        Task<DbResult> DeleteAsync(WriteContext w, SetupKind kind, int id, CancellationToken ct = default);
    }

    public sealed record SetupLists(List<SetupItem> Categories, List<SetupItem> Manufacturers, List<SetupItem> Models, List<SetupItem> Suppliers,
        List<SetupItem> Locations, List<SetupItem> Departments, List<SetupItem> Statuses);

    public interface IAssetRepository
    {
        Task<List<AssetView>> ListAsync(Guid tenantId, AssetFilterReq filter, bool requestableOnly = false, CancellationToken ct = default);
        Task<AssetView?> GetAsync(Guid tenantId, int id, CancellationToken ct = default);
        Task<DbResult> SaveAsync(WriteContext w, AssetSaveReq asset, CancellationToken ct = default);
        Task<DbResult> DeleteAsync(WriteContext w, int id, CancellationToken ct = default);
        Task<DbResult> CheckoutAsync(WriteContext w, int id, Person to, DateTime? expectedCheckin, string? note, CancellationToken ct = default);
        Task<DbResult> CheckinAsync(WriteContext w, int id, int? statusLabelId, string? note, CancellationToken ct = default);
        Task<DbResult> AuditAsync(WriteContext w, int id, DateTime? nextAudit, string? note, CancellationToken ct = default);
        Task<MyItems> MyItemsAsync(Guid tenantId, string empCode, CancellationToken ct = default);
    }

    public sealed record MyItems(List<AssetView> Assets, List<LicenseSeatView> Licenses, List<StockAssignmentView> Accessories, List<StockAssignmentView> Consumables);

    public interface IMaintenanceRepository
    {
        Task<List<MaintenanceView>> ListAsync(Guid tenantId, bool openOnly, int? assetId = null, int? id = null, CancellationToken ct = default);
        Task<DbResult> SaveAsync(WriteContext w, MaintenanceSaveReq m, CancellationToken ct = default);
        Task<DbResult> DeleteAsync(WriteContext w, int id, CancellationToken ct = default);
    }

    public interface IRequestRepository
    {
        Task<List<RequestView>> ListAsync(Guid tenantId, RequestStatus? status, int? id = null, CancellationToken ct = default);
        Task<DbResult> CreateAsync(WriteContext w, int assetId, string reason, string email, CancellationToken ct = default);
        Task<DbResult> DecideAsync(WriteContext w, int id, bool approve, string? note, CancellationToken ct = default);
        Task<DbResult> CancelAsync(WriteContext w, int id, CancellationToken ct = default);
    }

    public interface ILicenseRepository
    {
        Task<List<LicenseView>> ListAsync(Guid tenantId, string? search, int? id = null, CancellationToken ct = default);
        Task<DbResult> SaveAsync(WriteContext w, LicenseSaveReq l, CancellationToken ct = default);
        Task<DbResult> DeleteAsync(WriteContext w, int id, CancellationToken ct = default);
        Task<List<LicenseSeatView>> SeatsAsync(Guid tenantId, int? licenseId, int? seatId = null, int? assetId = null, CancellationToken ct = default);
        Task<DbResult> CheckoutAsync(WriteContext w, int licenseId, Person? to, int? assetId, string? note, CancellationToken ct = default);
        Task<DbResult> CheckinAsync(WriteContext w, int seatId, string? note, CancellationToken ct = default);
    }

    public interface IStockRepository
    {
        Task<List<StockItemView>> ListAsync(Guid tenantId, StockKind? kind, string? search, int? id = null, CancellationToken ct = default);
        Task<DbResult> SaveAsync(WriteContext w, StockSaveReq s, CancellationToken ct = default);
        Task<DbResult> DeleteAsync(WriteContext w, int id, CancellationToken ct = default);
        Task<List<StockAssignmentView>> HoldersAsync(Guid tenantId, int? stockItemId, int? assetId = null, CancellationToken ct = default);
        Task<DbResult> CheckoutAsync(WriteContext w, int stockItemId, Person? to, int? assetId, int quantity, string? note, CancellationToken ct = default);
        Task<DbResult> CheckinAsync(WriteContext w, int stockItemId, int assignmentId, string? note, CancellationToken ct = default);
    }

    public interface IHistoryRepository
    {
        Task<List<HistoryEntry>> ListAsync(Guid tenantId, ItemType? type, int? itemId, string? search, DateTime? from, DateTime? to, int take, CancellationToken ct = default);
    }
}

using AMS.Application.Interfaces;
using AMS.Domain.Enums;
using AMS.Domain.Models;
using AMS.Domain.Requests;
using Dapper;
using System.Data;

namespace AMS.Infrastructure.Persistence
{
    // Dapper repositories over the stored procedures in database/002_ams_procedures.sql. Every call passes the tenant;
    // enums are passed by name (the schema stores names).

    internal static class P
    {
        public static DynamicParameters Tenant(Guid tenantId)
        {
            var p = new DynamicParameters();
            p.Add("@TenantId", tenantId, DbType.Guid);
            return p;
        }

        public static DynamicParameters Write(WriteContext w)
        {
            var p = Tenant(w.TenantId);
            p.Add("@Now", w.Now, DbType.DateTime2);
            p.Add("@ActorEmpCode", w.ActorEmpCode, DbType.String);
            p.Add("@ActorName", w.ActorName, DbType.String);
            return p;
        }

        public static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        public static DateTime? Date(DateTime? d) => d?.Date;
    }

    public sealed class SetupRepository : ISetupRepository
    {
        private readonly DapperHelper _db;
        public SetupRepository(DapperHelper db) => _db = db;

        public Task<List<SetupItem>> ListAsync(Guid tenantId, SetupKind kind, CancellationToken ct = default)
        {
            var p = P.Tenant(tenantId);
            p.Add("@Kind", kind.ToString());
            return _db.ListAsync<SetupItem>("dbo.PRO_AMS_SETUP_LIST", p);
        }

        public Task<SetupLists> OptionsAsync(Guid tenantId, CancellationToken ct = default) =>
            _db.MultipleAsync("dbo.PRO_AMS_OPTIONS", P.Tenant(tenantId), async g => new SetupLists(
                (await g.ReadAsync<SetupItem>()).ToList(), (await g.ReadAsync<SetupItem>()).ToList(), (await g.ReadAsync<SetupItem>()).ToList(),
                (await g.ReadAsync<SetupItem>()).ToList(), (await g.ReadAsync<SetupItem>()).ToList(), (await g.ReadAsync<SetupItem>()).ToList(),
                (await g.ReadAsync<SetupItem>()).ToList()));

        public Task<DbResult> SaveAsync(WriteContext w, SetupItem x, CancellationToken ct = default)
        {
            var p = P.Write(w);
            p.Add("@Kind", x.Kind.ToString());
            p.Add("@Id", x.Id);
            p.Add("@Name", x.Name);
            p.Add("@Code", P.Clean(x.Code));
            p.Add("@Region", P.Clean(x.Region));
            p.Add("@Address", P.Clean(x.Address));
            p.Add("@CategoryKind", (x.CategoryKind ?? CategoryKind.Asset).ToString());
            p.Add("@DepreciationMonths", x.DepreciationMonths);
            p.Add("@StatusType", (x.StatusType ?? StatusType.Deployable).ToString());
            p.Add("@Url", P.Clean(x.Url));
            p.Add("@ContactName", P.Clean(x.ContactName));
            p.Add("@Email", P.Clean(x.Email));
            p.Add("@Phone", P.Clean(x.Phone));
            p.Add("@ModelNumber", P.Clean(x.ModelNumber));
            p.Add("@CategoryId", x.CategoryId);
            p.Add("@ManufacturerId", x.ManufacturerId);
            p.Add("@EolMonths", x.EolMonths);
            return _db.WriteAsync("dbo.PRO_AMS_SETUP_SAVE", p, returnsRecordId: true);
        }

        public Task<DbResult> DeleteAsync(WriteContext w, SetupKind kind, int id, CancellationToken ct = default)
        {
            var p = P.Write(w);
            p.Add("@Kind", kind.ToString());
            p.Add("@Id", id);
            return _db.WriteAsync("dbo.PRO_AMS_SETUP_DELETE", p);
        }

        public async Task<List<Guid>> TenantsAsync() => await _db.ListAsync<Guid>("dbo.PRO_AMS_TENANTS");
    }

    public sealed class AssetRepository : IAssetRepository
    {
        private readonly DapperHelper _db;
        public AssetRepository(DapperHelper db) => _db = db;

        public Task<List<AssetView>> ListAsync(Guid tenantId, AssetFilterReq f, bool requestableOnly = false, CancellationToken ct = default)
        {
            var p = P.Tenant(tenantId);
            p.Add("@Search", P.Clean(f.Search));
            p.Add("@CategoryId", f.CategoryId);
            p.Add("@LocationId", f.LocationId);
            p.Add("@StatusLabelId", f.StatusLabelId);
            p.Add("@CheckedOut", f.CheckedOut);
            p.Add("@RequestableOnly", requestableOnly);
            return _db.ListAsync<AssetView>("dbo.PRO_AMS_ASSET_LIST", p);
        }

        public async Task<AssetView?> GetAsync(Guid tenantId, int id, CancellationToken ct = default)
        {
            var p = P.Tenant(tenantId);
            p.Add("@Id", id);
            return await _db.SingleAsync<AssetView>("dbo.PRO_AMS_ASSET_LIST", p);
        }

        public Task<DbResult> SaveAsync(WriteContext w, AssetSaveReq a, CancellationToken ct = default)
        {
            var p = P.Write(w);
            p.Add("@Id", a.Id);
            p.Add("@AssetTag", a.AssetTag);
            p.Add("@Name", a.Name);
            p.Add("@Serial", P.Clean(a.Serial));
            p.Add("@ModelId", a.ModelId);
            p.Add("@StatusLabelId", a.StatusLabelId);
            p.Add("@LocationId", a.LocationId);
            p.Add("@DepartmentId", a.DepartmentId);
            p.Add("@SupplierId", a.SupplierId);
            p.Add("@PurchaseDate", P.Date(a.PurchaseDate), DbType.Date);
            p.Add("@PurchaseCost", a.PurchaseCost);
            p.Add("@OrderNumber", P.Clean(a.OrderNumber));
            p.Add("@WarrantyMonths", a.WarrantyMonths);
            p.Add("@Requestable", a.Requestable);
            p.Add("@Notes", P.Clean(a.Notes));
            return _db.WriteAsync("dbo.PRO_AMS_ASSET_SAVE", p, returnsRecordId: true);
        }

        public Task<DbResult> DeleteAsync(WriteContext w, int id, CancellationToken ct = default)
        {
            var p = P.Write(w);
            p.Add("@Id", id);
            return _db.WriteAsync("dbo.PRO_AMS_ASSET_DELETE", p);
        }

        public Task<DbResult> CheckoutAsync(WriteContext w, int id, Person to, DateTime? expectedCheckin, string? note, CancellationToken ct = default)
        {
            var p = P.Write(w);
            p.Add("@Id", id);
            p.Add("@EmpCode", to.EmpCode);
            p.Add("@EmpName", to.FullName);
            p.Add("@EmpEmail", P.Clean(to.Email));
            p.Add("@ExpectedCheckin", P.Date(expectedCheckin), DbType.Date);
            p.Add("@Note", P.Clean(note));
            return _db.WriteAsync("dbo.PRO_AMS_ASSET_CHECKOUT", p);
        }

        public Task<DbResult> CheckinAsync(WriteContext w, int id, int? statusLabelId, string? note, CancellationToken ct = default)
        {
            var p = P.Write(w);
            p.Add("@Id", id);
            p.Add("@StatusLabelId", statusLabelId);
            p.Add("@Note", P.Clean(note));
            return _db.WriteAsync("dbo.PRO_AMS_ASSET_CHECKIN", p);
        }

        public Task<DbResult> AuditAsync(WriteContext w, int id, DateTime? nextAudit, string? note, CancellationToken ct = default)
        {
            var p = P.Write(w);
            p.Add("@Id", id);
            p.Add("@NextAuditDate", P.Date(nextAudit), DbType.Date);
            p.Add("@Note", P.Clean(note));
            return _db.WriteAsync("dbo.PRO_AMS_ASSET_AUDIT", p);
        }

        public Task<MyItems> MyItemsAsync(Guid tenantId, string empCode, CancellationToken ct = default)
        {
            var p = P.Tenant(tenantId);
            p.Add("@EmpCode", empCode);
            return _db.MultipleAsync("dbo.PRO_AMS_MY_ITEMS", p, async g => new MyItems(
                (await g.ReadAsync<AssetView>()).ToList(), (await g.ReadAsync<LicenseSeatView>()).ToList(),
                (await g.ReadAsync<StockAssignmentView>()).ToList(), (await g.ReadAsync<StockAssignmentView>()).ToList()));
        }
    }

    public sealed class MaintenanceRepository : IMaintenanceRepository
    {
        private readonly DapperHelper _db;
        public MaintenanceRepository(DapperHelper db) => _db = db;

        public Task<List<MaintenanceView>> ListAsync(Guid tenantId, bool openOnly, int? assetId = null, int? id = null, CancellationToken ct = default)
        {
            var p = P.Tenant(tenantId);
            p.Add("@OpenOnly", openOnly);
            p.Add("@AssetId", assetId);
            p.Add("@Id", id);
            return _db.ListAsync<MaintenanceView>("dbo.PRO_AMS_MAINTENANCE_LIST", p);
        }

        public Task<DbResult> SaveAsync(WriteContext w, MaintenanceSaveReq m, CancellationToken ct = default)
        {
            var p = P.Write(w);
            p.Add("@Id", m.Id);
            p.Add("@AssetId", m.AssetId);
            p.Add("@MaintenanceType", m.MaintenanceType.ToString());
            p.Add("@Title", m.Title);
            p.Add("@SupplierId", m.SupplierId);
            p.Add("@StartDate", (m.StartDate ?? w.Now).Date, DbType.Date);
            p.Add("@CompletionDate", P.Date(m.CompletionDate), DbType.Date);
            p.Add("@Cost", m.Cost);
            p.Add("@IsWarranty", m.IsWarranty);
            p.Add("@Notes", P.Clean(m.Notes));
            return _db.WriteAsync("dbo.PRO_AMS_MAINTENANCE_SAVE", p, returnsRecordId: true);
        }

        public Task<DbResult> DeleteAsync(WriteContext w, int id, CancellationToken ct = default)
        {
            var p = P.Write(w);
            p.Add("@Id", id);
            return _db.WriteAsync("dbo.PRO_AMS_MAINTENANCE_DELETE", p);
        }
    }

    public sealed class RequestRepository : IRequestRepository
    {
        private readonly DapperHelper _db;
        public RequestRepository(DapperHelper db) => _db = db;

        public Task<List<RequestView>> ListAsync(Guid tenantId, RequestStatus? status, int? id = null, CancellationToken ct = default)
        {
            var p = P.Tenant(tenantId);
            p.Add("@Status", status?.ToString());
            p.Add("@Id", id);
            return _db.ListAsync<RequestView>("dbo.PRO_AMS_REQUEST_LIST", p);
        }

        public Task<DbResult> CreateAsync(WriteContext w, int assetId, string reason, string email, CancellationToken ct = default)
        {
            var p = P.Tenant(w.TenantId);
            p.Add("@AssetId", assetId);
            p.Add("@Reason", reason);
            p.Add("@EmpCode", w.ActorEmpCode);
            p.Add("@EmpName", w.ActorName);
            p.Add("@EmpEmail", P.Clean(email));
            p.Add("@Now", w.Now, DbType.DateTime2);
            return _db.WriteAsync("dbo.PRO_AMS_REQUEST_CREATE", p, returnsRecordId: true);
        }

        public Task<DbResult> DecideAsync(WriteContext w, int id, bool approve, string? note, CancellationToken ct = default)
        {
            var p = P.Write(w);
            p.Add("@Id", id);
            p.Add("@Approve", approve);
            p.Add("@Note", P.Clean(note));
            return _db.WriteAsync("dbo.PRO_AMS_REQUEST_DECIDE", p);
        }

        public Task<DbResult> CancelAsync(WriteContext w, int id, CancellationToken ct = default)
        {
            var p = P.Write(w);
            p.Add("@Id", id);
            return _db.WriteAsync("dbo.PRO_AMS_REQUEST_CANCEL", p);
        }
    }

    public sealed class LicenseRepository : ILicenseRepository
    {
        private readonly DapperHelper _db;
        public LicenseRepository(DapperHelper db) => _db = db;

        public Task<List<LicenseView>> ListAsync(Guid tenantId, string? search, int? id = null, CancellationToken ct = default)
        {
            var p = P.Tenant(tenantId);
            p.Add("@Search", P.Clean(search));
            p.Add("@Id", id);
            return _db.ListAsync<LicenseView>("dbo.PRO_AMS_LICENSE_LIST", p);
        }

        public Task<DbResult> SaveAsync(WriteContext w, LicenseSaveReq l, CancellationToken ct = default)
        {
            var p = P.Write(w);
            p.Add("@Id", l.Id);
            p.Add("@Name", l.Name);
            p.Add("@ProductKey", P.Clean(l.ProductKey));
            p.Add("@Seats", l.Seats);
            p.Add("@ManufacturerId", l.ManufacturerId);
            p.Add("@CategoryId", l.CategoryId);
            p.Add("@SupplierId", l.SupplierId);
            p.Add("@PurchaseDate", P.Date(l.PurchaseDate), DbType.Date);
            p.Add("@PurchaseCost", l.PurchaseCost);
            p.Add("@ExpirationDate", P.Date(l.ExpirationDate), DbType.Date);
            p.Add("@Notes", P.Clean(l.Notes));
            return _db.WriteAsync("dbo.PRO_AMS_LICENSE_SAVE", p, returnsRecordId: true);
        }

        public Task<DbResult> DeleteAsync(WriteContext w, int id, CancellationToken ct = default)
        {
            var p = P.Write(w);
            p.Add("@Id", id);
            return _db.WriteAsync("dbo.PRO_AMS_LICENSE_DELETE", p);
        }

        public Task<List<LicenseSeatView>> SeatsAsync(Guid tenantId, int? licenseId, int? seatId = null, int? assetId = null, CancellationToken ct = default)
        {
            var p = P.Tenant(tenantId);
            p.Add("@LicenseId", licenseId);
            p.Add("@SeatId", seatId);
            p.Add("@AssetId", assetId);
            return _db.ListAsync<LicenseSeatView>("dbo.PRO_AMS_LICENSE_SEATS", p);
        }

        public Task<DbResult> CheckoutAsync(WriteContext w, int licenseId, Person? to, int? assetId, string? note, CancellationToken ct = default)
        {
            var p = P.Write(w);
            p.Add("@LicenseId", licenseId);
            p.Add("@EmpCode", to?.EmpCode);
            p.Add("@EmpName", to?.FullName);
            p.Add("@AssetId", assetId);
            p.Add("@Note", P.Clean(note));
            return _db.WriteAsync("dbo.PRO_AMS_LICENSE_CHECKOUT", p, returnsRecordId: true);
        }

        public Task<DbResult> CheckinAsync(WriteContext w, int seatId, string? note, CancellationToken ct = default)
        {
            var p = P.Write(w);
            p.Add("@SeatId", seatId);
            p.Add("@Note", P.Clean(note));
            return _db.WriteAsync("dbo.PRO_AMS_LICENSE_CHECKIN", p);
        }
    }

    public sealed class StockRepository : IStockRepository
    {
        private readonly DapperHelper _db;
        public StockRepository(DapperHelper db) => _db = db;

        public Task<List<StockItemView>> ListAsync(Guid tenantId, StockKind? kind, string? search, int? id = null, CancellationToken ct = default)
        {
            var p = P.Tenant(tenantId);
            p.Add("@Kind", kind?.ToString());
            p.Add("@Search", P.Clean(search));
            p.Add("@Id", id);
            return _db.ListAsync<StockItemView>("dbo.PRO_AMS_STOCK_LIST", p);
        }

        public Task<DbResult> SaveAsync(WriteContext w, StockSaveReq s, CancellationToken ct = default)
        {
            var p = P.Write(w);
            p.Add("@Id", s.Id);
            p.Add("@Kind", s.Kind.ToString());
            p.Add("@Name", s.Name);
            p.Add("@CategoryId", s.CategoryId);
            p.Add("@ManufacturerId", s.ManufacturerId);
            p.Add("@LocationId", s.LocationId);
            p.Add("@ModelNumber", P.Clean(s.ModelNumber));
            p.Add("@Serial", P.Clean(s.Serial));
            p.Add("@Quantity", s.Quantity);
            p.Add("@MinQuantity", s.MinQuantity);
            p.Add("@UnitCost", s.UnitCost);
            p.Add("@Notes", P.Clean(s.Notes));
            return _db.WriteAsync("dbo.PRO_AMS_STOCK_SAVE", p, returnsRecordId: true);
        }

        public Task<DbResult> DeleteAsync(WriteContext w, int id, CancellationToken ct = default)
        {
            var p = P.Write(w);
            p.Add("@Id", id);
            return _db.WriteAsync("dbo.PRO_AMS_STOCK_DELETE", p);
        }

        public Task<List<StockAssignmentView>> HoldersAsync(Guid tenantId, int? stockItemId, int? assetId = null, CancellationToken ct = default)
        {
            var p = P.Tenant(tenantId);
            p.Add("@StockItemId", stockItemId);
            p.Add("@AssetId", assetId);
            return _db.ListAsync<StockAssignmentView>("dbo.PRO_AMS_STOCK_HOLDERS", p);
        }

        public Task<DbResult> CheckoutAsync(WriteContext w, int stockItemId, Person? to, int? assetId, int quantity, string? note, CancellationToken ct = default)
        {
            var p = P.Write(w);
            p.Add("@StockItemId", stockItemId);
            p.Add("@EmpCode", to?.EmpCode);
            p.Add("@EmpName", to?.FullName);
            p.Add("@AssetId", assetId);
            p.Add("@Quantity", quantity);
            p.Add("@Note", P.Clean(note));
            return _db.WriteAsync("dbo.PRO_AMS_STOCK_CHECKOUT", p, returnsRecordId: true);
        }

        public Task<DbResult> CheckinAsync(WriteContext w, int stockItemId, int assignmentId, string? note, CancellationToken ct = default)
        {
            var p = P.Write(w);
            p.Add("@StockItemId", stockItemId);
            p.Add("@AssignmentId", assignmentId);
            p.Add("@Note", P.Clean(note));
            return _db.WriteAsync("dbo.PRO_AMS_STOCK_CHECKIN", p);
        }
    }

    public sealed class HistoryRepository : IHistoryRepository
    {
        private readonly DapperHelper _db;
        public HistoryRepository(DapperHelper db) => _db = db;

        public Task<List<HistoryEntry>> ListAsync(Guid tenantId, ItemType? type, int? itemId, string? search, DateTime? from, DateTime? to, int take, CancellationToken ct = default)
        {
            var p = P.Tenant(tenantId);
            p.Add("@ItemType", type?.ToString());
            p.Add("@ItemId", itemId);
            p.Add("@Search", P.Clean(search));
            p.Add("@From", P.Date(from), DbType.Date);
            p.Add("@To", P.Date(to), DbType.Date);
            p.Add("@Take", take);
            return _db.ListAsync<HistoryEntry>("dbo.PRO_AMS_HISTORY_LIST", p);
        }
    }
}

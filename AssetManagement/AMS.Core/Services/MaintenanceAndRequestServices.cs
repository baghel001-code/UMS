using AMS.Core.Entities;
using AMS.Core.Security;
using Microsoft.EntityFrameworkCore;
using static AMS.Core.Security.Perm;

namespace AMS.Core.Services
{
    public class MaintenanceInput
    {
        public Guid AssetId { get; set; }
        public MaintenanceType Type { get; set; } = MaintenanceType.Maintenance;
        public string Title { get; set; } = "";
        public Guid? SupplierId { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? CompletionDate { get; set; }
        public decimal Cost { get; set; }
        public bool IsWarranty { get; set; }
        public string? Notes { get; set; }

        public static MaintenanceInput From(Maintenance m) => new()
        {
            AssetId = m.AssetId, Type = m.Type, Title = m.Title, SupplierId = m.SupplierId, StartDate = m.StartDate.ToDateTime(TimeOnly.MinValue),
            CompletionDate = m.CompletionDate?.ToDateTime(TimeOnly.MinValue), Cost = m.Cost, IsWarranty = m.IsWarranty, Notes = m.Notes
        };
    }

    public sealed record MaintenanceRow(Maintenance Item, bool CanEdit, bool CanDelete);

    /// <summary>Repairs, upgrades and servicing of assets. Checked against the asset's attributes (location scope, cost…).</summary>
    public class MaintenanceService : AmsServiceBase
    {
        public MaintenanceService(IAccessGuard guard, IAmsDbContextFactory factory, TimeProvider time) : base(guard, factory, time) { }

        private static AccessCheck Check(Asset a, string action, decimal? cost = null)
        {
            var attrs = ResourceAttrs.For(a);
            attrs["assetCost"] = attrs["cost"];
            if (cost != null) attrs["cost"] = ResourceAttrs.Money(cost.Value);
            return new AccessCheck(Modules.Assets, Resources.Maintenance, action, attrs);
        }

        public async Task<List<MaintenanceRow>> ListAsync(bool openOnly, CancellationToken ct = default)
        {
            await using var db = Db();
            var q = db.Maintenances.AsNoTracking().Include(m => m.Supplier)
                .Include(m => m.Asset!).ThenInclude(a => a.Location)
                .Include(m => m.Asset!).ThenInclude(a => a.Model!).ThenInclude(x => x.Category)
                .Include(m => m.Asset!).ThenInclude(a => a.StatusLabel)
                .AsQueryable();
            if (openOnly) q = q.Where(m => m.CompletionDate == null);
            var all = await q.OrderByDescending(m => m.StartDate).ToListAsync(ct);
            var visible = await Guard.FilterAsync(all, m => Check(m.Asset!, Actions.View, m.Cost), ct);
            if (visible.Count == 0) return new List<MaintenanceRow>();
            var allowed = await Guard.CanAsync(visible.SelectMany(m => new[] { Check(m.Asset!, Actions.Edit, m.Cost), Check(m.Asset!, Actions.Delete, m.Cost) }).ToList(), ct);
            return visible.Select((m, i) => new MaintenanceRow(m, allowed[i * 2], allowed[i * 2 + 1])).ToList();
        }

        private static async Task<Asset> AssetAsync(IAmsDbContext db, Guid id, CancellationToken ct) =>
            Found(await AssetService.WithRefs(db.Assets).FirstOrDefaultAsync(a => a.Id == id, ct), "Asset");

        private void Apply(Maintenance m, MaintenanceInput x)
        {
            Require(!string.IsNullOrWhiteSpace(x.Title), "Title is required.");
            Require(x.Cost >= 0, "Cost cannot be negative.");
            var start = x.StartDate == null ? Today : DateOnly.FromDateTime(x.StartDate.Value);
            var done = x.CompletionDate == null ? (DateOnly?)null : DateOnly.FromDateTime(x.CompletionDate.Value);
            Require(done == null || done >= start, "Completion date cannot be before the start date.");
            m.Type = x.Type;
            m.Title = x.Title.Trim();
            m.SupplierId = x.SupplierId;
            m.StartDate = start;
            m.CompletionDate = done;
            m.Cost = x.Cost;
            m.IsWarranty = x.IsWarranty;
            m.Notes = x.Notes;
        }

        public async Task CreateAsync(MaintenanceInput input, CancellationToken ct = default)
        {
            await using var db = Db();
            var asset = await AssetAsync(db, input.AssetId, ct);
            await Guard.EnsureAsync(Check(asset, Actions.Create, input.Cost), ct);
            var m = new Maintenance { TenantId = Me.TenantId, AssetId = asset.Id };
            Apply(m, input);
            db.Maintenances.Add(m);
            Log(db, ItemType.Asset, asset.Id, $"{asset.AssetTag} {asset.Name}", asset.Location?.Code, HistoryAction.Maintenance, m.Type.ToString(), m.Title);
            await db.SaveChangesAsync(ct);
        }

        public async Task UpdateAsync(Guid id, MaintenanceInput input, CancellationToken ct = default)
        {
            await using var db = Db();
            var m = Found(await db.Maintenances.FirstOrDefaultAsync(x => x.Id == id, ct), "Maintenance");
            var asset = await AssetAsync(db, m.AssetId, ct);
            await Guard.EnsureAsync(Check(asset, Actions.Edit, Math.Max(m.Cost, input.Cost)), ct);
            Apply(m, input);
            Log(db, ItemType.Asset, asset.Id, $"{asset.AssetTag} {asset.Name}", asset.Location?.Code, HistoryAction.Maintenance, m.Type.ToString(),
                m.CompletionDate == null ? $"Updated: {m.Title}" : $"Completed: {m.Title}");
            await db.SaveChangesAsync(ct);
        }

        public async Task DeleteAsync(Guid id, CancellationToken ct = default)
        {
            await using var db = Db();
            var m = Found(await db.Maintenances.FirstOrDefaultAsync(x => x.Id == id, ct), "Maintenance");
            var asset = await AssetAsync(db, m.AssetId, ct);
            await Guard.EnsureAsync(Check(asset, Actions.Delete, m.Cost), ct);
            db.Maintenances.Remove(m);
            await db.SaveChangesAsync(ct);
        }
    }

    public sealed record RequestRow(AssetRequest Request, bool CanApprove, bool CanCancel);

    /// <summary>
    /// Employees request requestable assets; staff approve or reject. Who may see, cancel or approve which request is a UMS policy
    /// (e.g. "no self-approval", "approval needs MFA", "own requests only") evaluated on the request's attributes.
    /// </summary>
    public class RequestService : AmsServiceBase
    {
        private readonly IPeopleDirectory _people;
        private readonly AssetService _assets;

        public RequestService(IAccessGuard guard, IAmsDbContextFactory factory, IPeopleDirectory people, AssetService assets, TimeProvider time)
            : base(guard, factory, time)
        {
            _people = people;
            _assets = assets;
        }

        private static AccessCheck Check(AssetRequest r, string action) => new(Modules.Assets, Resources.Request, action, ResourceAttrs.For(r));

        private static IQueryable<AssetRequest> WithRefs(IQueryable<AssetRequest> q) => q
            .Include(r => r.Asset!).ThenInclude(a => a.Location)
            .Include(r => r.Asset!).ThenInclude(a => a.StatusLabel)
            .Include(r => r.Asset!).ThenInclude(a => a.Department)
            .Include(r => r.Asset!).ThenInclude(a => a.Model!).ThenInclude(m => m.Category);

        /// <summary>Assets offered for request that are free now, filtered by UMS (Request permission on each asset).</summary>
        public async Task<List<Asset>> RequestableAsync(CancellationToken ct = default)
        {
            await using var db = Db();
            var free = await AssetService.WithRefs(db.Assets.AsNoTracking())
                .Where(a => a.Requestable && a.AssignedToEmpCode == null && a.StatusLabel!.Type == StatusType.Deployable)
                .OrderBy(a => a.Model!.Name).ThenBy(a => a.AssetTag).ToListAsync(ct);
            var pending = await db.Requests.Where(r => r.Status == RequestStatus.Pending && r.RequestedByEmpCode == Me.EmpCode).Select(r => r.AssetId).ToListAsync(ct);
            free = free.Where(a => !pending.Contains(a.Id)).ToList();
            return await Guard.FilterAsync(free, a => new AccessCheck(Modules.Assets, Resources.Request, Actions.Request,
                new Dictionary<string, string>(ResourceAttrs.For(a)) { ["requestedByEmpCode"] = Me.EmpCode }), ct);
        }

        public async Task CreateAsync(Guid assetId, string reason, CancellationToken ct = default)
        {
            await using var db = Db();
            var asset = Found(await AssetService.WithRefs(db.Assets).FirstOrDefaultAsync(a => a.Id == assetId, ct), "Asset");
            var r = new AssetRequest
            {
                TenantId = Me.TenantId, AssetId = asset.Id, Asset = asset, RequestedByEmpCode = Me.EmpCode, RequestedByName = Me.Name,
                Reason = reason?.Trim() ?? "", RequestedAt = Now
            };
            await Guard.EnsureAsync(Check(r, Actions.Request), ct);
            Require(asset.Requestable, $"{asset.AssetTag} is not offered for requests.");
            Require(!asset.IsCheckedOut, $"{asset.AssetTag} is already issued.");
            Require(!string.IsNullOrWhiteSpace(r.Reason), "Please give a reason.");
            Require(!await db.Requests.AnyAsync(x => x.AssetId == asset.Id && x.RequestedByEmpCode == Me.EmpCode && x.Status == RequestStatus.Pending, ct),
                "You already have a pending request for this asset.");
            db.Requests.Add(r);
            Log(db, ItemType.Request, r.Id, $"{asset.AssetTag} {asset.Name}", asset.Location?.Code, HistoryAction.Requested, Me.Name, r.Reason);
            Log(db, ItemType.Asset, asset.Id, $"{asset.AssetTag} {asset.Name}", asset.Location?.Code, HistoryAction.Requested, $"{Me.Name} ({Me.EmpCode})", r.Reason);
            await db.SaveChangesAsync(ct);
        }

        public async Task<List<RequestRow>> ListAsync(RequestStatus? status, CancellationToken ct = default)
        {
            await using var db = Db();
            var q = WithRefs(db.Requests.AsNoTracking());
            if (status != null) q = q.Where(r => r.Status == status);
            var all = await q.OrderByDescending(r => r.RequestedAt).ToListAsync(ct);
            var visible = await Guard.FilterAsync(all, r => Check(r, Actions.View), ct);
            if (visible.Count == 0) return new List<RequestRow>();
            var allowed = await Guard.CanAsync(visible.SelectMany(r => new[] { Check(r, Actions.Approve), Check(r, Actions.Void) }).ToList(), ct);
            return visible.Select((r, i) =>
                new RequestRow(r, allowed[i * 2] && r.Status == RequestStatus.Pending, allowed[i * 2 + 1] && r.Status == RequestStatus.Pending)).ToList();
        }

        /// <summary>Approves and issues the asset to the requester (needs both Approve on the request and Checkout on the asset).</summary>
        public async Task ApproveAsync(Guid id, string? note, CancellationToken ct = default)
        {
            await using var db = Db();
            var r = Found(await WithRefs(db.Requests).FirstOrDefaultAsync(x => x.Id == id, ct), "Request");
            await Guard.EnsureAsync(Check(r, Actions.Approve), ct);
            Require(r.Status == RequestStatus.Pending, $"The request is already {r.Status.ToString().ToLowerInvariant()}.");
            var person = Found(await _people.FindAsync(r.RequestedByEmpCode, ct), $"Employee {r.RequestedByEmpCode}");
            var asset = r.Asset!;
            await Guard.EnsureAsync(new AccessCheck(Modules.Assets, Resources.Asset, Actions.Checkout, ResourceAttrs.For(asset).WithAssignee(person)), ct);

            await _assets.CheckoutCoreAsync(db, asset, person, null, $"Request approved{(string.IsNullOrWhiteSpace(note) ? "" : ": " + note)}", ct);
            Decide(r, RequestStatus.Approved, note);
            Log(db, ItemType.Request, r.Id, $"{asset.AssetTag} {asset.Name}", asset.Location?.Code, HistoryAction.Approved, r.RequestedByName, note);
            // other pending requests for the same asset can no longer be met
            foreach (var other in await db.Requests.Where(x => x.AssetId == asset.Id && x.Id != r.Id && x.Status == RequestStatus.Pending).ToListAsync(ct))
                Decide(other, RequestStatus.Rejected, $"Asset issued to {r.RequestedByName}");
            await db.SaveChangesAsync(ct);
        }

        public async Task RejectAsync(Guid id, string? note, CancellationToken ct = default)
        {
            await using var db = Db();
            var r = Found(await WithRefs(db.Requests).FirstOrDefaultAsync(x => x.Id == id, ct), "Request");
            await Guard.EnsureAsync(Check(r, Actions.Approve), ct);
            Require(r.Status == RequestStatus.Pending, $"The request is already {r.Status.ToString().ToLowerInvariant()}.");
            Decide(r, RequestStatus.Rejected, note);
            Log(db, ItemType.Request, r.Id, $"{r.Asset!.AssetTag} {r.Asset.Name}", r.Asset.Location?.Code, HistoryAction.Rejected, r.RequestedByName, note);
            await db.SaveChangesAsync(ct);
        }

        public async Task CancelAsync(Guid id, CancellationToken ct = default)
        {
            await using var db = Db();
            var r = Found(await WithRefs(db.Requests).FirstOrDefaultAsync(x => x.Id == id, ct), "Request");
            await Guard.EnsureAsync(Check(r, Actions.Void), ct);
            Require(r.Status == RequestStatus.Pending, $"The request is already {r.Status.ToString().ToLowerInvariant()}.");
            Decide(r, RequestStatus.Cancelled, null);
            Log(db, ItemType.Request, r.Id, $"{r.Asset!.AssetTag} {r.Asset.Name}", r.Asset.Location?.Code, HistoryAction.Cancelled, r.RequestedByName);
            await db.SaveChangesAsync(ct);
        }

        private void Decide(AssetRequest r, RequestStatus status, string? note)
        {
            r.Status = status;
            r.DecidedByEmpCode = Me.EmpCode;
            r.DecidedByName = Me.Name;
            r.DecidedAt = Now;
            r.DecisionNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        }
    }
}

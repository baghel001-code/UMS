using AMS.Core.Entities;
using AMS.Core.Security;
using Microsoft.EntityFrameworkCore;
using static AMS.Core.Security.Perm;

namespace AMS.Core.Services
{
    public sealed record CountRow(string Label, int Count, decimal Value);

    public sealed record DepreciationRow(Asset Asset, int Months, decimal CurrentValue)
    {
        public decimal Depreciated => Asset.PurchaseCost - CurrentValue;
    }

    public sealed record AssetReport(List<CountRow> ByCategory, List<CountRow> ByLocation, List<CountRow> ByStatus, List<DepreciationRow> Depreciation,
        List<Asset> WarrantyExpiring, List<Asset> AuditDue, List<Asset> OverdueCheckins)
    {
        public decimal TotalCost => Depreciation.Sum(d => d.Asset.PurchaseCost);
        public decimal TotalValue => Depreciation.Sum(d => d.CurrentValue);
    }

    public sealed record Dashboard(int Assets, int CheckedOut, int ReadyToDeploy, int NotDeployable, decimal TotalCost, int LicenseSeats, int LicenseSeatsUsed,
        int LowStock, int PendingRequests, int MaintenanceOpen, List<CountRow> ByCategory, List<CountRow> ByStatus, List<CountRow> ByLocation,
        List<Asset> AuditDue, List<Asset> OverdueCheckins, List<Asset> WarrantyExpiring, List<HistoryEntry> Recent, bool ShowLicenses, bool ShowStock,
        bool ShowRequests, bool ShowActivity);

    /// <summary>Type-level answers from UMS ("may I create assets at all?") used to build the menu and show buttons.</summary>
    public sealed class Capabilities
    {
        private readonly HashSet<string> _allowed;
        public Capabilities(IEnumerable<string> allowed) => _allowed = new HashSet<string>(allowed, StringComparer.OrdinalIgnoreCase);
        public static readonly Capabilities None = new(Array.Empty<string>());
        public bool Can(string module, string resource, string action) => _allowed.Contains($"AMS.{module}.{resource}.{action}");
        public bool Any => _allowed.Count > 0;
    }

    public class ReportService : AmsServiceBase
    {
        public ReportService(IAccessGuard guard, IAmsDbContextFactory factory, TimeProvider time) : base(guard, factory, time) { }

        /// <summary>Everything the UI may need to know up front: one UMS batch call, no logging.</summary>
        public async Task<Capabilities> CapabilitiesAsync(CancellationToken ct = default)
        {
            var mine = new Dictionary<string, string> { ["requestedByEmpCode"] = Me.EmpCode };
            var checks = new List<AccessCheck>
            {
                new(Modules.Dashboard, Resources.Overview, Actions.View),
                new(Modules.Self, Resources.MyAssets, Actions.View),
                new(Modules.Reports, Resources.Report, Actions.View), new(Modules.Reports, Resources.Report, Actions.Export),
                new(Modules.Reports, Resources.Activity, Actions.View), new(Modules.Reports, Resources.Activity, Actions.Export),
                new(Modules.Assets, Resources.Request, Actions.View, mine), new(Modules.Assets, Resources.Request, Actions.Request, mine),
                new(Modules.Assets, Resources.Request, Actions.Approve),
            };
            foreach (var a in new[] { Actions.View, Actions.Create, Actions.Edit, Actions.Delete, Actions.Checkout, Actions.Checkin, Actions.Audit, Actions.Export, Actions.Print })
                checks.Add(new(Modules.Assets, Resources.Asset, a));
            foreach (var a in new[] { Actions.View, Actions.Create, Actions.Edit, Actions.Delete })
                checks.Add(new(Modules.Assets, Resources.Maintenance, a));
            foreach (var (m, r) in new[] { (Modules.Licenses, Resources.License), (Modules.Accessories, Resources.Accessory), (Modules.Consumables, Resources.Consumable), (Modules.Components, Resources.Component) })
                foreach (var a in new[] { Actions.View, Actions.Create, Actions.Checkout, Actions.Export })
                    checks.Add(new(m, r, a));
            foreach (var k in Enum.GetValues<LookupKind>())
                foreach (var a in new[] { Actions.View, Actions.Create })
                    checks.Add(new(Modules.Setup, LookupService.ResourceOf(k), a));

            var ok = await Guard.CanAsync(checks, ct);
            return new Capabilities(checks.Where((_, i) => ok[i]).Select(c => c.Key));
        }

        private async Task<List<Asset>> AssetsAsync(IAmsDbContext db, string module, string resource, CancellationToken ct)
        {
            var all = await AssetService.WithRefs(db.Assets.AsNoTracking()).OrderBy(a => a.AssetTag).ToListAsync(ct);
            return await Guard.FilterAsync(all, a => new AccessCheck(module, resource, Actions.View, ResourceAttrs.For(a)), ct);
        }

        private static List<CountRow> Group(IEnumerable<Asset> assets, Func<Asset, string?> key) => assets
            .GroupBy(a => key(a) ?? "—").Select(g => new CountRow(g.Key, g.Count(), g.Sum(a => a.PurchaseCost)))
            .OrderByDescending(r => r.Count).ThenBy(r => r.Label).ToList();

        public static decimal CurrentValue(Asset a, DateOnly today, out int months)
        {
            months = a.PurchaseDate == null ? 0 : Math.Max(0, (today.Year - a.PurchaseDate.Value.Year) * 12 + today.Month - a.PurchaseDate.Value.Month);
            var life = a.Model?.Category?.DepreciationMonths ?? 0;
            if (life <= 0) return a.PurchaseCost;
            return Math.Round(a.PurchaseCost * Math.Max(0m, 1m - (decimal)months / life), 2);
        }

        private List<Asset> WarrantyExpiring(IEnumerable<Asset> a) =>
            a.Where(x => x.WarrantyExpires != null && x.WarrantyExpires >= Today && x.WarrantyExpires <= Today.AddDays(90)).OrderBy(x => x.WarrantyExpires).ToList();
        private List<Asset> AuditDue(IEnumerable<Asset> a) =>
            a.Where(x => x.NextAuditDate != null && x.NextAuditDate <= Today.AddDays(30)).OrderBy(x => x.NextAuditDate).ToList();
        private List<Asset> Overdue(IEnumerable<Asset> a) =>
            a.Where(x => x.IsCheckedOut && x.ExpectedCheckin != null && x.ExpectedCheckin < Today).OrderBy(x => x.ExpectedCheckin).ToList();

        public async Task<Dashboard> DashboardAsync(CancellationToken ct = default)
        {
            await Ensure(Modules.Dashboard, Resources.Overview, Actions.View, ct: ct);
            await using var db = Db();
            var assets = await AssetsAsync(db, Modules.Assets, Resources.Asset, ct);

            var caps = await Guard.CanAsync(new[]
            {
                new AccessCheck(Modules.Licenses, Resources.License, Actions.View), new AccessCheck(Modules.Accessories, Resources.Accessory, Actions.View),
                new AccessCheck(Modules.Consumables, Resources.Consumable, Actions.View), new AccessCheck(Modules.Assets, Resources.Request, Actions.Approve),
                new AccessCheck(Modules.Reports, Resources.Activity, Actions.View), new AccessCheck(Modules.Assets, Resources.Maintenance, Actions.View),
            }, ct);

            int seats = 0, used = 0, low = 0, pending = 0, open = 0;
            if (caps[0])
            {
                var lic = await db.Licenses.AsNoTracking().Include(l => l.SeatAssignments).ToListAsync(ct);
                seats = lic.Sum(l => l.Seats);
                used = lic.Sum(l => l.SeatAssignments.Count);
            }
            if (caps[1] || caps[2])
            {
                var acc = caps[1] ? await db.Accessories.AsNoTracking().Include(a => a.Checkouts).Include(a => a.Location).ToListAsync(ct) : new();
                var con = caps[2] ? await db.Consumables.AsNoTracking().Include(a => a.Location).ToListAsync(ct) : new();
                var items = acc.Cast<StockItem>().Concat(con).ToList();
                var visible = await Guard.FilterAsync(items, s => new AccessCheck(s is Accessory ? Modules.Accessories : Modules.Consumables,
                    s is Accessory ? Resources.Accessory : Resources.Consumable, Actions.View, ResourceAttrs.For(s)), ct);
                low = visible.Count(s => s.Quantity - InventoryService.InUse(s) <= s.MinQuantity);
            }
            if (caps[3])
            {
                var reqs = await db.Requests.AsNoTracking().Include(r => r.Asset!).ThenInclude(a => a.Location).Where(r => r.Status == RequestStatus.Pending).ToListAsync(ct);
                pending = (await Guard.FilterAsync(reqs, r => new AccessCheck(Modules.Assets, Resources.Request, Actions.Approve, ResourceAttrs.For(r)), ct)).Count;
            }
            if (caps[5])
            {
                var ids = assets.Select(a => a.Id).ToHashSet();
                open = (await db.Maintenances.Where(m => m.CompletionDate == null).Select(m => m.AssetId).ToListAsync(ct)).Count(ids.Contains);
            }
            var recent = new List<HistoryEntry>();
            if (caps[4])
            {
                var last = await db.History.AsNoTracking().OrderByDescending(h => h.At).Take(60).ToListAsync(ct);
                recent = (await Guard.FilterAsync(last, h => new AccessCheck(Modules.Reports, Resources.Activity, Actions.View, ResourceAttrs.Location(h.LocationCode)), ct)).Take(8).ToList();
            }

            return new Dashboard(assets.Count, assets.Count(a => a.IsCheckedOut),
                assets.Count(a => !a.IsCheckedOut && a.StatusLabel?.Type == StatusType.Deployable),
                assets.Count(a => a.StatusLabel?.Type is StatusType.Pending or StatusType.Undeployable),
                assets.Sum(a => a.PurchaseCost), seats, used, low, pending, open,
                Group(assets, a => a.Model?.Category?.Name), Group(assets, a => a.StatusLabel?.Name), Group(assets, a => a.Location?.Code),
                AuditDue(assets).Take(6).ToList(), Overdue(assets).Take(6).ToList(), WarrantyExpiring(assets).Take(6).ToList(), recent,
                caps[0], caps[1] || caps[2], caps[3], caps[4]);
        }

        public async Task<AssetReport> AssetReportAsync(CancellationToken ct = default)
        {
            await Ensure(Modules.Reports, Resources.Report, Actions.View, ct: ct);
            await using var db = Db();
            // per asset: a location-scoped manager reports only on their locations
            var assets = await AssetsAsync(db, Modules.Reports, Resources.Report, ct);
            var dep = assets.Select(a => new DepreciationRow(a, 0, CurrentValue(a, Today, out var m)) with { Months = m }).ToList();
            return new AssetReport(Group(assets, a => a.Model?.Category?.Name), Group(assets, a => a.Location?.Code), Group(assets, a => a.StatusLabel?.Name),
                dep, WarrantyExpiring(assets), AuditDue(assets), Overdue(assets));
        }

        public async Task<string> AssetReportCsvAsync(CancellationToken ct = default)
        {
            await Ensure(Modules.Reports, Resources.Report, Actions.Export, ct: ct);
            var r = await AssetReportAsync(ct);
            return Csv.Write(new[] { "Asset tag", "Name", "Category", "Location", "Status", "Purchase date", "Cost", "Months", "Current value", "Depreciated" },
                r.Depreciation.Select(d => new object?[] { d.Asset.AssetTag, d.Asset.Name, d.Asset.Model?.Category?.Name, d.Asset.Location?.Code, d.Asset.StatusLabel?.Name,
                    d.Asset.PurchaseDate, d.Asset.PurchaseCost, d.Months, d.CurrentValue, d.Depreciated }));
        }

        public async Task<List<HistoryEntry>> ActivityAsync(ItemType? type, string? search, DateTime? from, DateTime? to, CancellationToken ct = default)
        {
            await Ensure(Modules.Reports, Resources.Activity, Actions.View, ct: ct);
            await using var db = Db();
            var q = db.History.AsNoTracking().AsQueryable();
            if (type != null) q = q.Where(h => h.ItemType == type);
            if (from != null) q = q.Where(h => h.At >= from.Value.Date);
            if (to != null) q = q.Where(h => h.At < to.Value.Date.AddDays(1));
            var rows = (await q.OrderByDescending(h => h.At).Take(2000).ToListAsync(ct))
                .Where(h => string.IsNullOrWhiteSpace(search) || $"{h.ItemName} {h.ActorName} {h.ActorEmpCode} {h.Target} {h.Note}".Contains(search.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToList();
            return await Guard.FilterAsync(rows, h => new AccessCheck(Modules.Reports, Resources.Activity, Actions.View, ResourceAttrs.Location(h.LocationCode)), ct);
        }

        public async Task<string> ActivityCsvAsync(ItemType? type, string? search, DateTime? from, DateTime? to, CancellationToken ct = default)
        {
            await Ensure(Modules.Reports, Resources.Activity, Actions.Export, ct: ct);
            var rows = await ActivityAsync(type, search, from, to, ct);
            return Csv.Write(new[] { "When", "By", "Emp code", "Item type", "Item", "Action", "Target", "Location", "Note" },
                rows.Select(h => new object?[] { h.At, h.ActorName, h.ActorEmpCode, h.ItemType, h.ItemName, h.Action, h.Target, h.LocationCode, h.Note }));
        }
    }
}

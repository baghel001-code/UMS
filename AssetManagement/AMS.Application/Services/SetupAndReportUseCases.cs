using AMS.Application.Common;
using AMS.Application.Interfaces;
using AMS.Application.Notifications;
using AMS.Domain.Base;
using AMS.Domain.Enums;
using AMS.Domain.Models;
using AMS.Domain.Requests;
using AMS.Domain.Responses;
using AMS.Domain.Security;
using static AMS.Domain.Security.Perm;

namespace AMS.Application.Services
{
    /// <summary>Setup lists (categories, manufacturers, models, suppliers, locations, departments, status labels).</summary>
    public sealed class SetupUseCases : UseCaseBase
    {
        private readonly ISetupRepository _setup;

        public SetupUseCases(IAccessGuard guard, ICurrentActor current, IBusinessClock clock, ISetupRepository setup) : base(guard, current, clock) => _setup = setup;

        public static string ResourceOf(SetupKind kind) => Domain.Security.Perm.Setup(kind);

        private static AccessCheck Check(SetupKind kind, string action, SetupItem? item = null)
        {
            var attrs = new Dictionary<string, string>();
            if (item != null)
            {
                attrs["name"] = item.Name;
                if (kind == SetupKind.Location && !string.IsNullOrEmpty(item.Code)) { attrs["location"] = item.Code; attrs["region"] = item.Region ?? ""; }
            }
            return new AccessCheck(Modules.Setup, ResourceOf(kind), action, attrs);
        }

        public async Task<SetupListRes> ListAsync(SetupKind kind, CancellationToken ct = default)
        {
            await Ensure(Modules.Setup, ResourceOf(kind), Actions.View, ct: ct);
            var visible = await Guard.FilterAsync(await _setup.ListAsync(Tenant, kind, ct), i => Check(kind, Actions.View, i), ct);
            var m = await Guard.MatrixAsync(visible, new[] { Actions.Edit, Actions.Delete }, (i, a) => Check(kind, a, i), ct);
            for (var i = 0; i < visible.Count; i++)
            {
                visible[i].CanEdit = m[i][0];
                visible[i].CanDelete = m[i][1] && visible[i].UsedBy == 0;
            }
            return new SetupListRes { Items = visible };
        }

        public async Task<InUpRes> SaveAsync(SetupItem item, CancellationToken ct = default)
        {
            Require(!string.IsNullOrWhiteSpace(item.Name), "Name is required.");
            if (item.Id != 0)
            {
                // check against the record as stored, then as edited
                var stored = Found((await _setup.ListAsync(Tenant, item.Kind, ct)).FirstOrDefault(i => i.Id == item.Id), item.Kind.ToString());
                await Guard.EnsureAsync(Check(item.Kind, Actions.Edit, stored), ct);
            }
            await Guard.EnsureAsync(Check(item.Kind, item.Id == 0 ? Actions.Create : Actions.Edit, item), ct);
            var res = (await _setup.SaveAsync(Write, item, ct)).EnsureOk();
            return new InUpRes { recordId = res.RecordId, responseMessage = "Saved" };
        }

        public async Task<InUpRes> DeleteAsync(SetupKind kind, int id, CancellationToken ct = default)
        {
            var item = Found((await _setup.ListAsync(Tenant, kind, ct)).FirstOrDefault(i => i.Id == id), kind.ToString());
            await Guard.EnsureAsync(Check(kind, Actions.Delete, item), ct);
            (await _setup.DeleteAsync(Write, kind, id, ct)).EnsureOk();
            return new InUpRes { recordId = id, responseMessage = "Deleted" };
        }

        /// <summary>Choices for forms and filters. Each list is filtered by the user's SETUP view rights (a location-scoped manager sees only their locations).</summary>
        public async Task<OptionsRes> OptionsAsync(CancellationToken ct = default)
        {
            var o = await _setup.OptionsAsync(Tenant, ct);
            var checks = new List<AccessCheck>
            {
                Check(SetupKind.Category, Actions.View), Check(SetupKind.Manufacturer, Actions.View), Check(SetupKind.Model, Actions.View),
                Check(SetupKind.Supplier, Actions.View), Check(SetupKind.Department, Actions.View), Check(SetupKind.Status, Actions.View)
            };
            checks.AddRange(o.Locations.Select(l => Check(SetupKind.Location, Actions.View, l)));
            var ok = await Guard.CanAsync(checks, ct);
            return new OptionsRes
            {
                Categories = ok[0] ? o.Categories : new(), Manufacturers = ok[1] ? o.Manufacturers : new(), Models = ok[2] ? o.Models : new(),
                Suppliers = ok[3] ? o.Suppliers : new(), Departments = ok[4] ? o.Departments : new(), Statuses = ok[5] ? o.Statuses : new(),
                Locations = o.Locations.Where((_, i) => ok[6 + i]).ToList()
            };
        }
    }

    /// <summary>Capabilities for the portal menu, the dashboard, reports and the activity log.</summary>
    public sealed class ReportUseCases : UseCaseBase
    {
        private readonly AssetUseCases _assets;
        private readonly ILicenseRepository _licenses;
        private readonly IStockRepository _stock;
        private readonly IRequestRepository _requests;
        private readonly IMaintenanceRepository _maintenance;
        private readonly IHistoryRepository _history;

        public ReportUseCases(IAccessGuard guard, ICurrentActor current, IBusinessClock clock, AssetUseCases assets, ILicenseRepository licenses,
            IStockRepository stock, IRequestRepository requests, IMaintenanceRepository maintenance, IHistoryRepository history) : base(guard, current, clock)
        {
            _assets = assets;
            _licenses = licenses;
            _stock = stock;
            _requests = requests;
            _maintenance = maintenance;
            _history = history;
        }

        /// <summary>Type-level answers ("may I create assets at all?") in one UMS batch call; only used to shape the UI.</summary>
        public async Task<CapabilitiesRes> CapabilitiesAsync(CancellationToken ct = default)
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
            foreach (var k in Enum.GetValues<SetupKind>())
                foreach (var a in new[] { Actions.View, Actions.Create })
                    checks.Add(new(Modules.Setup, SetupUseCases.ResourceOf(k), a));

            var ok = await Guard.CanAsync(checks, ct);
            return new CapabilitiesRes { Allowed = checks.Where((_, i) => ok[i]).Select(c => c.Key).Distinct().ToList() };
        }

        private static List<CountRow> Group(IEnumerable<AssetView> assets, Func<AssetView, string?> key) => assets
            .GroupBy(a => key(a) ?? "—").Select(g => new CountRow(g.Key, g.Count(), g.Sum(a => a.PurchaseCost)))
            .OrderByDescending(r => r.Count).ThenBy(r => r.Label).ToList();

        internal static List<AssetView> WarrantyExpiring(IEnumerable<AssetView> a, DateTime today) =>
            a.Where(x => x.WarrantyExpires != null && x.WarrantyExpires >= today && x.WarrantyExpires <= today.AddDays(90)).OrderBy(x => x.WarrantyExpires).ToList();
        internal static List<AssetView> AuditDue(IEnumerable<AssetView> a, DateTime today) =>
            a.Where(x => x.NextAuditDate != null && x.NextAuditDate <= today.AddDays(30)).OrderBy(x => x.NextAuditDate).ToList();
        internal static List<AssetView> Overdue(IEnumerable<AssetView> a, DateTime today) =>
            a.Where(x => x.IsCheckedOut && x.ExpectedCheckin != null && x.ExpectedCheckin < today).OrderBy(x => x.ExpectedCheckin).ToList();

        public async Task<DashboardRes> DashboardAsync(CancellationToken ct = default)
        {
            await Ensure(Modules.Dashboard, Resources.Overview, Actions.View, ct: ct);
            var assets = await _assets.VisibleAsync(new AssetFilterReq(), ct);
            var caps = await Guard.CanAsync(new[]
            {
                new AccessCheck(Modules.Licenses, Resources.License, Actions.View), new AccessCheck(Modules.Accessories, Resources.Accessory, Actions.View),
                new AccessCheck(Modules.Consumables, Resources.Consumable, Actions.View), new AccessCheck(Modules.Assets, Resources.Request, Actions.Approve),
                new AccessCheck(Modules.Reports, Resources.Activity, Actions.View), new AccessCheck(Modules.Assets, Resources.Maintenance, Actions.View),
            }, ct);
            var today = Clock.Today;
            var d = new DashboardRes
            {
                Assets = assets.Count,
                CheckedOut = assets.Count(a => a.IsCheckedOut),
                ReadyToDeploy = assets.Count(a => !a.IsCheckedOut && a.StatusType == StatusType.Deployable),
                NotDeployable = assets.Count(a => a.StatusType is StatusType.Pending or StatusType.Undeployable),
                TotalCost = assets.Sum(a => a.PurchaseCost),
                ByCategory = Group(assets, a => a.CategoryName),
                ByStatus = Group(assets, a => a.StatusName),
                ByLocation = Group(assets, a => a.LocationCode),
                AuditDue = AuditDue(assets, today).Take(6).ToList(),
                OverdueCheckins = Overdue(assets, today).Take(6).ToList(),
                WarrantyExpiring = WarrantyExpiring(assets, today).Take(6).ToList(),
                ShowLicenses = caps[0], ShowStock = caps[1] || caps[2], ShowRequests = caps[3], ShowActivity = caps[4],
            };
            if (caps[0])
            {
                var lic = await _licenses.ListAsync(Tenant, null, null, ct);
                d.LicenseSeats = lic.Sum(l => l.Seats);
                d.LicenseSeatsUsed = lic.Sum(l => l.Used);
            }
            if (caps[1] || caps[2])
            {
                var items = (await _stock.ListAsync(Tenant, null, null, null, ct))
                    .Where(s => (caps[1] && s.Kind == StockKind.Accessory) || (caps[2] && s.Kind == StockKind.Consumable)).ToList();
                d.LowStock = (await Guard.FilterAsync(items, s => new AccessCheck(StockUseCases.Catalogue(s.Kind).Module, StockUseCases.Catalogue(s.Kind).Resource,
                    Actions.View, ResourceAttrs.For(s)), ct)).Count(s => s.Low);
            }
            if (caps[3])
            {
                var pending = await _requests.ListAsync(Tenant, RequestStatus.Pending, null, ct);
                d.PendingRequests = (await Guard.FilterAsync(pending, r => new AccessCheck(Modules.Assets, Resources.Request, Actions.Approve, ResourceAttrs.For(r)), ct)).Count;
            }
            if (caps[5])
            {
                var ids = assets.Select(a => a.Id).ToHashSet();
                d.MaintenanceOpen = (await _maintenance.ListAsync(Tenant, true, null, null, ct)).Count(m => ids.Contains(m.AssetId));
            }
            if (caps[4])
            {
                var last = await _history.ListAsync(Tenant, null, null, null, null, null, 60, ct);
                d.Recent = (await Guard.FilterAsync(last, h => new AccessCheck(Modules.Reports, Resources.Activity, Actions.View, ResourceAttrs.Location(h.LocationCode)), ct))
                    .Take(8).ToList();
            }
            return d;
        }

        public async Task<AssetReportRes> AssetReportAsync(CancellationToken ct = default)
        {
            await Ensure(Modules.Reports, Resources.Report, Actions.View, ct: ct);
            // per asset: a location-scoped manager reports only on their locations
            var assets = await _assets.VisibleAsync(new AssetFilterReq(), ct, Modules.Reports, Resources.Report);
            var today = Clock.Today;
            return new AssetReportRes
            {
                ByCategory = Group(assets, a => a.CategoryName), ByLocation = Group(assets, a => a.LocationCode), ByStatus = Group(assets, a => a.StatusName),
                Depreciation = assets.Select(a => { var v = Depreciation.CurrentValue(a, today, out var m); return new DepreciationRow(a, m, v); }).ToList(),
                WarrantyExpiring = WarrantyExpiring(assets, today), AuditDue = AuditDue(assets, today), OverdueCheckins = Overdue(assets, today),
            };
        }

        public async Task<FileRes> AssetReportCsvAsync(CancellationToken ct = default)
        {
            await Ensure(Modules.Reports, Resources.Report, Actions.Export, ct: ct);
            var r = await AssetReportAsync(ct);
            return new FileRes
            {
                FileName = $"asset-depreciation-{Clock.Now:yyyyMMdd-HHmm}.csv",
                Content = Csv.Write(new[] { "Asset tag", "Name", "Category", "Location", "Status", "Purchase date", "Cost", "Months", "Current value", "Depreciated" },
                    r.Depreciation.Select(d => new object?[] { d.Asset.AssetTag, d.Asset.Name, d.Asset.CategoryName, d.Asset.LocationCode, d.Asset.StatusName,
                        d.Asset.PurchaseDate, d.Asset.PurchaseCost, d.Months, d.CurrentValue, d.Depreciated }))
            };
        }

        public async Task<ActivityRes> ActivityAsync(ActivityReq q, CancellationToken ct = default)
        {
            await Ensure(Modules.Reports, Resources.Activity, Actions.View, ct: ct);
            var rows = await _history.ListAsync(Tenant, q.ItemType, null, string.IsNullOrWhiteSpace(q.Search) ? null : q.Search.Trim(), q.From?.Date, q.To?.Date, 2000, ct);
            return new ActivityRes
            {
                Entries = await Guard.FilterAsync(rows, h => new AccessCheck(Modules.Reports, Resources.Activity, Actions.View, ResourceAttrs.Location(h.LocationCode)), ct)
            };
        }

        public async Task<FileRes> ActivityCsvAsync(ActivityReq q, CancellationToken ct = default)
        {
            await Ensure(Modules.Reports, Resources.Activity, Actions.Export, ct: ct);
            var rows = (await ActivityAsync(q, ct)).Entries;
            return new FileRes
            {
                FileName = $"ams-activity-{Clock.Now:yyyyMMdd-HHmm}.csv",
                Content = Csv.Write(new[] { "When", "By", "Emp code", "Item type", "Item", "Action", "Target", "Location", "Note" },
                    rows.Select(h => new object?[] { h.At, h.ActorName, h.ActorEmpCode, h.ItemType, h.ItemName, h.Action, h.Target, h.LocationCode, h.Note }))
            };
        }
    }

    /// <summary>
    /// The asset desk's daily digest (overdue returns, audits due, expiring licences, low stock). Runs as a background job for each tenant,
    /// not on behalf of a user, so it reads the repositories directly and sends only to the configured asset desk mailbox.
    /// </summary>
    public sealed class DailyDigestBuilder
    {
        private readonly IAssetRepository _assets;
        private readonly ILicenseRepository _licenses;
        private readonly IStockRepository _stock;
        private readonly IBusinessClock _clock;

        public DailyDigestBuilder(IAssetRepository assets, ILicenseRepository licenses, IStockRepository stock, IBusinessClock clock)
        {
            _assets = assets;
            _licenses = licenses;
            _stock = stock;
            _clock = clock;
        }

        public async Task<DailyDigestModel> BuildAsync(Guid tenantId, CancellationToken ct = default)
        {
            var today = _clock.Today;
            var assets = await _assets.ListAsync(tenantId, new AssetFilterReq(), false, ct);
            var licenses = (await _licenses.ListAsync(tenantId, null, null, ct))
                .Where(l => l.ExpirationDate != null && l.ExpirationDate <= today.AddDays(30)).OrderBy(l => l.ExpirationDate).ToList();
            var low = (await _stock.ListAsync(tenantId, null, null, null, ct)).Where(s => s.Kind != StockKind.Component && s.Low).ToList();
            return new DailyDigestModel(tenantId, today, ReportUseCases.Overdue(assets, today), ReportUseCases.AuditDue(assets, today), licenses, low);
        }
    }
}

using AMS.Application.Common;
using AMS.Application.Interfaces;
using AMS.Application.Notifications;
using AMS.Domain.Base;
using AMS.Domain.Enums;
using AMS.Domain.Exceptions;
using AMS.Domain.Models;
using AMS.Domain.Requests;
using AMS.Domain.Responses;
using AMS.Domain.Security;
using static AMS.Domain.Security.Perm;

namespace AMS.Application.Services
{
    /// <summary>Repairs, upgrades and servicing of assets, checked against the asset's attributes and the job's cost.</summary>
    public sealed class MaintenanceUseCases : UseCaseBase
    {
        private readonly IMaintenanceRepository _maintenance;
        private readonly IAssetRepository _assets;

        public MaintenanceUseCases(IAccessGuard guard, ICurrentActor current, IBusinessClock clock, IMaintenanceRepository maintenance, IAssetRepository assets)
            : base(guard, current, clock)
        {
            _maintenance = maintenance;
            _assets = assets;
        }

        private static AccessCheck Check(MaintenanceView m, string action) => new(Modules.Assets, Resources.Maintenance, action, ResourceAttrs.For(m));

        public async Task<MaintenanceListRes> ListAsync(bool openOnly, CancellationToken ct = default)
        {
            var visible = await Guard.FilterAsync(await _maintenance.ListAsync(Tenant, openOnly, null, null, ct), m => Check(m, Actions.View), ct);
            var m = await Guard.MatrixAsync(visible, new[] { Actions.Edit, Actions.Delete }, Check, ct);
            return new MaintenanceListRes { Rows = visible.Select((x, i) => new MaintenanceRow(x, m[i][0], m[i][1])).ToList() };
        }

        public async Task<InUpRes> SaveAsync(MaintenanceSaveReq r, CancellationToken ct = default)
        {
            Require(!string.IsNullOrWhiteSpace(r.Title), "Title is required.");
            var asset = Found(await _assets.GetAsync(Tenant, r.AssetId, ct), "Asset");
            var job = new MaintenanceView
            {
                AssetId = asset.Id, AssetTag = asset.AssetTag, AssetCost = asset.PurchaseCost, LocationCode = asset.LocationCode, Region = asset.Region,
                CategoryName = asset.CategoryName, StatusName = asset.StatusName, Cost = r.Cost
            };
            if (r.Id == 0) await Guard.EnsureAsync(Check(job, Actions.Create), ct);
            else
            {
                var before = Found((await _maintenance.ListAsync(Tenant, false, null, r.Id, ct)).FirstOrDefault(), "Maintenance record");
                Require(before.AssetId == r.AssetId, "A maintenance record cannot be moved to another asset.");
                job.Cost = Math.Max(before.Cost, r.Cost);
                await Guard.EnsureAsync(Check(job, Actions.Edit), ct);
            }
            r.StartDate ??= Clock.Today;
            var res = (await _maintenance.SaveAsync(Write, r, ct)).EnsureOk();
            return new InUpRes { recordId = res.RecordId, responseMessage = "Maintenance saved" };
        }

        public async Task<InUpRes> DeleteAsync(int id, CancellationToken ct = default)
        {
            var m = Found((await _maintenance.ListAsync(Tenant, false, null, id, ct)).FirstOrDefault(), "Maintenance record");
            await Guard.EnsureAsync(Check(m, Actions.Delete), ct);
            (await _maintenance.DeleteAsync(Write, id, ct)).EnsureOk();
            return new InUpRes { recordId = id, responseMessage = "Maintenance deleted" };
        }
    }

    /// <summary>
    /// Employees request requestable assets; staff approve (which issues the asset) or reject. Who may see, cancel or approve which
    /// request is a UMS policy evaluated on the request's attributes (e.g. "no self-approval", "approval needs MFA", "own requests only").
    /// </summary>
    public sealed class RequestUseCases : UseCaseBase
    {
        private readonly IRequestRepository _requests;
        private readonly IAssetRepository _assets;
        private readonly IPeopleDirectory _people;
        private readonly INotificationPublisher _notify;

        public RequestUseCases(IAccessGuard guard, ICurrentActor current, IBusinessClock clock, IRequestRepository requests, IAssetRepository assets,
            IPeopleDirectory people, INotificationPublisher notify) : base(guard, current, clock)
        {
            _requests = requests;
            _assets = assets;
            _people = people;
            _notify = notify;
        }

        private static AccessCheck Check(RequestView r, string action) => new(Modules.Assets, Resources.Request, action, ResourceAttrs.For(r));

        private async Task<RequestView> LoadAsync(int id, CancellationToken ct) => Found((await _requests.ListAsync(Tenant, null, id, ct)).FirstOrDefault(), "Request");

        /// <summary>Assets offered for request that are free now, filtered by UMS (Request permission on each asset).</summary>
        public async Task<AssetsRes> RequestableAsync(CancellationToken ct = default)
        {
            var free = await _assets.ListAsync(Tenant, new AssetFilterReq(), requestableOnly: true, ct);
            var mine = (await _requests.ListAsync(Tenant, RequestStatus.Pending, null, ct)).Where(r => r.RequestedByEmpCode == Me.EmpCode).Select(r => r.AssetId).ToHashSet();
            free = free.Where(a => !mine.Contains(a.Id)).ToList();
            return new AssetsRes
            {
                Assets = await Guard.FilterAsync(free, a => new AccessCheck(Modules.Assets, Resources.Request, Actions.Request,
                    new Dictionary<string, string>(ResourceAttrs.For(a)) { ["requestedByEmpCode"] = Me.EmpCode }), ct)
            };
        }

        public async Task<InUpRes> CreateAsync(RequestCreateReq r, CancellationToken ct = default)
        {
            var asset = Found(await _assets.GetAsync(Tenant, r.AssetId, ct), "Asset");
            var attrs = ResourceAttrs.For(asset);
            attrs.Remove("assignedToEmpCode");
            attrs["requestedByEmpCode"] = Me.EmpCode;
            attrs["requestStatus"] = RequestStatus.Pending.ToString();
            await Guard.EnsureAsync(new AccessCheck(Modules.Assets, Resources.Request, Actions.Request, attrs), ct);
            var res = (await _requests.CreateAsync(Write, r.AssetId, r.Reason ?? "", Me.Email, ct)).EnsureOk();
            if ((await _requests.ListAsync(Tenant, null, res.RecordId, ct)).FirstOrDefault() is { } created)
                _notify.Publish(new RequestSubmittedModel(created));
            return new InUpRes { recordId = res.RecordId, responseMessage = "Request submitted" };
        }

        public async Task<RequestListRes> ListAsync(RequestStatus? status, CancellationToken ct = default)
        {
            var visible = await Guard.FilterAsync(await _requests.ListAsync(Tenant, status, null, ct), r => Check(r, Actions.View), ct);
            var m = await Guard.MatrixAsync(visible, new[] { Actions.Approve, Actions.Void }, Check, ct);
            return new RequestListRes
            {
                Rows = visible.Select((r, i) => new RequestRow(r, m[i][0] && r.Status == RequestStatus.Pending, m[i][1] && r.Status == RequestStatus.Pending)).ToList()
            };
        }

        /// <summary>Approve = issue the asset to the requester: needs Approve on the request and Checkout on the asset (with the requester as assignee).</summary>
        public async Task<InUpRes> DecideAsync(RequestDecideReq d, CancellationToken ct = default)
        {
            var r = await LoadAsync(d.Id, ct);
            await Guard.EnsureAsync(Check(r, Actions.Approve), ct);
            if (d.Approve)
            {
                var person = await _people.FindAsync(r.RequestedByEmpCode, ct) ?? throw AmsException.NotFound($"Employee {r.RequestedByEmpCode}");
                var asset = Found(await _assets.GetAsync(Tenant, r.AssetId, ct), "Asset");
                await Guard.EnsureAsync(new AccessCheck(Modules.Assets, Resources.Asset, Actions.Checkout, ResourceAttrs.For(asset).WithAssignee(person)), ct);
            }
            else Require(!string.IsNullOrWhiteSpace(d.Note), "Give a reason for the rejection.");
            (await _requests.DecideAsync(Write, d.Id, d.Approve, d.Note, ct)).EnsureOk();
            _notify.Publish(new RequestDecidedModel(r, d.Approve, Me.Name, d.Note));
            return new InUpRes { recordId = d.Id, responseMessage = d.Approve ? "Approved — asset issued" : "Rejected" };
        }

        public async Task<InUpRes> CancelAsync(int id, CancellationToken ct = default)
        {
            var r = await LoadAsync(id, ct);
            await Guard.EnsureAsync(Check(r, Actions.Void), ct);
            (await _requests.CancelAsync(Write, id, ct)).EnsureOk();
            return new InUpRes { recordId = id, responseMessage = "Request cancelled" };
        }
    }

    /// <summary>Software licences and their seats (given to a person and/or installed on an asset).</summary>
    public sealed class LicenseUseCases : UseCaseBase
    {
        private readonly ILicenseRepository _licenses;
        private readonly IAssetRepository _assets;
        private readonly IPeopleDirectory _people;

        public LicenseUseCases(IAccessGuard guard, ICurrentActor current, IBusinessClock clock, ILicenseRepository licenses, IAssetRepository assets, IPeopleDirectory people)
            : base(guard, current, clock)
        {
            _licenses = licenses;
            _assets = assets;
            _people = people;
        }

        private static AccessCheck Check(LicenseView l, string action) => new(Modules.Licenses, Resources.License, action, ResourceAttrs.For(l));

        private async Task<LicenseView> LoadAsync(int id, CancellationToken ct) => Found((await _licenses.ListAsync(Tenant, null, id, ct)).FirstOrDefault(), "Licence");

        /// <summary>Licences the user may view; product keys only for those UMS allows to edit the licence.</summary>
        public async Task<LicenseListRes> ListAsync(string? search, CancellationToken ct = default)
        {
            var visible = await Guard.FilterAsync(await _licenses.ListAsync(Tenant, search, null, ct), l => Check(l, Actions.View), ct);
            var m = await Guard.MatrixAsync(visible, new[] { Actions.Edit, Actions.Delete, Actions.Checkout, Actions.Checkin }, Check, ct);
            return new LicenseListRes
            {
                Rows = visible.Select((l, i) =>
                {
                    if (!m[i][0]) l.ProductKey = null;
                    return new LicenseRow(l, m[i][0], m[i][1], m[i][2] && l.Free > 0, m[i][3] && l.Used > 0);
                }).ToList()
            };
        }

        public async Task<InUpRes> SaveAsync(LicenseSaveReq r, CancellationToken ct = default)
        {
            var after = new LicenseView { Name = r.Name, Seats = r.Seats, PurchaseCost = r.PurchaseCost };
            if (r.Id == 0) await Guard.EnsureAsync(Check(after, Actions.Create), ct);
            else
            {
                var before = await LoadAsync(r.Id, ct);
                await Guard.EnsureAsync(Check(before, Actions.Edit), ct);
                after.CategoryName = before.CategoryId == r.CategoryId ? before.CategoryName : null;
                await Guard.EnsureAsync(Check(after, Actions.Edit), ct);
            }
            var res = (await _licenses.SaveAsync(Write, r, ct)).EnsureOk();
            return new InUpRes { recordId = res.RecordId, responseMessage = "Licence saved" };
        }

        public async Task<InUpRes> DeleteAsync(int id, CancellationToken ct = default)
        {
            var l = await LoadAsync(id, ct);
            await Guard.EnsureAsync(Check(l, Actions.Delete), ct);
            (await _licenses.DeleteAsync(Write, id, ct)).EnsureOk();
            return new InUpRes { recordId = id, responseMessage = "Licence deleted" };
        }

        public async Task<HoldersRes> SeatsAsync(int licenseId, CancellationToken ct = default)
        {
            var l = await LoadAsync(licenseId, ct);
            await Guard.EnsureAsync(Check(l, Actions.View), ct);
            var canCheckin = await Guard.CanAsync(Check(l, Actions.Checkin), ct);
            return new HoldersRes
            {
                Holders = (await _licenses.SeatsAsync(Tenant, licenseId, null, null, ct)).Select(s => new HolderRow(s.Id,
                    string.Join(" · ", new[] { s.AssignedToName == null ? null : $"{s.AssignedToName} ({s.AssignedToEmpCode})", s.AssetTag == null ? null : $"{s.AssetTag} {s.AssetName}" }
                        .Where(x => x != null)), 1, s.AssignedAt, canCheckin)).ToList()
            };
        }

        /// <summary>Gives one seat to a person and/or installs it on an asset.</summary>
        public async Task<InUpRes> CheckoutAsync(LicenseCheckoutReq r, CancellationToken ct = default)
        {
            Require(!string.IsNullOrWhiteSpace(r.EmpCode) || r.AssetId != null, "Choose a person or an asset.");
            var person = string.IsNullOrWhiteSpace(r.EmpCode) ? null : await _people.FindAsync(r.EmpCode, ct) ?? throw AmsException.NotFound($"Employee {r.EmpCode}");
            var asset = r.AssetId == null ? null : Found(await _assets.GetAsync(Tenant, r.AssetId.Value, ct), "Asset");
            var l = await LoadAsync(r.LicenseId, ct);
            var attrs = ResourceAttrs.For(l);
            if (person != null) attrs.WithAssignee(person);
            if (asset != null) attrs["location"] = asset.LocationCode;
            await Guard.EnsureAsync(new AccessCheck(Modules.Licenses, Resources.License, Actions.Checkout, attrs), ct);
            var res = (await _licenses.CheckoutAsync(Write, r.LicenseId, person, r.AssetId, r.Note, ct)).EnsureOk();
            return new InUpRes { recordId = res.RecordId, responseMessage = "Seat checked out" };
        }

        public async Task<InUpRes> CheckinAsync(LicenseCheckinReq r, CancellationToken ct = default)
        {
            var seat = Found((await _licenses.SeatsAsync(Tenant, null, r.SeatId, null, ct)).FirstOrDefault(), "Seat");
            var attrs = ResourceAttrs.For(await LoadAsync(seat.LicenseId, ct));
            if (seat.LocationCode != null) attrs["location"] = seat.LocationCode;
            if (seat.AssignedToEmpCode != null) attrs["assignedToEmpCode"] = seat.AssignedToEmpCode;
            await Guard.EnsureAsync(new AccessCheck(Modules.Licenses, Resources.License, Actions.Checkin, attrs), ct);
            (await _licenses.CheckinAsync(Write, r.SeatId, r.Note, ct)).EnsureOk();
            return new InUpRes { recordId = r.SeatId, responseMessage = "Seat checked in" };
        }

        public async Task<FileRes> ExportAsync(string? search, CancellationToken ct = default)
        {
            await Ensure(Modules.Licenses, Resources.License, Actions.Export, ct: ct);
            var rows = (await ListAsync(search, ct)).Rows;
            return new FileRes
            {
                FileName = $"licences-{Clock.Now:yyyyMMdd-HHmm}.csv",
                Content = Csv.Write(new[] { "Licence", "Manufacturer", "Seats", "Used", "Free", "Purchase date", "Cost", "Expires" },
                    rows.Select(r => new object?[] { r.License.Name, r.License.ManufacturerName, r.License.Seats, r.License.Used, r.License.Free, r.License.PurchaseDate,
                        r.License.PurchaseCost, r.License.ExpirationDate }))
            };
        }
    }

    /// <summary>
    /// Quantity-tracked items: accessories are lent to people and returned, consumables are issued and used up, components are installed
    /// into assets. Each kind is its own UMS module, so access is granted separately.
    /// </summary>
    public sealed class StockUseCases : UseCaseBase
    {
        private readonly IStockRepository _stock;
        private readonly IAssetRepository _assets;
        private readonly IPeopleDirectory _people;
        private readonly ISetupRepository _setup;

        public StockUseCases(IAccessGuard guard, ICurrentActor current, IBusinessClock clock, IStockRepository stock, IAssetRepository assets, IPeopleDirectory people,
            ISetupRepository setup) : base(guard, current, clock)
        {
            _stock = stock;
            _assets = assets;
            _people = people;
            _setup = setup;
        }

        public static (string Module, string Resource) Catalogue(StockKind kind) => Domain.Security.Perm.Stock(kind);

        private static AccessCheck Check(StockItemView s, string action, int quantity = 1)
        {
            var (module, resource) = Catalogue(s.Kind);
            return new AccessCheck(module, resource, action, ResourceAttrs.For(s, quantity));
        }

        private async Task<StockItemView> LoadAsync(int id, CancellationToken ct) => Found((await _stock.ListAsync(Tenant, null, null, id, ct)).FirstOrDefault(), "Item");

        public async Task<StockListRes> ListAsync(StockKind kind, string? search, CancellationToken ct = default)
        {
            var visible = await Guard.FilterAsync(await _stock.ListAsync(Tenant, kind, search, null, ct), s => Check(s, Actions.View), ct);
            var m = await Guard.MatrixAsync(visible, new[] { Actions.Edit, Actions.Delete, Actions.Checkout, Actions.Checkin }, (s, a) => Check(s, a), ct);
            return new StockListRes
            {
                Rows = visible.Select((s, i) => new StockRow(s, m[i][0], m[i][1], m[i][2] && s.Available > 0, m[i][3] && kind != StockKind.Consumable && s.InUse > 0)).ToList()
            };
        }

        public async Task<HoldersRes> HoldersAsync(int itemId, CancellationToken ct = default)
        {
            var s = await LoadAsync(itemId, ct);
            await Guard.EnsureAsync(Check(s, Actions.View), ct);
            var canCheckin = s.Kind != StockKind.Consumable && await Guard.CanAsync(Check(s, Actions.Checkin), ct);
            return new HoldersRes
            {
                Holders = (await _stock.HoldersAsync(Tenant, itemId, null, ct)).Select(h => new HolderRow(h.Id,
                    h.AssetId != null ? $"{h.AssetTag} {h.AssetName}" : $"{h.EmpName} ({h.EmpCode})", h.Quantity, h.AssignedAt, canCheckin)).ToList()
            };
        }

        public async Task<InUpRes> SaveAsync(StockSaveReq r, CancellationToken ct = default)
        {
            var location = (await _setup.OptionsAsync(Tenant, ct)).Locations.FirstOrDefault(l => l.Id == r.LocationId) ?? throw AmsException.Rule("Choose a location.");
            var after = new StockItemView { Kind = r.Kind, Name = r.Name, UnitCost = r.UnitCost, LocationCode = location.Code ?? "", Region = location.Region ?? "" };
            if (r.Id == 0) await Guard.EnsureAsync(Check(after, Actions.Create, Math.Max(1, r.Quantity)), ct);
            else
            {
                var before = await LoadAsync(r.Id, ct);
                Require(before.Kind == r.Kind, "The kind of an item cannot change.");
                await Guard.EnsureAsync(Check(before, Actions.Edit), ct);
                after.CategoryName = before.CategoryName;
                await Guard.EnsureAsync(Check(after, Actions.Edit), ct);
            }
            var res = (await _stock.SaveAsync(Write, r, ct)).EnsureOk();
            return new InUpRes { recordId = res.RecordId, responseMessage = $"{r.Kind} saved" };
        }

        public async Task<InUpRes> DeleteAsync(int id, CancellationToken ct = default)
        {
            var s = await LoadAsync(id, ct);
            await Guard.EnsureAsync(Check(s, Actions.Delete, Math.Max(1, s.Quantity)), ct);
            (await _stock.DeleteAsync(Write, id, ct)).EnsureOk();
            return new InUpRes { recordId = id, responseMessage = $"{s.Kind} deleted" };
        }

        /// <summary>Accessory → lend one to a person · Consumable → issue a quantity to a person · Component → install a quantity into an asset.</summary>
        public async Task<InUpRes> CheckoutAsync(StockCheckoutReq r, CancellationToken ct = default)
        {
            var s = await LoadAsync(r.StockItemId, ct);
            var quantity = s.Kind == StockKind.Accessory ? 1 : r.Quantity;
            Require(quantity >= 1, "Quantity must be at least 1.");
            Person? person = null;
            AssetView? asset = null;
            if (s.Kind == StockKind.Component)
            {
                Require(r.AssetId != null, "Choose the asset the component goes into.");
                asset = Found(await _assets.GetAsync(Tenant, r.AssetId!.Value, ct), "Asset");
            }
            else
            {
                Require(!string.IsNullOrWhiteSpace(r.EmpCode), "Choose a person.");
                person = await _people.FindAsync(r.EmpCode!, ct) ?? throw AmsException.NotFound($"Employee {r.EmpCode}");
            }
            var (module, resource) = Catalogue(s.Kind);
            var attrs = ResourceAttrs.For(s, quantity);
            if (person != null) attrs.WithAssignee(person);
            if (asset != null) { attrs["assetTag"] = asset.AssetTag; attrs["assetLocation"] = asset.LocationCode; }
            await Guard.EnsureAsync(new AccessCheck(module, resource, Actions.Checkout, attrs), ct);
            var res = (await _stock.CheckoutAsync(Write, s.Id, person, asset?.Id, quantity, r.Note, ct)).EnsureOk();
            return new InUpRes { recordId = res.RecordId, responseMessage = "Done" };
        }

        /// <summary>Returns a lent accessory or removes an installed component (back into stock).</summary>
        public async Task<InUpRes> CheckinAsync(StockCheckinReq r, CancellationToken ct = default)
        {
            var s = await LoadAsync(r.StockItemId, ct);
            Require(s.Kind != StockKind.Consumable, "Consumables are used up and cannot be checked in.");
            await Guard.EnsureAsync(Check(s, Actions.Checkin), ct);
            (await _stock.CheckinAsync(Write, r.StockItemId, r.AssignmentId, r.Note, ct)).EnsureOk();
            return new InUpRes { recordId = r.AssignmentId, responseMessage = "Returned to stock" };
        }
    }
}

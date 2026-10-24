using AMS.Application.Common;
using AMS.Application.Interfaces;
using AMS.Application.Notifications;
using AMS.Domain.Enums;
using AMS.Domain.Exceptions;
using AMS.Domain.Models;
using AMS.Domain.Requests;
using AMS.Domain.Responses;
using AMS.Domain.Security;
using static AMS.Domain.Security.Perm;

namespace AMS.Application.Services
{
    /// <summary>Assets: list, view, create/edit/delete, check-out/in, audit, labels, export, my assets.</summary>
    public sealed class AssetUseCases : UseCaseBase
    {
        private static readonly string[] RowActions = { Actions.Edit, Actions.Delete, Actions.Checkout, Actions.Checkin, Actions.Audit };

        private readonly IAssetRepository _assets;
        private readonly ISetupRepository _setup;
        private readonly IMaintenanceRepository _maintenance;
        private readonly ILicenseRepository _licenses;
        private readonly IStockRepository _stock;
        private readonly IHistoryRepository _history;
        private readonly IPeopleDirectory _people;
        private readonly INotificationPublisher _notify;

        public AssetUseCases(IAccessGuard guard, ICurrentActor current, IBusinessClock clock, IAssetRepository assets, ISetupRepository setup,
            IMaintenanceRepository maintenance, ILicenseRepository licenses, IStockRepository stock, IHistoryRepository history, IPeopleDirectory people,
            INotificationPublisher notify) : base(guard, current, clock)
        {
            _assets = assets;
            _setup = setup;
            _maintenance = maintenance;
            _licenses = licenses;
            _stock = stock;
            _history = history;
            _people = people;
            _notify = notify;
        }

        public static AccessCheck Check(AssetView a, string action) => new(Modules.Assets, Resources.Asset, action, ResourceAttrs.For(a));

        private async Task<AssetView> LoadAsync(int id, CancellationToken ct) => Found(await _assets.GetAsync(Tenant, id, ct), "Asset");

        /// <summary>Assets the user may view (UMS decides per asset: role scope, "own assets" grant…) with the actions allowed on each.</summary>
        public async Task<AssetListRes> ListAsync(AssetFilterReq filter, CancellationToken ct = default)
        {
            var visible = await VisibleAsync(filter, ct);
            return new AssetListRes { Rows = await RowsAsync(visible, ct) };
        }

        internal async Task<List<AssetView>> VisibleAsync(AssetFilterReq filter, CancellationToken ct, string module = Modules.Assets, string resource = Resources.Asset)
        {
            var all = await _assets.ListAsync(Tenant, filter, false, ct);
            return await Guard.FilterAsync(all, a => new AccessCheck(module, resource, Actions.View, ResourceAttrs.For(a)), ct);
        }

        private async Task<List<AssetRow>> RowsAsync(List<AssetView> assets, CancellationToken ct)
        {
            var m = await Guard.MatrixAsync(assets, RowActions, Check, ct);
            return assets.Select((a, i) => new AssetRow(a, m[i][0], m[i][1], m[i][2] && !a.IsCheckedOut && a.StatusType == StatusType.Deployable,
                m[i][3] && a.IsCheckedOut, m[i][4])).ToList();
        }

        public async Task<AssetDetailRes> GetAsync(int id, CancellationToken ct = default)
        {
            var a = await LoadAsync(id, ct);
            await Guard.EnsureAsync(Check(a, Actions.View), ct);
            var row = (await RowsAsync(new List<AssetView> { a }, ct))[0];
            var canMaintain = await Guard.CanAsync(new AccessCheck(Modules.Assets, Resources.Maintenance, Actions.Create, ResourceAttrs.For(a)), ct);
            return new AssetDetailRes
            {
                Asset = row,
                History = await _history.ListAsync(Tenant, ItemType.Asset, id, null, null, null, 200, ct),
                Maintenances = await _maintenance.ListAsync(Tenant, false, id, null, ct),
                Licenses = await _licenses.SeatsAsync(Tenant, null, null, id, ct),
                Components = await _stock.HoldersAsync(Tenant, null, id, ct),
                CanMaintain = canMaintain,
                CurrentValue = Depreciation.CurrentValue(a, Clock.Today, out _),
            };
        }

        /// <summary>The asset as it would be after saving, so UMS can judge it by its new location, cost and category.</summary>
        private async Task<AssetView> PreviewAsync(AssetSaveReq r, CancellationToken ct)
        {
            var o = await _setup.OptionsAsync(Tenant, ct);
            var model = o.Models.FirstOrDefault(x => x.Id == r.ModelId) ?? throw AmsException.Rule("Choose a model.");
            var status = o.Statuses.FirstOrDefault(x => x.Id == r.StatusLabelId) ?? throw AmsException.Rule("Choose a status.");
            var location = o.Locations.FirstOrDefault(x => x.Id == r.LocationId) ?? throw AmsException.Rule("Choose a location.");
            return new AssetView
            {
                Id = r.Id, AssetTag = r.AssetTag?.Trim().ToUpperInvariant() ?? "", Name = r.Name?.Trim() ?? "", PurchaseCost = r.PurchaseCost, Requestable = r.Requestable,
                ModelId = model.Id, ModelName = model.Name, CategoryId = model.CategoryId ?? 0, CategoryName = model.CategoryName ?? "",
                StatusLabelId = status.Id, StatusName = status.Name, StatusType = status.StatusType ?? StatusType.Deployable,
                LocationId = location.Id, LocationCode = location.Code ?? "", LocationName = location.Name, Region = location.Region ?? "",
                DepartmentName = o.Departments.FirstOrDefault(x => x.Id == r.DepartmentId)?.Name,
            };
        }

        public async Task<Domain.Base.InUpRes> SaveAsync(AssetSaveReq r, CancellationToken ct = default)
        {
            var after = await PreviewAsync(r, ct);
            if (r.Id == 0)
            {
                // the new record's attributes go to UMS: a manager scoped to one location can only create assets there
                await Guard.EnsureAsync(Check(after, Actions.Create), ct);
            }
            else
            {
                var before = await LoadAsync(r.Id, ct);
                await Guard.EnsureAsync(Check(before, Actions.Edit), ct);   // allowed to edit it as it is…
                after.AssignedToEmpCode = before.AssignedToEmpCode;
                await Guard.EnsureAsync(Check(after, Actions.Edit), ct);    // …and as it will be (e.g. moved to another location)
            }
            var res = (await _assets.SaveAsync(Write, r, ct)).EnsureOk();
            return new Domain.Base.InUpRes { recordId = res.RecordId, responseMessage = $"Asset {after.AssetTag} saved" };
        }

        public async Task<Domain.Base.InUpRes> DeleteAsync(int id, CancellationToken ct = default)
        {
            var a = await LoadAsync(id, ct);
            await Guard.EnsureAsync(Check(a, Actions.Delete), ct);
            (await _assets.DeleteAsync(Write, id, ct)).EnsureOk();
            return new Domain.Base.InUpRes { recordId = id, responseMessage = $"{a.AssetTag} deleted" };
        }

        public async Task<Domain.Base.InUpRes> CheckoutAsync(AssetCheckoutReq r, CancellationToken ct = default)
        {
            Require(!string.IsNullOrWhiteSpace(r.EmpCode), "Choose an employee.");
            var person = await _people.FindAsync(r.EmpCode, ct) ?? throw AmsException.NotFound($"Employee {r.EmpCode}");
            Require(person.Status == "Active", $"{person.FullName} is not an active employee.");
            var a = await LoadAsync(r.Id, ct);
            // UMS sees both the asset and the person it goes to (employment type, department, location)
            await Guard.EnsureAsync(new AccessCheck(Modules.Assets, Resources.Asset, Actions.Checkout, ResourceAttrs.For(a).WithAssignee(person)), ct);
            Require(r.ExpectedCheckin == null || r.ExpectedCheckin.Value.Date >= Clock.Today, "The return date cannot be in the past.");
            (await _assets.CheckoutAsync(Write, r.Id, person, r.ExpectedCheckin?.Date, r.Note, ct)).EnsureOk();
            _notify.Publish(new AssetCheckedOutModel(a, person, Me.Name, r.ExpectedCheckin?.Date, r.Note));
            return new Domain.Base.InUpRes { recordId = r.Id, responseMessage = $"{a.AssetTag} checked out to {person.FullName}" };
        }

        public async Task<Domain.Base.InUpRes> CheckinAsync(AssetCheckinReq r, CancellationToken ct = default)
        {
            var a = await LoadAsync(r.Id, ct);
            await Guard.EnsureAsync(Check(a, Actions.Checkin), ct);
            (await _assets.CheckinAsync(Write, r.Id, r.StatusLabelId, r.Note, ct)).EnsureOk();
            if (a.AssignedToName != null) _notify.Publish(new AssetCheckedInModel(a, a.AssignedToName, a.AssignedToEmail, Me.Name, r.Note));
            return new Domain.Base.InUpRes { recordId = r.Id, responseMessage = $"{a.AssetTag} checked in" };
        }

        /// <summary>Physical verification: confirms the asset was seen and schedules the next audit.</summary>
        public async Task<Domain.Base.InUpRes> AuditAsync(AssetAuditReq r, CancellationToken ct = default)
        {
            var a = await LoadAsync(r.Id, ct);
            await Guard.EnsureAsync(Check(a, Actions.Audit), ct);
            (await _assets.AuditAsync(Write, r.Id, r.NextAuditDate?.Date, r.Note, ct)).EnsureOk();
            return new Domain.Base.InUpRes { recordId = r.Id, responseMessage = $"Audit of {a.AssetTag} recorded" };
        }

        /// <summary>CSV of the assets the user may view; exporting is its own UMS permission (and blocked on unmanaged devices by policy).</summary>
        public async Task<FileRes> ExportAsync(AssetFilterReq filter, CancellationToken ct = default)
        {
            await Ensure(Modules.Assets, Resources.Asset, Actions.Export, ct: ct);
            var rows = await VisibleAsync(filter, ct);
            return new FileRes
            {
                FileName = $"assets-{Clock.Now:yyyyMMdd-HHmm}.csv",
                Content = Csv.Write(new[] { "Asset tag", "Name", "Serial", "Model", "Category", "Status", "Location", "Assigned to", "Purchase date", "Cost", "Warranty expires", "Next audit" },
                    rows.Select(a => new object?[] { a.AssetTag, a.Name, a.Serial, a.ModelName, a.CategoryName, a.StatusName, a.LocationCode, a.AssignedToName,
                        a.PurchaseDate, a.PurchaseCost, a.WarrantyExpires, a.NextAuditDate }))
            };
        }

        /// <summary>Assets to print tag labels for (Print permission, checked per asset).</summary>
        public async Task<AssetsRes> LabelsAsync(AssetFilterReq filter, CancellationToken ct = default)
        {
            await Ensure(Modules.Assets, Resources.Asset, Actions.Print, ct: ct);
            var rows = await VisibleAsync(filter, ct);
            return new AssetsRes { Assets = await Guard.FilterAsync(rows, a => Check(a, Actions.Print), ct) };
        }

        /// <summary>Assets the user may view, for pickers (licence seat on an asset, component installation, maintenance).</summary>
        public async Task<AssetsRes> ChoicesAsync(CancellationToken ct = default) => new() { Assets = await VisibleAsync(new AssetFilterReq(), ct) };

        public async Task<PeopleRes> PeopleAsync(string? search, CancellationToken ct = default) =>
            new() { People = (await _people.SearchAsync(search, 30, ct)).ToList() };

        /// <summary>What is issued to me (assets, licence seats, accessories, consumables).</summary>
        public async Task<MyItemsRes> MyItemsAsync(CancellationToken ct = default)
        {
            await Ensure(Modules.Self, Resources.MyAssets, Actions.View, ct: ct);
            var m = await _assets.MyItemsAsync(Tenant, Me.EmpCode, ct);
            return new MyItemsRes { Assets = m.Assets, Licenses = m.Licenses, Accessories = m.Accessories, Consumables = m.Consumables };
        }
    }
}

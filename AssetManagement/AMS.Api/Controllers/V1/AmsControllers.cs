using AMS.Application.Services;
using AMS.Domain.Base;
using AMS.Domain.Models;
using AMS.Domain.Requests;
using AMS.Domain.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMS.Api.Controllers.V1
{
    // AMS API, same style as the other VMM APIs: POST api/v1/{Action} with a request body, a BaseResponse-derived body back.
    // [Authorize] only proves a valid UMS token; what the caller may do is decided by UMS inside each use case.

    [ApiController]
    [Authorize]
    [Route("api/v1/[action]")]
    [Produces("application/json")]
    public abstract class AmsControllerBase : ControllerBase { }

    public sealed class AssetController : AmsControllerBase
    {
        private readonly AssetUseCases _assets;
        public AssetController(AssetUseCases assets) => _assets = assets;

        [HttpPost] public Task<AssetListRes> GetAssets(AssetFilterReq req, CancellationToken ct) => _assets.ListAsync(req, ct);
        [HttpPost] public Task<AssetDetailRes> GetAsset(IdReq req, CancellationToken ct) => _assets.GetAsync(req.Id, ct);
        [HttpPost] public Task<InUpRes> SaveAsset(AssetSaveReq req, CancellationToken ct) => _assets.SaveAsync(req, ct);
        [HttpPost] public Task<InUpRes> DeleteAsset(IdReq req, CancellationToken ct) => _assets.DeleteAsync(req.Id, ct);
        [HttpPost] public Task<InUpRes> CheckoutAsset(AssetCheckoutReq req, CancellationToken ct) => _assets.CheckoutAsync(req, ct);
        [HttpPost] public Task<InUpRes> CheckinAsset(AssetCheckinReq req, CancellationToken ct) => _assets.CheckinAsync(req, ct);
        [HttpPost] public Task<InUpRes> AuditAsset(AssetAuditReq req, CancellationToken ct) => _assets.AuditAsync(req, ct);
        [HttpPost] public Task<FileRes> ExportAssets(AssetFilterReq req, CancellationToken ct) => _assets.ExportAsync(req, ct);
        [HttpPost] public Task<AssetsRes> GetAssetLabels(AssetFilterReq req, CancellationToken ct) => _assets.LabelsAsync(req, ct);
        [HttpPost] public Task<AssetsRes> GetAssetChoices(CancellationToken ct) => _assets.ChoicesAsync(ct);
        [HttpPost] public Task<PeopleRes> GetPeople(SearchReq req, CancellationToken ct) => _assets.PeopleAsync(req.Search, ct);
        [HttpPost] public Task<MyItemsRes> GetMyItems(CancellationToken ct) => _assets.MyItemsAsync(ct);
    }

    public sealed class MaintenanceController : AmsControllerBase
    {
        private readonly MaintenanceUseCases _maintenance;
        public MaintenanceController(MaintenanceUseCases maintenance) => _maintenance = maintenance;

        [HttpPost] public Task<MaintenanceListRes> GetMaintenance(MaintenanceListReq req, CancellationToken ct) => _maintenance.ListAsync(req.OpenOnly, ct);
        [HttpPost] public Task<InUpRes> SaveMaintenance(MaintenanceSaveReq req, CancellationToken ct) => _maintenance.SaveAsync(req, ct);
        [HttpPost] public Task<InUpRes> DeleteMaintenance(IdReq req, CancellationToken ct) => _maintenance.DeleteAsync(req.Id, ct);
    }

    public sealed class RequestController : AmsControllerBase
    {
        private readonly RequestUseCases _requests;
        public RequestController(RequestUseCases requests) => _requests = requests;

        [HttpPost] public Task<RequestListRes> GetRequests(RequestListReq req, CancellationToken ct) => _requests.ListAsync(req.Status, ct);
        [HttpPost] public Task<AssetsRes> GetRequestableAssets(CancellationToken ct) => _requests.RequestableAsync(ct);
        [HttpPost] public Task<InUpRes> CreateRequest(RequestCreateReq req, CancellationToken ct) => _requests.CreateAsync(req, ct);
        [HttpPost] public Task<InUpRes> DecideRequest(RequestDecideReq req, CancellationToken ct) => _requests.DecideAsync(req, ct);
        [HttpPost] public Task<InUpRes> CancelRequest(IdReq req, CancellationToken ct) => _requests.CancelAsync(req.Id, ct);
    }

    public sealed class LicenseController : AmsControllerBase
    {
        private readonly LicenseUseCases _licenses;
        public LicenseController(LicenseUseCases licenses) => _licenses = licenses;

        [HttpPost] public Task<LicenseListRes> GetLicenses(SearchReq req, CancellationToken ct) => _licenses.ListAsync(req.Search, ct);
        [HttpPost] public Task<InUpRes> SaveLicense(LicenseSaveReq req, CancellationToken ct) => _licenses.SaveAsync(req, ct);
        [HttpPost] public Task<InUpRes> DeleteLicense(IdReq req, CancellationToken ct) => _licenses.DeleteAsync(req.Id, ct);
        [HttpPost] public Task<HoldersRes> GetLicenseSeats(IdReq req, CancellationToken ct) => _licenses.SeatsAsync(req.Id, ct);
        [HttpPost] public Task<InUpRes> CheckoutLicense(LicenseCheckoutReq req, CancellationToken ct) => _licenses.CheckoutAsync(req, ct);
        [HttpPost] public Task<InUpRes> CheckinLicense(LicenseCheckinReq req, CancellationToken ct) => _licenses.CheckinAsync(req, ct);
        [HttpPost] public Task<FileRes> ExportLicenses(SearchReq req, CancellationToken ct) => _licenses.ExportAsync(req.Search, ct);
    }

    public sealed class StockController : AmsControllerBase
    {
        private readonly StockUseCases _stock;
        public StockController(StockUseCases stock) => _stock = stock;

        [HttpPost] public Task<StockListRes> GetStock(StockListReq req, CancellationToken ct) => _stock.ListAsync(req.Kind, req.Search, ct);
        [HttpPost] public Task<InUpRes> SaveStock(StockSaveReq req, CancellationToken ct) => _stock.SaveAsync(req, ct);
        [HttpPost] public Task<InUpRes> DeleteStock(IdReq req, CancellationToken ct) => _stock.DeleteAsync(req.Id, ct);
        [HttpPost] public Task<HoldersRes> GetStockHolders(IdReq req, CancellationToken ct) => _stock.HoldersAsync(req.Id, ct);
        [HttpPost] public Task<InUpRes> CheckoutStock(StockCheckoutReq req, CancellationToken ct) => _stock.CheckoutAsync(req, ct);
        [HttpPost] public Task<InUpRes> CheckinStock(StockCheckinReq req, CancellationToken ct) => _stock.CheckinAsync(req, ct);
    }

    public sealed class SetupController : AmsControllerBase
    {
        private readonly SetupUseCases _setup;
        public SetupController(SetupUseCases setup) => _setup = setup;

        [HttpPost] public Task<SetupListRes> GetSetup(SetupListReq req, CancellationToken ct) => _setup.ListAsync(req.Kind, ct);
        [HttpPost] public Task<InUpRes> SaveSetup(SetupItem req, CancellationToken ct) => _setup.SaveAsync(req, ct);
        [HttpPost] public Task<InUpRes> DeleteSetup(SetupDeleteReq req, CancellationToken ct) => _setup.DeleteAsync(req.Kind, req.Id, ct);
        [HttpPost] public Task<OptionsRes> GetOptions(CancellationToken ct) => _setup.OptionsAsync(ct);
    }

    public sealed class ReportController : AmsControllerBase
    {
        private readonly ReportUseCases _reports;
        public ReportController(ReportUseCases reports) => _reports = reports;

        [HttpPost] public Task<CapabilitiesRes> GetCapabilities(CancellationToken ct) => _reports.CapabilitiesAsync(ct);
        [HttpPost] public Task<DashboardRes> GetDashboard(CancellationToken ct) => _reports.DashboardAsync(ct);
        [HttpPost] public Task<AssetReportRes> GetAssetReport(CancellationToken ct) => _reports.AssetReportAsync(ct);
        [HttpPost] public Task<FileRes> ExportAssetReport(CancellationToken ct) => _reports.AssetReportCsvAsync(ct);
        [HttpPost] public Task<ActivityRes> GetActivity(ActivityReq req, CancellationToken ct) => _reports.ActivityAsync(req, ct);
        [HttpPost] public Task<FileRes> ExportActivity(ActivityReq req, CancellationToken ct) => _reports.ActivityCsvAsync(req, ct);
    }
}

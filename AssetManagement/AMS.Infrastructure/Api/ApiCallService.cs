using AMS.Application.Interfaces;
using AMS.Domain.Base;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AMS.Infrastructure.Api
{
    /// <summary>
    /// Names of the AMS API operations used by the portal (like AllApiNames in the other VMM portals). Each maps to api/v1/{name} unless
    /// "AmsApi:Routes" in the portal settings overrides it, so routes can move without changing pages.
    /// </summary>
    public static class AmsApiNames
    {
        public const string GET_CAPABILITIES = "GetCapabilities", GET_DASHBOARD = "GetDashboard", GET_OPTIONS = "GetOptions", GET_PEOPLE = "GetPeople";
        public const string GET_ASSETS = "GetAssets", GET_ASSET = "GetAsset", SAVE_ASSET = "SaveAsset", DELETE_ASSET = "DeleteAsset", CHECKOUT_ASSET = "CheckoutAsset",
            CHECKIN_ASSET = "CheckinAsset", AUDIT_ASSET = "AuditAsset", EXPORT_ASSETS = "ExportAssets", GET_ASSET_LABELS = "GetAssetLabels",
            GET_ASSET_CHOICES = "GetAssetChoices", GET_MY_ITEMS = "GetMyItems";
        public const string GET_MAINTENANCE = "GetMaintenance", SAVE_MAINTENANCE = "SaveMaintenance", DELETE_MAINTENANCE = "DeleteMaintenance";
        public const string GET_REQUESTS = "GetRequests", GET_REQUESTABLE = "GetRequestableAssets", CREATE_REQUEST = "CreateRequest", DECIDE_REQUEST = "DecideRequest",
            CANCEL_REQUEST = "CancelRequest";
        public const string GET_LICENSES = "GetLicenses", SAVE_LICENSE = "SaveLicense", DELETE_LICENSE = "DeleteLicense", GET_LICENSE_SEATS = "GetLicenseSeats",
            CHECKOUT_LICENSE = "CheckoutLicense", CHECKIN_LICENSE = "CheckinLicense", EXPORT_LICENSES = "ExportLicenses";
        public const string GET_STOCK = "GetStock", SAVE_STOCK = "SaveStock", DELETE_STOCK = "DeleteStock", GET_STOCK_HOLDERS = "GetStockHolders",
            CHECKOUT_STOCK = "CheckoutStock", CHECKIN_STOCK = "CheckinStock";
        public const string GET_SETUP = "GetSetup", SAVE_SETUP = "SaveSetup", DELETE_SETUP = "DeleteSetup";
        public const string GET_ASSET_REPORT = "GetAssetReport", EXPORT_ASSET_REPORT = "ExportAssetReport", GET_ACTIVITY = "GetActivity", EXPORT_ACTIVITY = "ExportActivity";
    }

    /// <summary>"AmsApi" section of the portal settings.</summary>
    public sealed class AmsApiOptions
    {
        public const string SectionName = "AmsApi";
        public string BaseUrl { get; set; } = "";
        public int TimeoutSeconds { get; set; } = 60;
        /// <summary>Optional overrides: api name → relative route.</summary>
        public Dictionary<string, string> Routes { get; set; } = new();

        public string RouteFor(string apiName) => Routes.TryGetValue(apiName, out var r) ? r : $"api/v1/{apiName}";
    }

    /// <summary>
    /// The portal's single way to call the AMS API: POSTs the request as JSON with the user's UMS access token and returns the typed
    /// response. Transport failures, 401/403 and server errors come back as a response with responseCode/responseMessage set.
    /// </summary>
    public sealed class ApiCallService : IAPICall
    {
        /// <summary>Forwarded to the API so UMS sees the browser's address (env.ip / env.network).</summary>
        public const string ClientIpHeader = "X-Client-IP";
        public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNamingPolicy = null, Converters = { new JsonStringEnumConverter() } };

        private readonly HttpClient _http;
        private readonly AmsApiOptions _options;
        private readonly IAccessTokenProvider _tokens;
        private readonly ILogger<ApiCallService> _logger;

        public ApiCallService(HttpClient http, AmsApiOptions options, IAccessTokenProvider tokens, ILogger<ApiCallService> logger)
        {
            _http = http;
            _options = options;
            _tokens = tokens;
            _logger = logger;
        }

        public async Task<TRes> CallingAPI<TRes, TReq>(string apiName, TReq requestBody, CancellationToken ct = default) where TRes : BaseResponse, new()
        {
            var route = _options.RouteFor(apiName);
            try
            {
                var token = await _tokens.GetAccessTokenAsync(ct);
                if (token == null) return Fail<TRes>(ResponseCodes.Unauthorized, "Please sign in.");
                using var req = new HttpRequestMessage(HttpMethod.Post, route) { Content = JsonContent.Create(requestBody, options: Json) };
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                if (!string.IsNullOrEmpty(_tokens.ClientIp)) req.Headers.Add(ClientIpHeader, _tokens.ClientIp);
                using var res = await _http.SendAsync(req, ct);
                if (res.StatusCode == HttpStatusCode.Unauthorized)
                    _tokens.SessionEnded(res.Headers.TryGetValues(Ums.UmsClient.SessionEndHeader, out var v) ? v.FirstOrDefault() ?? "Expired" : "Expired");

                // the API answers with a BaseResponse body for success and for refusals (403 denied, 422 rule broken, 404, 401)
                if (res.Content.Headers.ContentType?.MediaType == "application/json")
                {
                    var body = await res.Content.ReadFromJsonAsync<TRes>(Json, ct);
                    if (body != null && (res.IsSuccessStatusCode || body.responseCode != ResponseCodes.Success)) return body;
                }
                return res.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => Fail<TRes>(ResponseCodes.Unauthorized, "Your session has ended. Please sign in again."),
                    HttpStatusCode.Forbidden => Fail<TRes>(ResponseCodes.Forbidden, "Not allowed."),
                    _ => Fail<TRes>(ResponseCodes.ApiError, $"The Asset Management API returned {(int)res.StatusCode} {res.ReasonPhrase}.")
                };
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException && !ct.IsCancellationRequested)
            {
                _logger.LogError(ex, "Calling {Api} ({Route}) failed", apiName, route);
                return Fail<TRes>(ResponseCodes.ApiError, $"The Asset Management API is not reachable ({ex.Message}).");
            }
        }

        private static TRes Fail<TRes>(int code, string message) where TRes : BaseResponse, new() => new() { responseCode = code, responseMessage = message };
    }
}

using AMS.Application.Common;
using AMS.Application.Interfaces;
using AMS.Application.Notifications;
using AMS.Domain.Base;
using AMS.Domain.Exceptions;
using AMS.Domain.Responses;
using AMS.Domain.Security;
using AMS.Infrastructure.Api;
using AMS.Infrastructure.Ums;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text;
using System.Text.Json;
using static AMS.Tests.TestData;

namespace AMS.Tests
{
    internal sealed class StubHandler : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Calls { get; } = new();
        public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => Json(HttpStatusCode.OK, "{}");

        public static HttpResponseMessage Json(HttpStatusCode status, string json, string? sessionEnd = null)
        {
            var res = new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            if (sessionEnd != null) res.Headers.Add(UmsClient.SessionEndHeader, sessionEnd);
            return res;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls.Add((request, request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct)));
            return Respond(request);
        }
    }

    // The web portal reaches the AMS API only through IAPICall (POST api/v1/{name}); refusals come back as a responseCode, never as exceptions.
    public class ApiCallServiceTests
    {
        private readonly StubHandler _http = new();
        private readonly IAccessTokenProvider _tokens = Substitute.For<IAccessTokenProvider>();

        private ApiCallService Create()
        {
            _tokens.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns("portal-token");
            _tokens.ClientIp.Returns("10.1.2.3");
            return new ApiCallService(new HttpClient(_http) { BaseAddress = new Uri("http://ams-api.test/") }, new AmsApiOptions(), _tokens,
                NullLogger<ApiCallService>.Instance);
        }

        [Fact]
        public async Task Posts_the_request_with_the_users_token_and_reads_the_response()
        {
            _http.Respond = _ => StubHandler.Json(HttpStatusCode.OK, """{"responseCode":0,"responseMessage":"SUCCESS","recordId":7}""");

            var res = await Create().CallingAPI<InUpRes, Domain.Requests.AssetCheckoutReq>(AmsApiNames.CHECKOUT_ASSET,
                new Domain.Requests.AssetCheckoutReq { Id = 7, EmpCode = "VMM10002" });

            Assert.True(res.IsSuccess);
            Assert.Equal(7, res.recordId);
            var (req, body) = _http.Calls.Single();
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal("http://ams-api.test/api/v1/CheckoutAsset", req.RequestUri!.ToString());
            Assert.Equal("portal-token", req.Headers.Authorization!.Parameter);
            Assert.Equal("10.1.2.3", req.Headers.GetValues(ApiCallService.ClientIpHeader).Single());
            using var doc = JsonDocument.Parse(body);
            Assert.Equal("VMM10002", doc.RootElement.GetProperty("EmpCode").GetString());   // same casing as the API models
        }

        [Fact]
        public async Task A_denial_keeps_the_UMS_reason()
        {
            _http.Respond = _ => StubHandler.Json(HttpStatusCode.Forbidden, """{"responseCode":403,"responseMessage":"Module: no permission on module Reports"}""");

            var res = await Create().CallingAPI<AssetReportRes, object>(AmsApiNames.GET_ASSET_REPORT, new { });

            Assert.Equal(ResponseCodes.Forbidden, res.responseCode);
            Assert.Equal("Module: no permission on module Reports", res.responseMessage);
        }

        [Fact]
        public async Task An_ended_session_is_reported_with_its_reason()
        {
            _http.Respond = _ => StubHandler.Json(HttpStatusCode.Unauthorized, """{"responseCode":401,"responseMessage":"Session ended: Replaced"}""", "Replaced");

            var res = await Create().CallingAPI<AssetListRes, object>(AmsApiNames.GET_ASSETS, new { });

            Assert.Equal(ResponseCodes.Unauthorized, res.responseCode);
            _tokens.Received(1).SessionEnded("Replaced");
        }

        [Fact]
        public async Task An_unreachable_API_becomes_an_error_response()
        {
            _http.Respond = _ => throw new HttpRequestException("Connection refused");

            var res = await Create().CallingAPI<AssetListRes, object>(AmsApiNames.GET_ASSETS, new { });

            Assert.Equal(ResponseCodes.ApiError, res.responseCode);
            Assert.Contains("not reachable", res.responseMessage);
        }

        [Fact]
        public async Task Without_a_token_nothing_is_sent()
        {
            var api = Create();
            _tokens.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns((string?)null);

            var res = await api.CallingAPI<AssetListRes, object>(AmsApiNames.GET_ASSETS, new { });

            Assert.Equal(ResponseCodes.Unauthorized, res.responseCode);
            Assert.Empty(_http.Calls);
        }
    }

    // The AMS API's guard forwards each question to UMS with the user's own token and fails closed.
    public class UmsAccessGuardTests
    {
        private readonly StubHandler _http = new();

        private UmsAccessGuard Create() =>
            new(new UmsClient(new HttpClient(_http) { BaseAddress = new Uri("http://ums.test/") }), ActorOf(Rahul), new UmsOptions { Device = "Managed", Channel = "Web" });

        private static readonly AccessCheck Checkout = new("ASSETS", "ASSET", "Checkout", new Dictionary<string, string> { ["location"] = "S045", ["cost"] = "60000" });

        [Fact]
        public async Task Sends_the_users_token_app_record_and_request_context_and_obeys_a_deny()
        {
            _http.Respond = _ => StubHandler.Json(HttpStatusCode.OK, """{"allowed":false,"reason":"Scope: location: role scoped to 'HO-GGN', resource is 'S045'"}""");

            var ex = await Assert.ThrowsAsync<AccessDeniedException>(() => Create().EnsureAsync(Checkout));

            Assert.Contains("role scoped to 'HO-GGN'", ex.Message);
            Assert.Equal("AMS.ASSETS.ASSET.CHECKOUT", ex.Permission);
            var (req, body) = _http.Calls.Single();
            Assert.Equal("http://ums.test/api/v1/me/authorize", req.RequestUri!.ToString());
            Assert.Equal("user-token", req.Headers.Authorization!.Parameter);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            Assert.Equal("AMS", root.GetProperty("app").GetString());
            Assert.Equal("Checkout", root.GetProperty("action").GetString());
            Assert.Equal("S045", root.GetProperty("resourceAttributes").GetProperty("location").GetString());
            Assert.Equal("10.20.30.40", root.GetProperty("env").GetProperty("ip").GetString());
            Assert.Equal("Office LAN", root.GetProperty("env").GetProperty("network").GetString());
            Assert.True(root.GetProperty("log").GetBoolean());
        }

        [Fact]
        public async Task An_unreachable_UMS_never_grants_access()
        {
            _http.Respond = _ => throw new HttpRequestException("Connection refused");

            await Assert.ThrowsAsync<AuthorizationUnavailableException>(() => Create().EnsureAsync(Checkout));
            await Assert.ThrowsAsync<AuthorizationUnavailableException>(() => Create().CanAsync(new[] { Checkout }));
        }

        [Fact]
        public async Task A_session_ended_in_UMS_ends_the_call_with_the_reason()
        {
            _http.Respond = _ => StubHandler.Json(HttpStatusCode.Unauthorized, "{}", "Replaced");

            var ex = await Assert.ThrowsAsync<SessionEndedException>(() => Create().EnsureAsync(Checkout));

            Assert.Equal("Replaced", ex.Reason);
        }

        [Theory]
        [InlineData("10.20.30.40", "Office LAN")]
        [InlineData("192.168.1.9", "Office LAN")]
        [InlineData("::ffff:172.16.0.4", "Office LAN")]
        [InlineData("127.0.0.1", "Office LAN")]
        [InlineData("49.36.10.2", "Internet")]
        [InlineData(null, "Internet")]
        public void Network_is_derived_from_the_client_address(string? ip, string expected) => Assert.Equal(expected, UmsAccessGuard.NetworkOf(ip));
    }

    public class EmailAndCalculationTests
    {
        [Fact]
        public void Recipients_are_cleaned_deduplicated_and_kept_inside_the_allowed_domains()
        {
            var policy = new RecipientPolicy(Options.Create(new NotificationOptions { AllowedDomains = { "vmm.example" } }), NullLogger<RecipientPolicy>.Instance);

            var r = policy.Filter(new EmailRecipients
            {
                To = new[] { "priya.verma@vmm.example; PRIYA.VERMA@vmm.example", "not-an-address" },
                Cc = new[] { "rahul.agarwal@vmm.example", "someone@gmail.com" },
            }, "Test");

            Assert.Equal(new[] { "priya.verma@vmm.example" }, r.To);
            Assert.Equal(new[] { "rahul.agarwal@vmm.example" }, r.Cc);
        }

        [Fact]
        public void Book_value_is_straight_line_over_the_category_life()
        {
            var laptop = Laptop();   // ₹2,10,000 bought 1 Apr 2025, 36-month life
            Assert.Equal(210000m * (1 - 18m / 36), Depreciation.CurrentValue(laptop, new DateTime(2026, 10, 23), out var months));
            Assert.Equal(18, months);
            Assert.Equal(0m, Depreciation.CurrentValue(laptop, new DateTime(2030, 1, 1), out _));
        }

        [Fact]
        public void Csv_cells_are_quoted_and_formula_injection_is_neutralised()
        {
            var csv = Csv.Write(new[] { "Name", "Note" }, new[] { new object?[] { "Dell, Inc", "=HYPERLINK(\"x\")" } });

            Assert.Contains("\"Dell, Inc\"", csv);
            Assert.DoesNotContain(",=HYPERLINK", csv);
        }
    }
}

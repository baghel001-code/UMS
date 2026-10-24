using AMS.Core.Security;
using AMS.Infrastructure.Ums;
using System.Net;
using System.Text;
using System.Text.Json;

namespace AMS.Tests
{
    // The guard is a thin client: it forwards the question to UMS with the user's own token and obeys the answer.
    public class UmsGuardTests
    {
        private sealed class StubHandler : HttpMessageHandler
        {
            public List<(HttpRequestMessage Request, string Body)> Calls { get; } = new();
            public Func<HttpRequestMessage, string, (HttpStatusCode, string)> Respond { get; set; } = (_, _) => (HttpStatusCode.OK, "{}");

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct);
                Calls.Add((request, body));
                var (status, json) = Respond(request, body);
                return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            }
        }

        private static (UmsAccessGuard Guard, StubHandler Http) Create()
        {
            var http = new StubHandler();
            var client = new UmsClient(new HttpClient(http) { BaseAddress = new Uri("http://ums.test/") });
            var session = new UmsSession { ClientIp = "10.12.4.21" };
            session.SignIn("user-token", new Actor(Guid.NewGuid(), Guid.NewGuid(), "VMM10008", "Sneha Iyer"));
            return (new UmsAccessGuard(client, session, new UmsOptions { Device = "Managed", Channel = "Web" }), http);
        }

        [Fact]
        public async Task Ensure_sends_the_users_token_app_and_record_attributes_and_obeys_a_deny()
        {
            var (guard, http) = Create();
            http.Respond = (_, _) => (HttpStatusCode.OK, """{"allowed":false,"reason":"Scope: location: role scoped to 'HO-GGN', resource is 'S045'","steps":[]}""");

            var ex = await Assert.ThrowsAsync<AccessDeniedException>(() => guard.EnsureAsync(
                new AccessCheck("ASSETS", "ASSET", "Checkout", new Dictionary<string, string> { ["location"] = "S045", ["cost"] = "60000" })));

            Assert.Contains("role scoped to 'HO-GGN'", ex.Message);
            Assert.Equal("AMS.ASSETS.ASSET.CHECKOUT", ex.Permission);
            var (req, body) = http.Calls.Single();
            Assert.Equal("http://ums.test/api/v1/me/authorize", req.RequestUri!.ToString());
            Assert.Equal("Bearer", req.Headers.Authorization!.Scheme);
            Assert.Equal("user-token", req.Headers.Authorization.Parameter);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            Assert.Equal("AMS", root.GetProperty("app").GetString());
            Assert.Equal("Checkout", root.GetProperty("action").GetString());
            Assert.Equal("S045", root.GetProperty("resourceAttributes").GetProperty("location").GetString());
            Assert.Equal("Office LAN", root.GetProperty("env").GetProperty("network").GetString());
            Assert.True(root.GetProperty("log").GetBoolean());
        }

        [Fact]
        public async Task Allowed_decisions_pass_and_batches_keep_order()
        {
            var (guard, http) = Create();
            http.Respond = (r, _) => r.RequestUri!.AbsolutePath.EndsWith("/batch")
                ? (HttpStatusCode.OK, """[{"allowed":true,"reason":"ok"},{"allowed":false,"reason":"no"},{"allowed":true,"reason":"ok"}]""")
                : (HttpStatusCode.OK, """{"allowed":true,"reason":"ok"}""");

            await guard.EnsureAsync(new AccessCheck("ASSETS", "ASSET", "View"));
            var result = await guard.CanAsync(new[] { new AccessCheck("A", "B", "View"), new AccessCheck("A", "B", "Edit"), new AccessCheck("A", "B", "Delete") });
            Assert.Equal(new[] { true, false, true }, result);
            Assert.False(JsonDocument.Parse(http.Calls[1].Body).RootElement.GetProperty("log").GetBoolean());
        }

        [Fact]
        public async Task Expired_ums_session_surfaces_as_unauthorized()
        {
            var (guard, http) = Create();
            http.Respond = (_, _) => (HttpStatusCode.Unauthorized, "");
            var ex = await Assert.ThrowsAsync<UmsException>(() => guard.EnsureAsync(new AccessCheck("ASSETS", "ASSET", "View")));
            Assert.True(ex.IsUnauthorized);
        }

        [Theory]
        [InlineData("10.1.2.3", "Office LAN")]
        [InlineData("192.168.0.4", "Office LAN")]
        [InlineData("172.20.1.1", "Office LAN")]
        [InlineData("::1", "Office LAN")]
        [InlineData("::ffff:10.0.0.7", "Office LAN")]
        [InlineData("49.36.12.8", "Internet")]
        [InlineData("", "Internet")]
        public void Network_is_derived_from_the_client_address(string ip, string expected) =>
            Assert.Equal(expected, UmsAccessGuard.NetworkOf(ip));
    }
}

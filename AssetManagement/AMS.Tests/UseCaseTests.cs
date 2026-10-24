using AMS.Application.Interfaces;
using AMS.Application.Notifications;
using AMS.Application.Services;
using AMS.Domain.Base;
using AMS.Domain.Exceptions;
using AMS.Domain.Models;
using AMS.Domain.Requests;
using AMS.Domain.Security;
using static AMS.Tests.TestData;

namespace AMS.Tests
{
    // The use cases describe each action and record to UMS and act only on an Allow; nothing here knows about roles.
    public class UseCaseTests
    {
        private readonly IAccessGuard _guard = Substitute.For<IAccessGuard>();
        private readonly IAssetRepository _assets = Substitute.For<IAssetRepository>();
        private readonly IRequestRepository _requests = Substitute.For<IRequestRepository>();
        private readonly IPeopleDirectory _people = Substitute.For<IPeopleDirectory>();
        private readonly INotificationPublisher _notify = Substitute.For<INotificationPublisher>();
        private readonly IBusinessClock _clock = Clock(new DateTime(2026, 10, 23, 11, 0, 0));

        public UseCaseTests()
        {
            _people.FindAsync("VMM10002", Arg.Any<CancellationToken>()).Returns(Priya);
            _people.FindAsync("VMM10007", Arg.Any<CancellationToken>()).Returns(Imran);
            _assets.GetAsync(Tenant, 7, Arg.Any<CancellationToken>()).Returns(Laptop());
            _assets.CheckoutAsync(default!, default, default!, default, default, default).ReturnsForAnyArgs(DbResult.Success(7));
            _requests.ListAsync(Tenant, null, 3, Arg.Any<CancellationToken>()).Returns(new List<RequestView> { PendingRequest() });
            _requests.DecideAsync(default!, default, default, default, default).ReturnsForAnyArgs(DbResult.Success(3));
        }

        private AssetUseCases Assets() => new(_guard, ActorOf(Rahul), _clock, _assets, Substitute.For<ISetupRepository>(),
            Substitute.For<IMaintenanceRepository>(), Substitute.For<ILicenseRepository>(), Substitute.For<IStockRepository>(),
            Substitute.For<IHistoryRepository>(), _people, _notify);

        private RequestUseCases Requests() => new(_guard, ActorOf(Rahul), _clock, _requests, _assets, _people, _notify);

        private void Deny(string key, string reason) =>
            _guard.EnsureAsync(Arg.Is<AccessCheck>(c => c.Key == key), Arg.Any<CancellationToken>())
                .Returns(Task.FromException(new AccessDeniedException(key, reason)));

        [Fact]
        public async Task Checkout_asks_UMS_with_the_asset_and_the_assignee_then_writes_and_emails()
        {
            var res = await Assets().CheckoutAsync(new AssetCheckoutReq { Id = 7, EmpCode = "VMM10002", Note = "Store audit" });

            Assert.Equal(7, res.recordId);
            await _guard.Received(1).EnsureAsync(Arg.Is<AccessCheck>(c =>
                c.Key == "AMS.ASSETS.ASSET.CHECKOUT" &&
                c.Attributes!["location"] == "HO-GGN" && c.Attributes["cost"] == "210000" &&
                c.Attributes["assigneeEmpCode"] == "VMM10002" && c.Attributes["assigneeEmploymentType"] == "Permanent"), Arg.Any<CancellationToken>());
            await _assets.Received(1).CheckoutAsync(Arg.Is<WriteContext>(w => w.TenantId == Tenant && w.ActorEmpCode == "VMM10010"), 7, Priya, null,
                "Store audit", Arg.Any<CancellationToken>());
            _notify.Received(1).Publish(Arg.Is<AssetCheckedOutModel>(m => m.To == Priya && m.ByName == "Rahul Agarwal"));
        }

        [Fact]
        public async Task A_UMS_deny_stops_the_checkout_before_anything_is_written_or_sent()
        {
            Deny("AMS.ASSETS.ASSET.CHECKOUT", "Policy: high-value assets are not issued to contract staff");

            var ex = await Assert.ThrowsAsync<AccessDeniedException>(() => Assets().CheckoutAsync(new AssetCheckoutReq { Id = 7, EmpCode = "VMM10007" }));

            Assert.Contains("contract staff", ex.Message);
            await _assets.DidNotReceiveWithAnyArgs().CheckoutAsync(default!, default, default!, default, default, default);
            _notify.DidNotReceiveWithAnyArgs().Publish<AssetCheckedOutModel>(default!);
        }

        [Fact]
        public async Task Checkout_rejects_a_return_date_in_the_past()
        {
            var ex = await Assert.ThrowsAsync<AmsException>(() =>
                Assets().CheckoutAsync(new AssetCheckoutReq { Id = 7, EmpCode = "VMM10002", ExpectedCheckin = new DateTime(2026, 10, 1) }));

            Assert.Equal(ResponseCodes.RuleViolation, ex.Code);
            await _assets.DidNotReceiveWithAnyArgs().CheckoutAsync(default!, default, default!, default, default, default);
        }

        [Fact]
        public async Task A_failed_stored_procedure_surfaces_its_code_and_message()
        {
            _assets.CheckoutAsync(default!, default, default!, default, default, default)
                .ReturnsForAnyArgs(new DbResult(ResponseCodes.RuleViolation, "VMM-LT-1007 is already checked out."));

            var ex = await Assert.ThrowsAsync<AmsException>(() => Assets().CheckoutAsync(new AssetCheckoutReq { Id = 7, EmpCode = "VMM10002" }));

            Assert.Equal(ResponseCodes.RuleViolation, ex.Code);
            Assert.Equal("VMM-LT-1007 is already checked out.", ex.Message);
            _notify.DidNotReceiveWithAnyArgs().Publish<AssetCheckedOutModel>(default!);
        }

        [Fact]
        public async Task Each_listed_asset_carries_only_the_actions_UMS_allows_on_it()
        {
            _assets.ListAsync(Tenant, Arg.Any<AssetFilterReq>(), false, Arg.Any<CancellationToken>())
                .Returns(new List<AssetView> { Laptop(1), Laptop(2), Laptop(3) });
            // asset 2 is not visible; on the others only Edit (asset 1) and Checkout (asset 3) are allowed
            _guard.CanAsync(Arg.Any<IReadOnlyList<AccessCheck>>(), Arg.Any<CancellationToken>()).Returns(
                new[] { true, false, true },
                new[] { true, false, false, false, false, /* asset 3: */ false, false, true, false, false });

            var rows = (await Assets().ListAsync(new AssetFilterReq())).Rows;

            Assert.Equal(new[] { 1, 3 }, rows.Select(r => r.Asset.Id));
            Assert.True(rows[0].CanEdit);
            Assert.False(rows[0].CanCheckout);
            Assert.False(rows[1].CanEdit);
            Assert.True(rows[1].CanCheckout);
        }

        [Fact]
        public async Task Approving_a_request_also_needs_checkout_permission_for_the_requester()
        {
            Deny("AMS.ASSETS.ASSET.CHECKOUT", "Scope: location: role scoped to 'S045', resource is 'HO-GGN'");

            await Assert.ThrowsAsync<AccessDeniedException>(() => Requests().DecideAsync(new RequestDecideReq { Id = 3, Approve = true }));

            await _guard.Received(1).EnsureAsync(Arg.Is<AccessCheck>(c => c.Key == "AMS.ASSETS.REQUEST.APPROVE" && c.Attributes!["requestedByEmpCode"] == "VMM10002"),
                Arg.Any<CancellationToken>());
            await _requests.DidNotReceiveWithAnyArgs().DecideAsync(default!, default, default, default, default);
        }

        [Fact]
        public async Task Approval_issues_the_asset_and_tells_the_requester()
        {
            var res = await Requests().DecideAsync(new RequestDecideReq { Id = 3, Approve = true, Note = "Enjoy" });

            Assert.Equal("Approved — asset issued", res.responseMessage);
            await _requests.Received(1).DecideAsync(Arg.Any<WriteContext>(), 3, true, "Enjoy", Arg.Any<CancellationToken>());
            _notify.Received(1).Publish(Arg.Is<RequestDecidedModel>(m => m.Approved && m.Request.Id == 3));
        }

        [Fact]
        public async Task A_rejection_needs_a_reason()
        {
            var ex = await Assert.ThrowsAsync<AmsException>(() => Requests().DecideAsync(new RequestDecideReq { Id = 3, Approve = false }));

            Assert.Equal(ResponseCodes.RuleViolation, ex.Code);
            await _requests.DidNotReceiveWithAnyArgs().DecideAsync(default!, default, default, default, default);
        }
    }
}

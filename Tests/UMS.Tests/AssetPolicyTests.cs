using DomainLayer.Authorization;
using DomainLayer.Enumerations;
using DomainLayer.Services;
using Infrastructure.Persistence;

namespace UMS.Tests
{
    // The Asset Management System asks UMS for every action; these are the decisions it gets for the seeded AMS roles and policies.
    public class AssetPolicyTests
    {
        private static readonly DemoSeed Seed = DemoSeed.Create(new DateTime(2026, 10, 24, 11, 0, 0));
        private static readonly AccessEnvironment Friday11 = new(new DateTime(2026, 10, 23, 11, 0, 0), "10.12.4.21", "Office LAN", "Managed", "Web");

        private const string Priya = "VMM10002";  // cashier (EMPLOYEE only for AMS)
        private const string Vikram = "VMM10005"; // financial controller, no AMS role
        private const string Sneha = "VMM10008";  // ASSET_MANAGER scoped to location HO-GGN
        private const string Rahul = "VMM10010";  // ASSET_ADMIN

        private static AccessDecision Decide(string empCode, string resource, AccessAction action, Dictionary<string, string>? attrs = null,
            AccessEnvironment? env = null, string module = "ASSETS") =>
            AccessDecisionService.Decide(new AccessRequest(Seed.UserByEmpCode(empCode).Id, "AMS", module, resource, action,
                attrs ?? new Dictionary<string, string>(), env ?? Friday11),
                new AccessContext(Seed.Tenants, Seed.Applications, Seed.Roles, Seed.Users, Seed.Policies, Seed.Schedules, Seed.Holidays));

        private static Dictionary<string, string> Asset(string location, decimal cost, string? assignedTo = null, string? assigneeType = null)
        {
            var a = new Dictionary<string, string> { ["location"] = location, ["cost"] = cost.ToString(System.Globalization.CultureInfo.InvariantCulture) };
            if (assignedTo != null) a["assignedToEmpCode"] = assignedTo;
            if (assigneeType != null) a["assigneeEmploymentType"] = assigneeType;
            return a;
        }

        [Fact]
        public void Manager_works_only_on_assets_of_their_location()
        {
            Assert.True(Decide(Sneha, "ASSET", AccessAction.Checkout, Asset("HO-GGN", 60000, assigneeType: "Permanent")).Allowed);
            var other = Decide(Sneha, "ASSET", AccessAction.Checkout, Asset("S045", 60000, assigneeType: "Permanent"));
            Assert.False(other.Allowed);
            Assert.Equal("Scope", other.Steps[^1].Layer);
        }

        [Fact]
        public void Type_level_checks_without_attributes_pass_for_scoped_manager() =>
            Assert.True(Decide(Sneha, "ASSET", AccessAction.Create).Allowed);

        [Fact]
        public void Employee_sees_only_assets_issued_to_them()
        {
            Assert.True(Decide(Priya, "ASSET", AccessAction.View, Asset("S045", 45000, assignedTo: "VMM10002")).Allowed);
            Assert.False(Decide(Priya, "ASSET", AccessAction.View, Asset("S045", 45000, assignedTo: "VMM10003")).Allowed);
            Assert.False(Decide(Priya, "ASSET", AccessAction.Edit, Asset("S045", 45000, assignedTo: "VMM10002")).Allowed);
            Assert.True(Decide(Priya, "MY_ASSETS", AccessAction.View, module: "SELF").Allowed);
        }

        [Fact]
        public void No_self_approval_of_requests()
        {
            var own = new Dictionary<string, string> { ["requestedByEmpCode"] = Rahul, ["location"] = "HO-GGN" };
            var someoneElse = new Dictionary<string, string> { ["requestedByEmpCode"] = Priya, ["location"] = "HO-GGN" };
            Assert.False(Decide(Rahul, "REQUEST", AccessAction.Approve, own).Allowed);
            Assert.True(Decide(Rahul, "REQUEST", AccessAction.Approve, someoneElse).Allowed);
        }

        [Fact]
        public void High_value_assets_are_not_issued_to_contract_staff()
        {
            Assert.False(Decide(Rahul, "ASSET", AccessAction.Checkout, Asset("HO-GGN", 180000, assigneeType: "Contract")).Allowed);
            Assert.True(Decide(Rahul, "ASSET", AccessAction.Checkout, Asset("HO-GGN", 180000, assigneeType: "Permanent")).Allowed);
        }

        [Fact]
        public void Check_out_only_in_operating_hours() =>
            Assert.False(Decide(Rahul, "ASSET", AccessAction.Checkout, Asset("HO-GGN", 50000, assigneeType: "Permanent"),
                Friday11 with { DateTime = new DateTime(2026, 10, 23, 23, 30, 0) }).Allowed);

        [Fact]
        public void Managers_cannot_dispose_high_value_assets_admins_can()
        {
            Assert.False(Decide(Sneha, "ASSET", AccessAction.Delete, Asset("HO-GGN", 250000)).Allowed);
            Assert.True(Decide(Sneha, "ASSET", AccessAction.Delete, Asset("HO-GGN", 40000)).Allowed);
            Assert.True(Decide(Rahul, "ASSET", AccessAction.Delete, Asset("HO-GGN", 250000)).Allowed);
        }

        [Fact]
        public void Users_without_ams_roles_get_nothing() =>
            Assert.False(Decide(Vikram, "OVERVIEW", AccessAction.View, module: "DASHBOARD").Allowed);
    }
}

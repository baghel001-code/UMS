using DomainLayer.Authorization;
using DomainLayer.Entities;
using DomainLayer.Enumerations;
using DomainLayer.Services;
using DomainLayer.ValueObjects;
using Infrastructure.Persistence;

namespace UMS.Tests
{
    // Same scenarios as admin-panel/src/engine/pdp.test.ts so the API and the simulator stay in step.
    public class AccessDecisionServiceTests
    {
        private static readonly DemoSeed Seed = DemoSeed.Create(new DateTime(2026, 10, 24, 11, 0, 0));
        private static readonly AccessEnvironment Office = new(new DateTime(2026, 10, 23, 11, 0, 0), "10.12.4.21", "Office LAN", "Managed", "Web");
        private static readonly AccessEnvironment Pos = Office with { Channel = "POS Terminal" };

        private static AccessContext Data(IEnumerable<User>? users = null) =>
            new(Seed.Tenants, Seed.Applications, Seed.Roles, (users ?? Seed.Users).ToList(), Seed.Policies, Seed.Schedules, Seed.Holidays);

        private static AccessDecision Decide(string empCode, string app, string module, string resource, AccessAction action,
            AccessEnvironment? env = null, Dictionary<string, string>? attrs = null, AccessContext? data = null) =>
            AccessDecisionService.Decide(
                new AccessRequest(Seed.UserByEmpCode(empCode).Id, app, module, resource, action, attrs ?? new Dictionary<string, string>(), env ?? Office),
                data ?? Data());

        private static Dictionary<string, string> Attrs(params (string Key, string Value)[] kv) => kv.ToDictionary(x => x.Key, x => x.Value);

        private const string Priya = "VMM10002";   // cashier, store S045
        private const string Rohit = "VMM10003";   // shift supervisor, S045
        private const string Neha = "VMM10004";    // finance executive, no MFA
        private const string Vikram = "VMM10005";  // financial controller
        private const string Ankit = "VMM10009";   // super admin
        private const string Rahul = "VMM10010";   // buyer, expired temporary PROC_HEAD

        [Theory]
        [InlineData("POS.*.*.*", "POS.BILLING.INVOICE.REFUND", true)]
        [InlineData("*.*.*.VIEW", "HRMS.CORE.EMPLOYEE.VIEW", true)]
        [InlineData("*.*.*.VIEW", "HRMS.CORE.EMPLOYEE.EDIT", false)]
        [InlineData("POS.BILLING.INVOICE.VIEW", "POS.*.*.*", true)]
        [InlineData("WMS.*.*.*", "POS.*.*.*", false)]
        public void PermissionKey_supports_wildcards(string granted, string requested, bool expected) =>
            Assert.Equal(expected, PermissionKey.Matches(granted, requested));

        [Fact]
        public void PermissionKey_covers_is_strict_about_requested_wildcards()
        {
            Assert.False(PermissionKey.Covers("POS.BILLING.INVOICE.VIEW", "POS.BILLING.*.*"));
            Assert.True(PermissionKey.Covers("POS.BILLING.*.*", "POS.BILLING.*.*"));
        }

        [Fact]
        public void Schedules_office_hours_holidays_regions_and_overnight()
        {
            var general = Seed.Schedules.First(s => s.Code == "GENERAL");
            var night = Seed.Schedules.First(s => s.Code == "WH_NIGHT");
            var h = Seed.Holidays;
            Assert.True(ScheduleEvaluator.IsInSchedule(general, new DateTime(2026, 10, 23, 10, 0, 0), h, null));
            Assert.False(ScheduleEvaluator.IsInSchedule(general, new DateTime(2026, 10, 23, 19, 0, 0), h, null));
            Assert.False(ScheduleEvaluator.IsInSchedule(general, new DateTime(2026, 10, 25, 10, 0, 0), h, null)); // Sunday
            Assert.False(ScheduleEvaluator.IsInSchedule(general, new DateTime(2026, 10, 2, 10, 0, 0), h, null));  // Gandhi Jayanti
            Assert.False(ScheduleEvaluator.IsInSchedule(general, new DateTime(2026, 1, 14, 10, 0, 0), h, "South"));
            Assert.True(ScheduleEvaluator.IsInSchedule(general, new DateTime(2026, 1, 14, 10, 0, 0), h, "North"));
            Assert.True(ScheduleEvaluator.IsInSchedule(night, new DateTime(2026, 10, 23, 23, 30, 0), h, null));
            Assert.True(ScheduleEvaluator.IsInSchedule(night, new DateTime(2026, 10, 24, 5, 59, 0), h, null));
            Assert.False(ScheduleEvaluator.IsInSchedule(night, new DateTime(2026, 10, 24, 6, 0, 0), h, null));
        }

        [Fact]
        public void Cashier_creates_invoice_on_pos_terminal_in_store_hours() =>
            Assert.True(Decide(Priya, "POS", "BILLING", "INVOICE", AccessAction.Create, Pos, Attrs(("store", "S045"))).Allowed);

        [Fact]
        public void Cashier_blocked_outside_store_hours()
        {
            var d = Decide(Priya, "POS", "BILLING", "INVOICE", AccessAction.Create, Pos with { DateTime = new DateTime(2026, 10, 23, 23, 30, 0) });
            Assert.False(d.Allowed);
            Assert.Contains("store hours", d.Reason);
        }

        [Fact]
        public void Cashier_blocked_from_web_browser() =>
            Assert.False(Decide(Priya, "POS", "BILLING", "INVOICE", AccessAction.Create).Allowed);

        [Theory]
        [InlineData("POS", "BILLING", "INVOICE", AccessAction.Refund, "Permission")]
        [InlineData("FIN", "GL", "JOURNAL", AccessAction.View, "Application")]
        [InlineData("POS", "CONFIG", "TERMINAL", AccessAction.View, "Module")]
        public void Cashier_is_stopped_at_the_right_layer(string app, string module, string resource, AccessAction action, string layer)
        {
            var d = Decide(Priya, app, module, resource, action, Pos);
            Assert.False(d.Allowed);
            Assert.Equal(layer, d.Steps[^1].Layer);
        }

        [Fact]
        public void Role_scope_blocks_billing_for_another_store()
        {
            var d = Decide(Priya, "POS", "BILLING", "INVOICE", AccessAction.Create, Pos, Attrs(("store", "S101")));
            Assert.False(d.Allowed);
            Assert.Equal("Scope", d.Steps[^1].Layer);
        }

        [Fact]
        public void Supervisor_refund_limit()
        {
            Assert.False(Decide(Rohit, "POS", "BILLING", "INVOICE", AccessAction.Refund, Pos, Attrs(("store", "S045"), ("amount", "15000"))).Allowed);
            Assert.True(Decide(Rohit, "POS", "BILLING", "INVOICE", AccessAction.Refund, Pos, Attrs(("store", "S045"), ("amount", "2500"))).Allowed);
        }

        [Fact]
        public void Finance_approval_needs_office_network()
        {
            Assert.False(Decide(Neha, "FIN", "AP", "VENDOR_INVOICE", AccessAction.Approve).Allowed);
            Assert.True(Decide(Vikram, "FIN", "AP", "VENDOR_INVOICE", AccessAction.Approve).Allowed);
            Assert.False(Decide(Vikram, "FIN", "AP", "VENDOR_INVOICE", AccessAction.Approve, Office with { Network = "Internet" }).Allowed);
        }

        [Fact]
        public void Finance_executive_blocked_after_office_hours()
        {
            Assert.True(Decide(Neha, "FIN", "GL", "JOURNAL", AccessAction.View).Allowed);
            Assert.False(Decide(Neha, "FIN", "GL", "JOURNAL", AccessAction.View, Office with { DateTime = new DateTime(2026, 10, 23, 20, 15, 0) }).Allowed);
        }

        [Fact]
        public void Payslip_own_record_only()
        {
            Assert.True(Decide(Priya, "HRMS", "ESS", "PAYSLIP", AccessAction.View, null, Attrs(("ownerEmpCode", "VMM10002"))).Allowed);
            Assert.False(Decide(Priya, "HRMS", "ESS", "PAYSLIP", AccessAction.View, null, Attrs(("ownerEmpCode", "VMM10003"))).Allowed);
        }

        [Fact]
        public void Grant_policy_lets_named_approver_approve_po()
        {
            Assert.True(Decide(Vikram, "PROC", "PO", "PURCHASE_ORDER", AccessAction.Approve, null, Attrs(("approverEmpCode", "VMM10005"))).Allowed);
            Assert.False(Decide(Vikram, "PROC", "PO", "PURCHASE_ORDER", AccessAction.Approve, null, Attrs(("approverEmpCode", "VMM10099"))).Allowed);
        }

        [Fact]
        public void Expired_temporary_role_is_ignored() =>
            Assert.False(Decide(Rahul, "PROC", "PO", "PURCHASE_ORDER", AccessAction.Approve).Allowed);

        [Fact]
        public void Contract_staff_cannot_delete_even_with_permission()
        {
            Assert.True(Decide(Ankit, "UMS", "USERS", "USER", AccessAction.Delete).Allowed);

            var seed = DemoSeed.Create(new DateTime(2026, 10, 24, 11, 0, 0));
            var ankit = seed.UserByEmpCode(Ankit);
            ankit.UpdateProfile(ankit.EmpCode, ankit.Name, ankit.Email, ankit.Phone, ankit.Department, ankit.Designation, ankit.Location, ankit.Region,
                EmploymentType.Contract, ankit.Status, ankit.MfaEnabled, null);
            Assert.False(Decide(Ankit, "UMS", "USERS", "USER", AccessAction.Delete, data: Data(seed.Users)).Allowed);
        }

        [Fact]
        public void Locked_user_denied_at_identity_layer()
        {
            var seed = DemoSeed.Create(new DateTime(2026, 10, 24, 11, 0, 0));
            seed.UserByEmpCode(Priya).SetStatus(UserStatus.Locked);
            var d = Decide(Priya, "POS", "BILLING", "INVOICE", AccessAction.View, Pos, data: Data(seed.Users));
            Assert.False(d.Allowed);
            Assert.Equal("Identity", d.Steps[^1].Layer);
        }

        [Fact]
        public void Policy_expression_is_readable()
        {
            var p = Seed.Policies.First(x => x.Name.StartsWith("POS staff"));
            Assert.Equal("InSchedule(env.now, 'STORE') && env.channel == 'POS Terminal'", ConditionEvaluator.Expression(p, Seed.Schedules));
        }

        [Fact]
        public void Effective_permissions_list_what_roles_grant()
        {
            var priya = Seed.UserByEmpCode(Priya);
            var perms = AccessDecisionService.EffectivePermissions(priya, Seed.Roles, Seed.Applications, new DateOnly(2026, 10, 23));
            Assert.Contains(perms, p => p.Key == "POS.BILLING.INVOICE.CREATE" && p.ViaRoles.Contains("CASHIER"));
            Assert.DoesNotContain(perms, p => p.Key == "POS.BILLING.INVOICE.REFUND");
        }
    }
}

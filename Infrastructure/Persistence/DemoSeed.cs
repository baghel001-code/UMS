using DomainLayer.Entities;
using DomainLayer.Enumerations;
using DomainLayer.ValueObjects;
using System.Security.Cryptography;
using System.Text;
using A = DomainLayer.Enumerations.AccessAction;

namespace Infrastructure.Persistence
{
    /// <summary>
    /// Demo data for Vishal Mega Mart: tenants, 13 applications, roles, office-hour schedules, policies, ~300 users and logs.
    /// Used by the in-memory store (Persistence:Provider = InMemory) and by the tests. Ids are deterministic.
    /// </summary>
    public sealed class DemoSeed
    {
        public static Guid IdFor(string key) => new(MD5.HashData(Encoding.UTF8.GetBytes(key)));

        public static readonly Guid VmmTenantId = IdFor("tenant:VMM");
        public static readonly Guid VendorTenantId = IdFor("tenant:VMM-PARTNERS");
        public static readonly Guid UatTenantId = IdFor("tenant:VMM-UAT");

        public static readonly string[] Regions = { "North", "East", "West", "South", "Central" };

        public List<Tenant> Tenants { get; } = new();
        public List<Application> Applications { get; } = new();
        public List<Role> Roles { get; } = new();
        public List<User> Users { get; } = new();
        public List<Policy> Policies { get; } = new();
        public List<Schedule> Schedules { get; } = new();
        public List<Holiday> Holidays { get; } = new();
        public List<AuditLog> AuditLogs { get; } = new();
        public List<UserActionLog> ActionLogs { get; } = new();
        public List<ExceptionLog> ExceptionLogs { get; } = new();
        public Dictionary<Guid, SecuritySettings> Settings { get; } = new();

        private readonly Random _rnd = new(20261024);
        private readonly DateTime _now;
        private T Pick<T>(IReadOnlyList<T> list) => list[_rnd.Next(list.Count)];

        public static DemoSeed Create(DateTime? now = null) => new(now ?? DateTime.Now);

        private DemoSeed(DateTime now)
        {
            _now = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0);
            SeedTenants();
            SeedApplications();
            SeedRoles();
            SeedSchedules();
            SeedPolicies();
            SeedUsers();
            SeedLogs();
        }

        public User UserByEmpCode(string empCode) => Users.First(u => u.EmpCode == empCode);
        public Role RoleByCode(Guid tenantId, string code) => Roles.First(r => r.TenantId == tenantId && r.Code == code);

        // ------------------------------------------------------------ tenants

        private void SeedTenants()
        {
            Tenants.Add(new Tenant(VmmTenantId, "VMM", "Vishal Mega Mart Ltd", true, new DateOnly(2025, 4, 1)));
            Tenants.Add(new Tenant(VendorTenantId, "VMM-PARTNERS", "Vendor & Partner Network", true, new DateOnly(2025, 6, 15)));
            Tenants.Add(new Tenant(UatTenantId, "VMM-UAT", "UAT / Sandbox", true, new DateOnly(2025, 5, 10)));
            Settings[VmmTenantId] = SecuritySettings.Default;
            Settings[VendorTenantId] = SecuritySettings.Default with { SessionTimeoutMinutes = 15 };
            Settings[UatTenantId] = SecuritySettings.Default with { MfaRequiredForAdmins = false };
        }

        // ------------------------------------------------------------ applications

        private static (string Code, string Name, A[] Actions) R(string code, string name, params A[] actions) => (code, name, actions);
        private static (string Code, string Name, (string, string, A[])[] Resources) M(string code, string name, params (string, string, A[])[] resources) => (code, name, resources);

        private void App(string code, string name, AppType type, string vendor, string ownerDept, string description, Guid[] tenants,
            params (string Code, string Name, (string Code, string Name, A[] Actions)[] Resources)[] modules)
        {
            var mods = modules.Select(m => new AppModule(IdFor($"mod:{code}.{m.Code}"), m.Code, m.Name,
                m.Resources.Select(r => new AppResource(IdFor($"res:{code}.{m.Code}.{r.Code}"), r.Code, r.Name, r.Actions)).ToList())).ToList();
            Applications.Add(new Application(IdFor($"app:{code}"), code, name, type, vendor, ownerDept, description, true, tenants, mods));
        }

        private void SeedApplications()
        {
            var all = new[] { VmmTenantId, UatTenantId };
            var ih = AppType.InHouse;
            var tp = AppType.ThirdParty;

            App("UMS", "User Management System", ih, "VMM IT", "IT", "Central identity, roles and access policies for all applications.", all,
                M("USERS", "Users", R("USER", "User", A.View, A.Create, A.Edit, A.Delete, A.Export)),
                M("ROLES", "Roles & Permissions", R("ROLE", "Role", A.View, A.Create, A.Edit, A.Delete, A.Approve)),
                M("POLICIES", "ABAC Policies",
                    R("POLICY", "Policy", A.View, A.Create, A.Edit, A.Delete, A.Approve),
                    R("SCHEDULE", "Office hours & holidays", A.View, A.Create, A.Edit, A.Delete),
                    R("DECISION", "Access check", A.View)),
                M("SETUP", "Setup",
                    R("TENANT", "Tenant", A.View, A.Create, A.Edit),
                    R("APPLICATION", "Application", A.View, A.Create, A.Edit, A.Delete),
                    R("SETTINGS", "Security settings", A.View, A.Edit)),
                M("LOGS", "Audit & Logs",
                    R("AUDIT_LOG", "Audit log", A.View, A.Export),
                    R("ACTION_LOG", "User action trail", A.View, A.Export),
                    R("EXCEPTION_LOG", "Exception log", A.View, A.Edit)));
            App("POS", "Point of Sale", ih, "VMM IT", "Retail Operations", "Store billing, returns and shift management on POS terminals.", all,
                M("BILLING", "Billing",
                    R("INVOICE", "Sales invoice", A.View, A.Create, A.Void, A.Refund, A.Print),
                    R("DISCOUNT", "Manual discount", A.View, A.Create, A.Approve),
                    R("PAYMENT", "Payment", A.View, A.Create, A.Refund)),
                M("RETURNS", "Returns & Exchange", R("RETURN", "Return", A.View, A.Create, A.Approve)),
                M("SHIFT", "Shift & Cash",
                    R("CASH_DRAWER", "Cash drawer", A.View, A.Create, A.Edit, A.Approve),
                    R("SHIFT_REPORT", "Shift report", A.View, A.Print, A.Export)),
                M("CONFIG", "Terminal Config", R("TERMINAL", "POS terminal", A.View, A.Create, A.Edit, A.Delete)));
            App("WMS", "Warehouse & Inventory", ih, "VMM IT", "Supply Chain", "Stock, GRN, stock transfers and dispatch across DCs and stores.", all,
                M("INVENTORY", "Inventory", R("STOCK", "Stock", A.View, A.Edit, A.Export), R("STOCK_ADJ", "Stock adjustment", A.View, A.Create, A.Approve)),
                M("INBOUND", "Inbound", R("GRN", "Goods receipt (GRN)", A.View, A.Create, A.Edit, A.Approve)),
                M("OUTBOUND", "Outbound", R("STO", "Stock transfer order", A.View, A.Create, A.Approve), R("DISPATCH", "Dispatch", A.View, A.Create, A.Print)));
            App("MERCH", "Merchandising & Pricing", ih, "VMM IT", "Merchandising", "Article master, pricing, promotions and OTB planning.", all,
                M("CATALOG", "Catalog", R("ARTICLE", "Article", A.View, A.Create, A.Edit, A.Delete, A.Export)),
                M("PRICING", "Pricing", R("PRICE", "Price", A.View, A.Edit, A.Approve), R("PROMO", "Promotion", A.View, A.Create, A.Edit, A.Approve, A.Delete)),
                M("PLANNING", "Planning", R("OTB", "Open-to-buy", A.View, A.Edit, A.Approve)));
            App("FIN", "Finance & Accounts (ERP)", tp, "ERP vendor", "Finance", "Accounts payable, general ledger and financial reporting.", all,
                M("AP", "Accounts Payable", R("VENDOR_INVOICE", "Vendor invoice", A.View, A.Create, A.Edit, A.Approve, A.Export), R("PAYMENT_RUN", "Payment run", A.View, A.Create, A.Approve)),
                M("GL", "General Ledger", R("JOURNAL", "Journal entry", A.View, A.Create, A.Approve, A.Export)),
                M("REPORTS", "Financial Reports", R("PNL", "Profit & loss", A.View, A.Export, A.Print)));
            App("HRMS", "Human Resources (HRMS)", tp, "HRMS vendor", "Human Resources", "Employee master, attendance, leave and employee self-service.", all,
                M("CORE", "Employee Master", R("EMPLOYEE", "Employee record", A.View, A.Create, A.Edit, A.Delete, A.Export)),
                M("ATTENDANCE", "Attendance", R("ATTENDANCE", "Attendance", A.View, A.Edit, A.Approve)),
                M("LEAVE", "Leave", R("LEAVE_REQ", "Leave request", A.View, A.Create, A.Approve)),
                M("ESS", "Employee Self Service", R("PAYSLIP", "Payslip", A.View, A.Print), R("PROFILE", "My profile", A.View, A.Edit)));
            App("PAYROLL", "Payroll", tp, "Payroll vendor", "Human Resources", "Monthly payroll processing and statutory compliance.", all,
                M("PROCESS", "Payroll Processing", R("PAYROLL_RUN", "Payroll run", A.View, A.Create, A.Approve), R("SALARY", "Salary structure", A.View, A.Edit, A.Export)),
                M("COMPLIANCE", "Statutory", R("PF_ESI", "PF / ESI returns", A.View, A.Export)));
            App("PROC", "Procurement & Vendor Portal", ih, "VMM IT", "Procurement", "Purchase orders, RFQs and vendor onboarding.", new[] { VmmTenantId, VendorTenantId, UatTenantId },
                M("PO", "Purchase Orders", R("PURCHASE_ORDER", "Purchase order", A.View, A.Create, A.Edit, A.Approve, A.Delete, A.Print)),
                M("VENDOR", "Vendor Master", R("VENDOR", "Vendor", A.View, A.Create, A.Edit, A.Approve)),
                M("RFQ", "RFQ", R("RFQ", "Request for quotation", A.View, A.Create, A.Approve)),
                M("INVOICE", "Vendor Invoicing", R("VENDOR_BILL", "Vendor bill", A.View, A.Create, A.Edit)));
            App("CRM", "Loyalty & CRM", tp, "CRM vendor", "Marketing", "Customer profiles, loyalty points and campaigns.", all,
                M("CUSTOMER", "Customers", R("CUSTOMER", "Customer", A.View, A.Edit, A.Export, A.Delete)),
                M("LOYALTY", "Loyalty", R("POINTS", "Loyalty points", A.View, A.Edit, A.Approve)),
                M("CAMPAIGN", "Campaigns", R("CAMPAIGN", "Campaign", A.View, A.Create, A.Approve)));
            App("ECOM", "Online Orders", ih, "VMM IT", "E-commerce", "Online orders and store fulfilment.", all,
                M("ORDERS", "Orders", R("ORDER", "Customer order", A.View, A.Edit, A.Void, A.Refund)),
                M("FULFILMENT", "Fulfilment", R("PICKLIST", "Pick list", A.View, A.Create, A.Print)));
            App("BI", "BI & Reporting", tp, "BI vendor", "Strategy", "Dashboards and analytical reports.", all,
                M("SALES", "Sales Analytics", R("SALES_DASH", "Sales dashboard", A.View, A.Export), R("STORE_PL", "Store P&L", A.View, A.Export)),
                M("HR", "HR Analytics", R("HR_DASH", "HR dashboard", A.View, A.Export)));
            App("STOREOPS", "Store Operations", ih, "VMM IT", "Retail Operations", "Store tasks, audits and asset register.", all,
                M("TASKS", "Tasks", R("TASK", "Task", A.View, A.Create, A.Edit, A.Approve)),
                M("AUDIT", "Store Audit", R("STORE_AUDIT", "Store audit", A.View, A.Create, A.Approve)),
                M("ASSETS", "Assets", R("ASSET", "Asset", A.View, A.Create, A.Edit)));
            App("ITSM", "IT Helpdesk", tp, "ITSM vendor", "IT", "Tickets and access requests.", all,
                M("TICKETS", "Tickets", R("TICKET", "Ticket", A.View, A.Create, A.Edit, A.Approve)),
                M("ACCESS", "Access Requests", R("ACCESS_REQ", "Access request", A.View, A.Create, A.Approve)));
            // Asset Management System: every AMS action is authorized against this catalogue (AssetManagement/ in this repo)
            App("AMS", "Asset Management System", ih, "VMM IT", "IT", "Hardware, licenses, accessories, consumables and components across stores, DCs and offices.", all,
                M("DASHBOARD", "Dashboard", R("OVERVIEW", "Asset overview", A.View)),
                M("ASSETS", "Assets",
                    R("ASSET", "Asset", A.View, A.Create, A.Edit, A.Delete, A.Checkout, A.Checkin, A.Audit, A.Export, A.Print),
                    R("MAINTENANCE", "Maintenance", A.View, A.Create, A.Edit, A.Delete),
                    R("REQUEST", "Asset request", A.View, A.Request, A.Approve, A.Void)),
                M("LICENSES", "Licenses", R("LICENSE", "Software license", A.View, A.Create, A.Edit, A.Delete, A.Checkout, A.Checkin, A.Export)),
                M("ACCESSORIES", "Accessories", R("ACCESSORY", "Accessory", A.View, A.Create, A.Edit, A.Delete, A.Checkout, A.Checkin)),
                M("CONSUMABLES", "Consumables", R("CONSUMABLE", "Consumable", A.View, A.Create, A.Edit, A.Delete, A.Checkout)),
                M("COMPONENTS", "Components", R("COMPONENT", "Component", A.View, A.Create, A.Edit, A.Delete, A.Checkout, A.Checkin)),
                M("REPORTS", "Reports", R("ACTIVITY", "Activity log", A.View, A.Export), R("REPORT", "Reports", A.View, A.Export)),
                M("SETUP", "Setup",
                    R("CATEGORY", "Category", A.View, A.Create, A.Edit, A.Delete),
                    R("MANUFACTURER", "Manufacturer", A.View, A.Create, A.Edit, A.Delete),
                    R("MODEL", "Asset model", A.View, A.Create, A.Edit, A.Delete),
                    R("SUPPLIER", "Supplier", A.View, A.Create, A.Edit, A.Delete),
                    R("LOCATION", "Location", A.View, A.Create, A.Edit, A.Delete),
                    R("DEPARTMENT", "Department", A.View, A.Create, A.Edit, A.Delete),
                    R("STATUS", "Status label", A.View, A.Create, A.Edit, A.Delete)),
                M("SELF", "Self service", R("MY_ASSETS", "My assets", A.View)));
        }

        // ------------------------------------------------------------ roles

        private static IEnumerable<string> Each(string prefix, params A[] actions) => actions.Select(a => $"{prefix}.{a.ToString().ToUpperInvariant()}");

        private void Role(Guid tenantId, string code, string name, string? appCode, string description, IEnumerable<string> permissions, bool isSystem = false) =>
            Roles.Add(new Role(IdFor($"role:{tenantId}:{code}"), tenantId, code, name, description, appCode == null ? null : IdFor($"app:{appCode}"),
                permissions, isSystem, true));

        private void SeedRoles()
        {
            var t = VmmTenantId;
            Role(t, "SUPER_ADMIN", "Super Administrator", null, "Unrestricted access to every application. Break-glass only.", new[] { "*.*.*.*" }, true);
            Role(t, "UMS_ADMIN", "UMS Administrator", "UMS", "Manages users, roles and policies.", new[] { "UMS.*.*.*" }, true);
            Role(t, "AUDITOR", "Internal Auditor", null, "Read-only access across applications.", new[] { "*.*.*.VIEW", "*.*.*.EXPORT" }, true);
            Role(t, "EMPLOYEE", "Employee (Self Service)", null, "Default role for every employee.",
                new[] { "HRMS.ESS.*.VIEW", "HRMS.ESS.PAYSLIP.PRINT", "HRMS.ESS.PROFILE.EDIT" }
                    .Concat(Each("HRMS.LEAVE.LEAVE_REQ", A.View, A.Create)).Concat(Each("ITSM.TICKETS.TICKET", A.View, A.Create))
                    .Concat(Each("ITSM.ACCESS.ACCESS_REQ", A.View, A.Create))
                    .Concat(new[] { "AMS.SELF.MY_ASSETS.VIEW" }).Concat(Each("AMS.ASSETS.REQUEST", A.View, A.Request, A.Void)), true);
            Role(t, "CASHIER", "Cashier", "POS", "Billing on POS terminals.",
                Each("POS.BILLING.INVOICE", A.View, A.Create, A.Print).Concat(Each("POS.BILLING.PAYMENT", A.View, A.Create))
                    .Concat(Each("POS.RETURNS.RETURN", A.View, A.Create)).Concat(Each("POS.SHIFT.CASH_DRAWER", A.View, A.Create)));
            Role(t, "SHIFT_SUPERVISOR", "Shift Supervisor", "POS", "Approves voids, refunds and discounts during a shift.", new[] { "POS.BILLING.*.*", "POS.RETURNS.*.*", "POS.SHIFT.*.*" });
            Role(t, "STORE_MANAGER", "Store Manager", null, "Runs a store: POS, store ops, store stock and team attendance.",
                new[] { "POS.*.*.*", "STOREOPS.*.*.*", "WMS.INVENTORY.*.VIEW", "WMS.INBOUND.GRN.*", "BI.SALES.STORE_PL.VIEW", "HRMS.ATTENDANCE.*.*", "HRMS.LEAVE.LEAVE_REQ.APPROVE", "ECOM.FULFILMENT.*.*" });
            Role(t, "WH_EXEC", "Warehouse Executive", "WMS", "Receives and dispatches goods.",
                new[] { "WMS.INVENTORY.STOCK.VIEW" }.Concat(Each("WMS.INBOUND.GRN", A.View, A.Create, A.Edit)).Concat(new[] { "WMS.OUTBOUND.DISPATCH.*", "WMS.OUTBOUND.STO.VIEW" }));
            Role(t, "WH_MANAGER", "Warehouse Manager", "WMS", "Full warehouse control incl. stock adjustments.", new[] { "WMS.*.*.*" });
            Role(t, "MERCHANDISER", "Merchandiser", "MERCH", "Maintains articles and proposes prices.",
                new[] { "MERCH.CATALOG.*.*" }.Concat(Each("MERCH.PRICING.PRICE", A.View, A.Edit)).Concat(new[] { "MERCH.PRICING.PROMO.VIEW", "MERCH.PLANNING.OTB.VIEW" }));
            Role(t, "CATEGORY_HEAD", "Category Head", null, "Approves prices, promos and OTB.", new[] { "MERCH.*.*.*", "BI.SALES.*.*" });
            Role(t, "FIN_EXEC", "Finance Executive", "FIN", "Books vendor invoices and journals.",
                Each("FIN.AP.VENDOR_INVOICE", A.View, A.Create, A.Edit).Concat(Each("FIN.GL.JOURNAL", A.View, A.Create)).Concat(new[] { "FIN.REPORTS.PNL.VIEW" }));
            Role(t, "FIN_CONTROLLER", "Financial Controller", null, "Approves payments and journals.", new[] { "FIN.*.*.*", "PAYROLL.PROCESS.PAYROLL_RUN.APPROVE", "BI.*.*.VIEW" });
            Role(t, "HR_EXEC", "HR Executive", "HRMS", "Maintains employee master and attendance.",
                Each("HRMS.CORE.EMPLOYEE", A.View, A.Create, A.Edit).Concat(new[] { "HRMS.ATTENDANCE.*.*", "HRMS.LEAVE.*.VIEW" }));
            Role(t, "HR_MANAGER", "HR Manager", null, "Full HRMS plus payroll view.", new[] { "HRMS.*.*.*", "PAYROLL.*.*.VIEW", "BI.HR.*.*" });
            Role(t, "PAYROLL_ADMIN", "Payroll Administrator", "PAYROLL", "Runs monthly payroll.", new[] { "PAYROLL.*.*.*" });
            Role(t, "BUYER", "Buyer", "PROC", "Raises POs and RFQs.",
                Each("PROC.PO.PURCHASE_ORDER", A.View, A.Create, A.Edit, A.Print).Concat(new[] { "PROC.RFQ.*.*", "PROC.VENDOR.VENDOR.VIEW" }));
            Role(t, "PROC_HEAD", "Procurement Head", "PROC", "Approves POs and vendors.", new[] { "PROC.*.*.*" });
            Role(t, "CRM_EXEC", "CRM Executive", "CRM", "Handles loyalty queries.",
                new[] { "CRM.CUSTOMER.CUSTOMER.VIEW" }.Concat(Each("CRM.LOYALTY.POINTS", A.View, A.Edit)).Concat(new[] { "CRM.CAMPAIGN.*.VIEW" }));
            Role(t, "ECOM_OPS", "E-commerce Operations", "ECOM", "Manages online orders.", new[] { "ECOM.*.*.*" });
            Role(t, "IT_SUPPORT", "IT Support Engineer", null, "Helpdesk and user look-up.",
                new[] { "ITSM.*.*.*", "UMS.USERS.USER.VIEW", "POS.CONFIG.*.*", "AMS.DASHBOARD.OVERVIEW.VIEW", "AMS.ASSETS.ASSET.VIEW" });
            Role(t, "ASSET_ADMIN", "Asset Administrator", "AMS", "Full control of the Asset Management System incl. setup and disposal.", new[] { "AMS.*.*.*" });
            Role(t, "ASSET_MANAGER", "Asset Manager", "AMS", "Runs assets of a location: check-out/in, audits, maintenance, approvals. Assign with a location scope.",
                new[] { "AMS.DASHBOARD.*.*", "AMS.ASSETS.*.*", "AMS.LICENSES.*.*", "AMS.ACCESSORIES.*.*", "AMS.CONSUMABLES.*.*", "AMS.COMPONENTS.*.*",
                        "AMS.REPORTS.*.*", "AMS.SETUP.*.VIEW", "AMS.SELF.*.*" });

            Role(VendorTenantId, "VENDOR_USER", "Vendor User", "PROC", "Supplier staff: view POs, respond to RFQs, raise bills.",
                Each("PROC.PO.PURCHASE_ORDER", A.View, A.Print).Concat(new[] { "PROC.RFQ.RFQ.VIEW" }).Concat(Each("PROC.INVOICE.VENDOR_BILL", A.View, A.Create, A.Edit)));
            Role(VendorTenantId, "VENDOR_ADMIN", "Vendor Administrator", "PROC", "Supplier admin; manages own vendor profile.",
                new[] { "PROC.PO.*.VIEW", "PROC.PO.*.PRINT", "PROC.RFQ.*.*", "PROC.INVOICE.*.*" }.Concat(Each("PROC.VENDOR.VENDOR", A.View, A.Edit)));

            Role(UatTenantId, "SUPER_ADMIN", "Super Administrator", null, "UAT full access.", new[] { "*.*.*.*" }, true);
            Role(UatTenantId, "TESTER", "QA Tester", null, "Functional testing across apps (no deletes).", new[] { "*.*.*.VIEW", "*.*.*.CREATE", "*.*.*.EDIT" });
        }

        // ------------------------------------------------------------ schedules & holidays

        public Guid ScheduleId(string code) => Schedules.First(s => s.Code == code).Id;

        private void SeedSchedules()
        {
            var mon2sat = new[] { Weekday.Mon, Weekday.Tue, Weekday.Wed, Weekday.Thu, Weekday.Fri, Weekday.Sat };
            var allDays = Enum.GetValues<Weekday>();
            void S(Guid tenant, string code, string name, bool excludeHolidays, params TimeWindow[] windows) =>
                Schedules.Add(new Schedule(IdFor($"sch:{tenant}:{code}"), tenant, code, name, "Asia/Kolkata", excludeHolidays, windows));

            S(VmmTenantId, "GENERAL", "General Office Hours", true, new TimeWindow(mon2sat, "09:30", "18:30"));
            S(VmmTenantId, "STORE", "Store Operating Hours", false, new TimeWindow(allDays, "08:00", "23:00"));
            S(VmmTenantId, "WH_NIGHT", "Warehouse Night Shift", false, new TimeWindow(allDays, "22:00", "06:00"));
            S(VmmTenantId, "FIN_CLOSE", "Finance Month-end Extended", true, new TimeWindow(mon2sat, "08:00", "22:00"));
            S(VendorTenantId, "VENDOR_HOURS", "Vendor Portal Hours", true, new TimeWindow(new[] { Weekday.Mon, Weekday.Tue, Weekday.Wed, Weekday.Thu, Weekday.Fri }, "09:00", "19:00"));

            var holidays = new (string Date, string Name, string[] Regions)[]
            {
                ("2026-01-26", "Republic Day", Array.Empty<string>()),
                ("2026-01-14", "Makar Sankranti / Pongal", new[] { "South" }),
                ("2026-03-04", "Holi", new[] { "North", "Central", "West" }),
                ("2026-08-15", "Independence Day", Array.Empty<string>()),
                ("2026-10-02", "Gandhi Jayanti", Array.Empty<string>()),
                ("2026-10-20", "Dussehra", Array.Empty<string>()),
                ("2026-11-08", "Diwali", Array.Empty<string>()),
                ("2026-12-25", "Christmas", Array.Empty<string>()),
            };
            foreach (var h in holidays)
                Holidays.Add(new Holiday(IdFor($"hol:{h.Date}"), VmmTenantId, DateOnly.Parse(h.Date), h.Name, h.Regions));
        }

        // ------------------------------------------------------------ policies

        private int _conditionSeq;
        private PolicyCondition C(string attribute, ConditionOperator op, string value, bool valueIsAttribute = false) =>
            new(IdFor($"cond:{++_conditionSeq}"), attribute, op, value, valueIsAttribute);

        private static PolicyTarget T(string app, string module, string resource, string[] actions, params string[] roles) => new(app, module, resource, actions, roles);
        private static readonly string[] AllActions = { "*" };

        private void P(string name, string description, PolicyEffect effect, int priority, ConditionMatch match, PolicyTarget target, PolicyCondition[] conditions, Guid? tenantId = null) =>
            Policies.Add(new Policy(IdFor($"pol:{name}"), tenantId ?? VmmTenantId, name, description, effect, target, match, conditions, priority, true,
                "ums.admin", new DateTime(2026, 6, 1, 10, 0, 0)));

        private void SeedPolicies()
        {
            var general = ScheduleId("GENERAL").ToString();
            var store = ScheduleId("STORE").ToString();
            var all = ConditionMatch.All;
            var O = ConditionOperator.Eq;

            P("Finance executives only during office hours", "Finance executives can use the ERP only within General Office Hours (holidays excluded). Controllers are exempt.",
                PolicyEffect.Restrict, 10, all, T("FIN", "*", "*", AllActions, "FIN_EXEC"), new[] { C("env.now", ConditionOperator.InSchedule, general) });
            P("Payroll access only during office hours", "Payroll processing allowed only Mon–Sat 09:30–18:30, not on holidays.",
                PolicyEffect.Restrict, 10, all, T("PAYROLL", "*", "*", AllActions), new[] { C("env.now", ConditionOperator.InSchedule, general) });
            P("POS staff: store hours on POS terminal", "Cashiers and supervisors can bill only during store hours and only from a POS terminal.",
                PolicyEffect.Restrict, 20, all, T("POS", "*", "*", AllActions, "CASHIER", "SHIFT_SUPERVISOR"),
                new[] { C("env.now", ConditionOperator.InSchedule, store), C("env.channel", O, "POS Terminal") });
            P("Supervisor refund limit ₹10,000", "Refunds above ₹10,000 must be done by the Store Manager.",
                PolicyEffect.Deny, 30, all, T("POS", "BILLING", "*", new[] { "Refund" }, "SHIFT_SUPERVISOR"), new[] { C("resource.amount", ConditionOperator.Gt, "10000") });
            P("HR executives edit employees of own region", "Region of the employee record must equal the HR executive’s region.",
                PolicyEffect.Restrict, 40, all, T("HRMS", "CORE", "EMPLOYEE", new[] { "Edit", "Delete" }, "HR_EXEC"), new[] { C("resource.region", O, "user.region", true) });
            P("Payslip: own record or HR/Payroll", "An employee can only see their own payslip unless they are HR Manager / Payroll Admin.",
                PolicyEffect.Restrict, 40, ConditionMatch.Any, T("HRMS", "ESS", "PAYSLIP", AllActions),
                new[] { C("resource.ownerEmpCode", O, "user.empCode", true), C("user.roles", ConditionOperator.In, "HR_MANAGER,PAYROLL_ADMIN") });
            P("Designated approver may view & approve PO", "Resource-based grant: the approver named on a PO can view/approve it even without a procurement role.",
                PolicyEffect.Grant, 50, all, T("PROC", "PO", "PURCHASE_ORDER", new[] { "View", "Approve" }), new[] { C("resource.approverEmpCode", O, "user.empCode", true) });
            P("No export from unmanaged devices", "Data export is blocked on personal / unmanaged devices.",
                PolicyEffect.Deny, 5, all, T("*", "*", "*", new[] { "Export" }), new[] { C("env.device", O, "Unmanaged") });
            P("Contract & vendor staff cannot delete", "Delete actions are restricted to permanent employees.",
                PolicyEffect.Deny, 5, all, T("*", "*", "*", new[] { "Delete" }), new[] { C("user.employmentType", ConditionOperator.In, "Contract,Intern,Vendor") });
            P("Finance approvals need MFA on office network/VPN", "Approvals in Finance require MFA and an Office LAN or VPN connection.",
                PolicyEffect.Restrict, 15, all, T("FIN", "*", "*", new[] { "Approve" }), new[] { C("user.mfaEnabled", O, "true"), C("env.network", ConditionOperator.In, "Office LAN,VPN") });
            P("Store managers: own store only", "Store managers may act only on store-tagged records of their own location.",
                PolicyEffect.Restrict, 25, all, T("STOREOPS", "*", "*", AllActions, "STORE_MANAGER"), new[] { C("resource.store", O, "user.location", true) });
            // --- Asset Management System ---
            P("Asset check-out only during operating hours", "Assets can be checked out only within Store Operating Hours (08:00–23:00).",
                PolicyEffect.Restrict, 20, all, T("AMS", "ASSETS", "ASSET", new[] { "Checkout" }), new[] { C("env.now", ConditionOperator.InSchedule, store) });
            P("Managers cannot dispose high-value assets", "Deleting an asset that cost more than ₹1,00,000 needs an Asset Administrator.",
                PolicyEffect.Deny, 30, all, T("AMS", "ASSETS", "ASSET", new[] { "Delete" }, "ASSET_MANAGER"), new[] { C("resource.cost", ConditionOperator.Gt, "100000") });
            P("Employees see assets issued to them", "Resource-based grant: anyone may view an asset that is checked out to them.",
                PolicyEffect.Grant, 50, all, T("AMS", "ASSETS", "ASSET", new[] { "View" }), new[] { C("resource.assignedToEmpCode", O, "user.empCode", true) });
            P("No self-approval of asset requests", "Segregation of duties: a request cannot be approved by the person who raised it.",
                PolicyEffect.Deny, 10, all, T("AMS", "ASSETS", "REQUEST", new[] { "Approve" }), new[] { C("resource.requestedByEmpCode", O, "user.empCode", true) });
            P("Asset request approval needs MFA", "Approving asset requests requires MFA.",
                PolicyEffect.Restrict, 15, all, T("AMS", "ASSETS", "REQUEST", new[] { "Approve" }), new[] { C("user.mfaEnabled", O, "true") });
            P("High-value assets not issued to contract staff", "Assets costing over ₹1,50,000 cannot be checked out to contract, intern or vendor staff.",
                PolicyEffect.Deny, 25, all, T("AMS", "ASSETS", "ASSET", new[] { "Checkout" }),
                new[] { C("resource.cost", ConditionOperator.Gt, "150000"), C("resource.assigneeEmploymentType", ConditionOperator.In, "Contract,Intern,Vendor") });
            P("Asset requests: own or asset staff", "A request can be seen or cancelled by its requester, asset staff or auditors.",
                PolicyEffect.Restrict, 40, ConditionMatch.Any, T("AMS", "ASSETS", "REQUEST", new[] { "View", "Void" }),
                new[] { C("resource.requestedByEmpCode", O, "user.empCode", true), C("user.roles", ConditionOperator.In, "ASSET_MANAGER,ASSET_ADMIN,SUPER_ADMIN,AUDITOR") });

            P("Vendors see only their own POs", "PO vendor code must match the vendor user’s vendor code.",
                PolicyEffect.Restrict, 10, all, T("PROC", "*", "*", AllActions),
                new[] { C("resource.vendorCode", O, "user.vendorCode", true), C("env.now", ConditionOperator.InSchedule, ScheduleId("VENDOR_HOURS").ToString()) }, VendorTenantId);
        }

        // ------------------------------------------------------------ users

        private static readonly string[] First = { "Aarav", "Vivaan", "Aditya", "Vihaan", "Arjun", "Sai", "Reyansh", "Krishna", "Ishaan", "Rohan", "Ananya", "Diya", "Priya", "Saanvi", "Aadhya", "Kavya", "Isha", "Neha", "Pooja", "Riya", "Rahul", "Amit", "Sunil", "Deepak", "Manish", "Sneha", "Meera", "Nisha", "Anjali", "Vikram", "Karan", "Suresh", "Ramesh", "Lakshmi", "Divya", "Farhan", "Imran", "Simran", "Harpreet", "Gaurav" };
        private static readonly string[] Last = { "Sharma", "Verma", "Gupta", "Singh", "Kumar", "Agarwal", "Mehta", "Jain", "Reddy", "Nair", "Iyer", "Patel", "Shah", "Das", "Banerjee", "Chauhan", "Yadav", "Mishra", "Pandey", "Khan", "Bhardwaj", "Joshi", "Malhotra", "Kapoor", "Saxena" };
        private static readonly Dictionary<string, string[]> Stores = new()
        {
            ["North"] = new[] { "S001", "S012", "S045", "S046", "S078" },
            ["East"] = new[] { "S101", "S115", "S130" },
            ["West"] = new[] { "S201", "S214", "S233" },
            ["South"] = new[] { "S301", "S322", "S340" },
            ["Central"] = new[] { "S401", "S418" },
        };

        private sealed record Profile(string Dept, string Designation, string[] Roles, int Weight, string Site, bool ScopeStore = false);

        private static readonly Profile[] Profiles =
        {
            new("Retail Operations", "Cashier", new[] { "CASHIER" }, 30, "store", true),
            new("Retail Operations", "Shift Supervisor", new[] { "SHIFT_SUPERVISOR" }, 10, "store", true),
            new("Retail Operations", "Store Manager", new[] { "STORE_MANAGER" }, 6, "store", true),
            new("Supply Chain", "Warehouse Executive", new[] { "WH_EXEC" }, 12, "dc"),
            new("Supply Chain", "Warehouse Manager", new[] { "WH_MANAGER" }, 3, "dc"),
            new("Merchandising", "Merchandiser", new[] { "MERCHANDISER" }, 6, "ho"),
            new("Merchandising", "Category Head", new[] { "CATEGORY_HEAD" }, 2, "ho"),
            new("Finance", "Accounts Executive", new[] { "FIN_EXEC" }, 6, "ho"),
            new("Finance", "Financial Controller", new[] { "FIN_CONTROLLER" }, 1, "ho"),
            new("Human Resources", "HR Executive", new[] { "HR_EXEC" }, 5, "ho"),
            new("Human Resources", "HR Manager", new[] { "HR_MANAGER" }, 1, "ho"),
            new("Human Resources", "Payroll Specialist", new[] { "PAYROLL_ADMIN" }, 1, "ho"),
            new("Procurement", "Buyer", new[] { "BUYER" }, 4, "ho"),
            new("Marketing", "CRM Executive", new[] { "CRM_EXEC" }, 3, "ho"),
            new("E-commerce", "E-com Operations", new[] { "ECOM_OPS" }, 3, "ho"),
            new("IT", "IT Support Engineer", new[] { "IT_SUPPORT" }, 3, "ho"),
            new("Internal Audit", "Internal Auditor", new[] { "AUDITOR" }, 1, "ho"),
        };

        private Profile PickProfile()
        {
            var r = _rnd.Next(Profiles.Sum(p => p.Weight));
            foreach (var p in Profiles)
                if ((r -= p.Weight) < 0) return p;
            return Profiles[0];
        }

        private RoleAssignment Assign(Guid tenantId, string roleCode, IReadOnlyDictionary<string, string>? scope = null, DateOnly? validFrom = null, DateOnly? validTo = null) =>
            new(RoleByCode(tenantId, roleCode).Id, scope ?? new Dictionary<string, string>(), validFrom, validTo, "ums.admin", new DateOnly(2026, 4, 1));

        private readonly HashSet<string> _emails = new();

        private User MakeUser(int n, Profile? profile = null, string? fullName = null, string? region = null, string? location = null,
            UserStatus? status = null, bool? mfa = null, EmploymentType? employmentType = null, bool forceNoExpiry = false)
        {
            var name = fullName ?? $"{Pick(First)} {Pick(Last)}";
            profile ??= PickProfile();
            region ??= Pick(Regions);
            location ??= profile.Site switch
            {
                "store" => Pick(Stores[region]),
                "dc" => $"DC-{region[0]}R1",
                _ => "HO-GGN"
            };
            var email = $"{string.Join('.', name.ToLowerInvariant().Split(' '))}@vmm.example";
            if (!_emails.Add(email)) email = email.Replace("@", $"{n}@");
            var empType = employmentType ?? (_rnd.NextDouble() < 0.12 ? EmploymentType.Contract : EmploymentType.Permanent);
            var r = _rnd.NextDouble();
            var userStatus = status ?? (r < 0.04 ? UserStatus.Locked : r < 0.09 ? UserStatus.Inactive : UserStatus.Active);
            var userMfa = mfa ?? (profile.Site == "ho" ? _rnd.NextDouble() < 0.85 : _rnd.NextDouble() < 0.3);
            DateOnly? expiry = !forceNoExpiry && empType == EmploymentType.Contract ? DateOnly.FromDateTime(_now.AddDays(_rnd.Next(200))) : null;
            var phone = PhoneNumber.Create("+91", $"9{_rnd.Next(100_000_000, 999_999_999)}");

            var user = new User(IdFor($"user:VMM{10000 + n}"), VmmTenantId, $"VMM{10000 + n}", name, new Email(email), phone);
            user.UpdateProfile(user.EmpCode, name, user.Email, phone, profile.Dept, profile.Designation, location, region, empType, userStatus, userMfa, expiry);
            user.RecordLogin(_now.AddDays(-_rnd.Next(20)));
            user.SetAttributes(new Dictionary<string, string>
            {
                ["costCenter"] = $"CC-{profile.Dept[..Math.Min(3, profile.Dept.Length)].ToUpperInvariant()}-{region[0]}",
                ["grade"] = Pick(new[] { "L1", "L2", "L3", "M1", "M2" }),
            });
            user.AssignRole(Assign(VmmTenantId, "EMPLOYEE"));
            foreach (var code in profile.Roles)
                user.AssignRole(Assign(VmmTenantId, code, profile.ScopeStore ? new Dictionary<string, string> { ["store"] = location } : null));
            user.ClearDomainEvents();
            return user;
        }

        private void SeedUsers()
        {
            var admin = new Profile("IT", "Head – IT Applications", new[] { "UMS_ADMIN", "SUPER_ADMIN" }, 0, "ho");
            var perm = EmploymentType.Permanent;
            var active = UserStatus.Active;

            var aarav = MakeUser(1, Profiles[2], "Aarav Sharma", "North", "S045", active, true, perm, true);
            aarav.SetRoleAssignments(new[] { Assign(VmmTenantId, "EMPLOYEE"), Assign(VmmTenantId, "STORE_MANAGER", new Dictionary<string, string> { ["store"] = "S045,S046" }) });
            Users.Add(aarav);
            Users.Add(MakeUser(2, Profiles[0], "Priya Verma", "North", "S045", active, null, perm, true));
            Users.Add(MakeUser(3, Profiles[1], "Rohit Gupta", "North", "S045", active, null, perm, true));
            Users.Add(MakeUser(4, Profiles[7], "Neha Singh", "North", null, active, false, perm, true));
            Users.Add(MakeUser(5, Profiles[8], "Vikram Mehta", "North", null, active, true, perm, true));
            Users.Add(MakeUser(6, Profiles[9], "Kavya Nair", "South", null, active, true, perm, true));
            var imran = MakeUser(7, Profiles[3], "Imran Khan", "West", null, active, null, EmploymentType.Contract, true);
            imran.UpdateProfile(imran.EmpCode, imran.Name, imran.Email, imran.Phone, imran.Department, imran.Designation, imran.Location, imran.Region,
                imran.EmploymentType, imran.Status, imran.MfaEnabled, new DateOnly(2027, 3, 31));
            Users.Add(imran);
            var sneha = MakeUser(8, Profiles[15], "Sneha Iyer", "North", null, active, true, perm, true);
            // IT asset manager for the head office only (resource-based scope)
            sneha.AssignRole(Assign(VmmTenantId, "ASSET_MANAGER", new Dictionary<string, string> { ["location"] = "HO-GGN" }));
            Users.Add(sneha);
            Users.Add(MakeUser(9, admin, "Ankit Saxena", "North", null, active, true, perm, true));
            var rahul = MakeUser(10, Profiles[12], "Rahul Agarwal", "North", null, active, true, perm, true);
            rahul.AssignRole(Assign(VmmTenantId, "ASSET_ADMIN"));
            // temporary access example: Rahul covered procurement head duties for one week (already expired)
            rahul.AssignRole(Assign(VmmTenantId, "PROC_HEAD", null, new DateOnly(2026, 10, 3), new DateOnly(2026, 10, 10)));
            Users.Add(rahul);

            for (var i = 0; i < 290; i++) Users.Add(MakeUser(i + 11));

            var vendors = new[] { ("Ravi Traders", "V1001"), ("Shree Textiles", "V1002"), ("FreshFarm Foods", "V1003"), ("Metro Plastics", "V1004") };
            foreach (var (company, code) in vendors)
            {
                var domain = company.ToLowerInvariant().Replace(" ", "");
                foreach (var (suffix, mailbox, designation, role, mfa) in new[] { ("01", "admin", "Vendor Admin", "VENDOR_ADMIN", true), ("02", "sales", "Sales Executive", "VENDOR_USER", false) })
                {
                    var empCode = $"{code}-{suffix}";
                    var u = new User(IdFor($"user:{empCode}"), VendorTenantId, empCode, $"{Pick(First)} {Pick(Last)}", new Email($"{mailbox}@{domain}.example"), PhoneNumber.Create("+91", "9810000000"));
                    u.UpdateProfile(empCode, u.Name, u.Email, u.Phone, company, designation, "EXT", Pick(Regions), EmploymentType.Vendor, UserStatus.Active, mfa, new DateOnly(2027, 3, 31));
                    u.SetAttributes(new Dictionary<string, string> { ["vendorCode"] = code, ["company"] = company });
                    u.AssignRole(Assign(VendorTenantId, role));
                    u.RecordLogin(_now.AddDays(-_rnd.Next(1, 6)));
                    u.ClearDomainEvents();
                    Users.Add(u);
                }
            }

            for (var n = 1; n <= 3; n++)
            {
                var empCode = $"UAT{n:000}";
                var u = new User(IdFor($"user:{empCode}"), UatTenantId, empCode, $"{Pick(First)} {Pick(Last)}", new Email($"qa{n}@vmm.example"), null);
                u.UpdateProfile(empCode, u.Name, u.Email, null, "IT", "QA Engineer", "HO-GGN", "North", EmploymentType.Permanent, UserStatus.Active, true, null);
                u.AssignRole(Assign(UatTenantId, "TESTER"));
                u.ClearDomainEvents();
                Users.Add(u);
            }
        }

        // ------------------------------------------------------------ logs

        private void SeedLogs()
        {
            var ips = new[] { "10.12.4.21", "10.12.8.5", "10.40.1.77", "172.16.20.4", "10.88.3.19", "49.36.112.8" };
            var audit = new (string Entity, AuditAction Action, string Summary)[]
            {
                ("Role", AuditAction.Update, "Added POS.BILLING.DISCOUNT.APPROVE to SHIFT_SUPERVISOR"),
                ("User", AuditAction.Assign, "Assigned STORE_MANAGER (store=S045,S046) to Aarav Sharma"),
                ("User", AuditAction.Create, "Created user Imran Khan (Contract, valid to 2027-03-31)"),
                ("Policy", AuditAction.Create, "Created policy 'No export from unmanaged devices'"),
                ("User", AuditAction.Update, "Locked account after 5 failed logins"),
                ("Policy", AuditAction.Update, "Changed priority of 'Supervisor refund limit ₹10,000' to 30"),
                ("User", AuditAction.Revoke, "Revoked FIN_EXEC from transferred employee"),
                ("Application", AuditAction.Update, "Enabled Procurement & Vendor Portal for Vendor & Partner Network"),
                ("Schedule", AuditAction.Update, "Updated General Office Hours: Mon–Sat 09:30–18:30"),
                ("User", AuditAction.Reset, "Password reset & MFA re-enrolment"),
                ("Role", AuditAction.Create, "Created role ECOM_OPS"),
                ("User", AuditAction.Assign, "Temporary PROC_HEAD to Rahul Agarwal (03-Oct → 10-Oct)"),
            };
            for (var i = 0; i < audit.Length; i++)
            {
                var (entity, action, summary) = audit[i];
                AuditLogs.Add(new AuditLog(IdFor($"al:{i}"), VmmTenantId, _now.AddHours(-i * 17), i % 3 == 0 ? "ankit.saxena" : i % 3 == 1 ? "sneha.iyer" : "ums.admin",
                    entity, $"{entity.ToLowerInvariant()}-{100 + i}", action, summary,
                    action == AuditAction.Update ? "{\"status\":\"Active\"}" : null, $"{{\"summary\":\"{summary.Replace("\"", "'")}\"}}",
                    Pick(ips), Pick(new[] { "Chrome / Windows 11", "Edge / Windows 10", "Safari / macOS" })));
            }

            var samples = new (string App, string Route, string Action, AccessDecisionResult Decision, string Reason)[]
            {
                ("POS", "/billing/invoice/new", "POS.BILLING.INVOICE.CREATE", AccessDecisionResult.Allow, "permitted"),
                ("POS", "/billing/invoice/8812/refund", "POS.BILLING.INVOICE.REFUND", AccessDecisionResult.Deny, "Policy: Deny 'Supervisor refund limit ₹10,000'"),
                ("FIN", "/ap/vendor-invoice/220/approve", "FIN.AP.VENDOR_INVOICE.APPROVE", AccessDecisionResult.Deny, "Permission: missing FIN.AP.VENDOR_INVOICE.APPROVE"),
                ("FIN", "/gl/journal", "FIN.GL.JOURNAL.VIEW", AccessDecisionResult.Deny, "Policy: Restrict 'Finance executives only during office hours'"),
                ("HRMS", "/ess/payslip/2026-09", "HRMS.ESS.PAYSLIP.VIEW", AccessDecisionResult.Allow, "permitted"),
                ("WMS", "/inbound/grn/new", "WMS.INBOUND.GRN.CREATE", AccessDecisionResult.Allow, "permitted"),
                ("MERCH", "/pricing/price/approve", "MERCH.PRICING.PRICE.APPROVE", AccessDecisionResult.Deny, "Permission: missing MERCH.PRICING.PRICE.APPROVE"),
                ("BI", "/sales/dashboard/export", "BI.SALES.SALES_DASH.EXPORT", AccessDecisionResult.Deny, "Policy: Deny 'No export from unmanaged devices'"),
                ("STOREOPS", "/tasks", "STOREOPS.TASKS.TASK.VIEW", AccessDecisionResult.Allow, "permitted"),
                ("PROC", "/po/4410/approve", "PROC.PO.PURCHASE_ORDER.APPROVE", AccessDecisionResult.Allow, "granted by policy 'Designated approver may view & approve PO'"),
            };
            var actors = Users.Where(u => u.TenantId == VmmTenantId).Take(50).ToList();
            for (var i = 0; i < 80; i++)
            {
                var u = Pick(actors);
                var s = Pick(samples);
                ActionLogs.Add(new UserActionLog(IdFor($"ua:{i}"), VmmTenantId, _now.AddMinutes(-i * 37), u.Id, u.Name, s.App, s.Route, s.Action, s.Decision, s.Reason, Pick(ips)));
            }

            var exceptions = new (string App, ExceptionSeverity Severity, string Message, string Path)[]
            {
                ("POS", ExceptionSeverity.Error, "TimeoutException: Payment gateway did not respond in 30s", "/api/billing/payment"),
                ("WMS", ExceptionSeverity.Error, "DbUpdateConcurrencyException: Stock row was modified by another user", "/api/inventory/stock/88231"),
                ("HRMS", ExceptionSeverity.Warning, "SCIM sync: 3 employees skipped (missing manager)", "/scim/v2/Users"),
                ("UMS", ExceptionSeverity.Critical, "Redis connection lost; falling back to DB for token validation", "/connect/token"),
                ("FIN", ExceptionSeverity.Error, "HttpRequestException: ERP connector returned 502 Bad Gateway", "/api/ap/vendor-invoice"),
                ("UMS", ExceptionSeverity.Error, "PolicyEvaluationException: attribute resource.amount is not numeric", "/api/authorize"),
                ("ECOM", ExceptionSeverity.Warning, "Order 55120 refund retried 3 times", "/api/orders/55120/refund"),
                ("CRM", ExceptionSeverity.Error, "NullReferenceException in LoyaltyPointsService.Redeem", "/api/loyalty/redeem"),
            };
            for (var i = 0; i < 18; i++)
            {
                var e = exceptions[i % exceptions.Length];
                var stack = $"{e.Message.Split(':')[0]}\n   at VMM.{e.App}.Services.Handler.ExecuteAsync()\n   at VMM.Common.Middleware.ExceptionLoggerMiddleware.Invoke(HttpContext ctx)";
                ExceptionLogs.Add(new ExceptionLog(IdFor($"ex:{i}"), VmmTenantId, _now.AddHours(-i * 5), e.App, e.Severity, e.Message, stack,
                    i % 3 == 0 ? null : Pick(Users.Take(10).ToList()).Name, e.Path, Pick(ips), i > 9));
            }
        }
    }
}

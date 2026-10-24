namespace AMS.Domain.Security
{
    /// <summary>The signed-in UMS user on whose behalf AMS acts (from the UMS access token).</summary>
    public sealed record Actor(Guid UserId, Guid TenantId, string EmpCode, string Name, string Email);

    /// <summary>
    /// One question for UMS: may the current user perform <see cref="Action"/> on <see cref="Resource"/> in <see cref="Module"/> of AMS,
    /// given these attributes of the specific record? Module/Resource/Action are catalogue identifiers registered in UMS - never role names.
    /// </summary>
    public sealed record AccessCheck(string Module, string Resource, string Action, IReadOnlyDictionary<string, string>? Attributes = null)
    {
        public string Key => $"AMS.{Module}.{Resource}.{Action}".ToUpperInvariant();
    }

    /// <summary>The AMS permission catalogue as registered in UMS (application code "AMS"). Identifiers only - no access rules live here.</summary>
    public static class Perm
    {
        public const string App = "AMS";

        public static class Modules
        {
            public const string Dashboard = "DASHBOARD", Assets = "ASSETS", Licenses = "LICENSES", Accessories = "ACCESSORIES", Consumables = "CONSUMABLES",
                Components = "COMPONENTS", Reports = "REPORTS", Setup = "SETUP", Self = "SELF";
        }

        public static class Resources
        {
            public const string Overview = "OVERVIEW", Asset = "ASSET", Maintenance = "MAINTENANCE", Request = "REQUEST", License = "LICENSE",
                Accessory = "ACCESSORY", Consumable = "CONSUMABLE", Component = "COMPONENT", Activity = "ACTIVITY", Report = "REPORT",
                Category = "CATEGORY", Manufacturer = "MANUFACTURER", Model = "MODEL", Supplier = "SUPPLIER", Location = "LOCATION",
                Department = "DEPARTMENT", Status = "STATUS", MyAssets = "MY_ASSETS";
        }

        public static class Actions
        {
            public const string View = "View", Create = "Create", Edit = "Edit", Delete = "Delete", Approve = "Approve", Export = "Export", Print = "Print",
                Void = "Void", Checkout = "Checkout", Checkin = "Checkin", Audit = "Audit", Request = "Request";
        }

        public static string Key(string module, string resource, string action) => $"{App}.{module}.{resource}.{action}".ToUpperInvariant();

        /// <summary>Module/resource of a stock kind (accessories, consumables and components are separate catalogue entries).</summary>
        public static (string Module, string Resource) Stock(Enums.StockKind kind) => kind switch
        {
            Enums.StockKind.Accessory => (Modules.Accessories, Resources.Accessory),
            Enums.StockKind.Consumable => (Modules.Consumables, Resources.Consumable),
            _ => (Modules.Components, Resources.Component),
        };

        /// <summary>Resource of a setup kind (all live in the SETUP module).</summary>
        public static string Setup(Enums.SetupKind kind) => kind switch
        {
            Enums.SetupKind.Category => Resources.Category,
            Enums.SetupKind.Manufacturer => Resources.Manufacturer,
            Enums.SetupKind.Model => Resources.Model,
            Enums.SetupKind.Supplier => Resources.Supplier,
            Enums.SetupKind.Location => Resources.Location,
            Enums.SetupKind.Department => Resources.Department,
            _ => Resources.Status,
        };
    }
}

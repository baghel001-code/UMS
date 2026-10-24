namespace AMS.Core.Security
{
    /// <summary>The signed-in UMS user acting in AMS.</summary>
    public sealed record Actor(Guid UserId, Guid TenantId, string EmpCode, string Name);

    /// <summary>
    /// One question for UMS: may the current user perform <see cref="Action"/> on <see cref="Resource"/> in <see cref="Module"/> of AMS,
    /// given these attributes of the specific record (location, cost, assignee, status…)?
    /// Module/Resource/Action are catalogue identifiers registered in UMS — never role names.
    /// </summary>
    public sealed record AccessCheck(string Module, string Resource, string Action, IReadOnlyDictionary<string, string>? Attributes = null)
    {
        public string Key => $"AMS.{Module}.{Resource}.{Action}".ToUpperInvariant();
    }

    /// <summary>
    /// Policy enforcement point of AMS. Every service operation calls <see cref="EnsureAsync"/> before it reads or changes data;
    /// the decision itself is always made by UMS (roles, scopes and ABAC policies managed in the UMS admin portal).
    /// </summary>
    public interface IAccessGuard
    {
        Actor Actor { get; }

        /// <summary>Asks UMS and throws <see cref="AccessDeniedException"/> when denied. The decision is written to the UMS user action trail.</summary>
        Task EnsureAsync(AccessCheck check, CancellationToken ct = default);

        /// <summary>Asks UMS many questions at once (list filtering, which buttons to show). Not logged.</summary>
        Task<IReadOnlyList<bool>> CanAsync(IReadOnlyList<AccessCheck> checks, CancellationToken ct = default);
    }

    public class AccessDeniedException : Exception
    {
        public string Permission { get; }
        public AccessDeniedException(string permission, string reason) : base(reason) => Permission = permission;
    }

    /// <summary>A business rule was broken (e.g. checking out an asset that is already checked out).</summary>
    public class AmsValidationException : Exception
    {
        public AmsValidationException(string message) : base(message) { }
    }

    public static class GuardExtensions
    {
        public static async Task<bool> CanAsync(this IAccessGuard guard, AccessCheck check, CancellationToken ct = default) =>
            (await guard.CanAsync(new[] { check }, ct))[0];

        /// <summary>Keeps only the items the user may see/act on, asking UMS once for the whole list.</summary>
        public static async Task<List<T>> FilterAsync<T>(this IAccessGuard guard, IReadOnlyList<T> items, Func<T, AccessCheck> check, CancellationToken ct = default)
        {
            if (items.Count == 0) return new List<T>();
            var allowed = await guard.CanAsync(items.Select(check).ToList(), ct);
            return items.Where((_, i) => allowed[i]).ToList();
        }
    }

    /// <summary>The AMS permission catalogue as registered in UMS (application code "AMS"). Identifiers only — no access rules live here.</summary>
    public static class Perm
    {
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
    }
}

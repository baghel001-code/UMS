using AMS.Core.Entities;
using AMS.Core.Security;
using System.Globalization;

namespace AMS.Core.Services
{
    /// <summary>
    /// Shared plumbing of the AMS services: the UMS guard, a tenant-scoped database and the activity log.
    /// Services never look at roles; they describe the action and the record to UMS and act only on an Allow.
    /// </summary>
    public abstract class AmsServiceBase
    {
        protected readonly IAccessGuard Guard;
        private readonly IAmsDbContextFactory _factory;
        protected readonly TimeProvider Time;

        protected AmsServiceBase(IAccessGuard guard, IAmsDbContextFactory factory, TimeProvider time)
        {
            Guard = guard;
            _factory = factory;
            Time = time;
        }

        protected Actor Me => Guard.Actor;
        protected DateTime Now => Time.GetLocalNow().DateTime;
        protected DateOnly Today => DateOnly.FromDateTime(Now);

        protected IAmsDbContext Db() => _factory.Create(Me.TenantId);

        protected Task Ensure(string module, string resource, string action, IReadOnlyDictionary<string, string>? attrs = null, CancellationToken ct = default) =>
            Guard.EnsureAsync(new AccessCheck(module, resource, action, attrs), ct);

        protected void Log(IAmsDbContext db, ItemType type, Guid id, string name, string? location, HistoryAction action, string? target = null, string? note = null) =>
            db.History.Add(new HistoryEntry
            {
                TenantId = Me.TenantId, At = Now, ActorEmpCode = Me.EmpCode, ActorName = Me.Name, ItemType = type, ItemId = id, ItemName = name,
                LocationCode = location, Action = action, Target = target, Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim()
            });

        protected static T Found<T>(T? item, string what) where T : class => item ?? throw new AmsValidationException($"{what} was not found.");

        protected static void Require(bool condition, string message)
        {
            if (!condition) throw new AmsValidationException(message);
        }
    }

    /// <summary>
    /// Attributes of a record sent to UMS with each check. UMS role scopes (e.g. location=HO-GGN) and ABAC policies
    /// (e.g. resource.cost &gt; 100000) are evaluated against these names.
    /// </summary>
    public static class ResourceAttrs
    {
        public static string Money(decimal v) => v.ToString("0.##", CultureInfo.InvariantCulture);

        public static Dictionary<string, string> For(Asset a)
        {
            var d = new Dictionary<string, string>
            {
                ["assetTag"] = a.AssetTag,
                ["cost"] = Money(a.PurchaseCost),
                ["requestable"] = a.Requestable ? "true" : "false",
            };
            if (a.Location != null) { d["location"] = a.Location.Code; d["region"] = a.Location.Region; }
            if (a.Model?.Category != null) d["category"] = a.Model.Category.Name;
            if (a.StatusLabel != null) { d["status"] = a.StatusLabel.Name; d["statusType"] = a.StatusLabel.Type.ToString(); }
            if (a.Department != null) d["department"] = a.Department.Name;
            if (a.AssignedToEmpCode != null) d["assignedToEmpCode"] = a.AssignedToEmpCode;
            return d;
        }

        public static Dictionary<string, string> For(License l)
        {
            var d = new Dictionary<string, string> { ["cost"] = Money(l.PurchaseCost), ["seats"] = l.Seats.ToString(CultureInfo.InvariantCulture) };
            if (l.Category != null) d["category"] = l.Category.Name;
            if (l.Manufacturer != null) d["manufacturer"] = l.Manufacturer.Name;
            return d;
        }

        public static Dictionary<string, string> For(StockItem s, int quantity = 1)
        {
            var d = new Dictionary<string, string> { ["cost"] = Money(s.UnitCost * Math.Max(quantity, 1)), ["quantity"] = quantity.ToString(CultureInfo.InvariantCulture) };
            if (s.Location != null) { d["location"] = s.Location.Code; d["region"] = s.Location.Region; }
            if (s.Category != null) d["category"] = s.Category.Name;
            return d;
        }

        public static Dictionary<string, string> For(AssetRequest r)
        {
            var d = r.Asset != null ? For(r.Asset) : new Dictionary<string, string>();
            d.Remove("assignedToEmpCode");
            d["requestedByEmpCode"] = r.RequestedByEmpCode;
            d["requestStatus"] = r.Status.ToString();
            return d;
        }

        public static Dictionary<string, string> Location(string? code)
        {
            var d = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(code)) d["location"] = code;
            return d;
        }

        /// <summary>Adds who the item goes to (policies such as "no high-value assets to contract staff" use these).</summary>
        public static Dictionary<string, string> WithAssignee(this Dictionary<string, string> d, Person p)
        {
            d["assigneeEmpCode"] = p.EmpCode;
            d["assigneeEmploymentType"] = p.EmploymentType;
            d["assigneeDepartment"] = p.Department;
            d["assigneeLocation"] = p.Location;
            d["assigneeRegion"] = p.Region;
            return d;
        }
    }
}

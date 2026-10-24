using AMS.Domain.Exceptions;
using AMS.Domain.Models;
using AMS.Application.Interfaces;
using System.Globalization;
using System.Text;

namespace AMS.Application.Common
{
    /// <summary>
    /// Attributes of a record sent to UMS with each check. UMS role scopes (e.g. location=HO-GGN) and ABAC policies
    /// (e.g. resource.cost &gt; 100000) are evaluated against these names.
    /// </summary>
    public static class ResourceAttrs
    {
        public static string Money(decimal v) => v.ToString("0.##", CultureInfo.InvariantCulture);

        public static Dictionary<string, string> For(AssetView a)
        {
            var d = new Dictionary<string, string>
            {
                ["assetTag"] = a.AssetTag,
                ["cost"] = Money(a.PurchaseCost),
                ["requestable"] = a.Requestable ? "true" : "false",
                ["location"] = a.LocationCode,
                ["region"] = a.Region,
                ["category"] = a.CategoryName,
                ["status"] = a.StatusName,
                ["statusType"] = a.StatusType.ToString(),
            };
            if (a.DepartmentName != null) d["department"] = a.DepartmentName;
            if (a.AssignedToEmpCode != null) d["assignedToEmpCode"] = a.AssignedToEmpCode;
            return d;
        }

        public static Dictionary<string, string> For(LicenseView l)
        {
            var d = new Dictionary<string, string> { ["cost"] = Money(l.PurchaseCost), ["seats"] = l.Seats.ToString(CultureInfo.InvariantCulture) };
            if (l.CategoryName != null) d["category"] = l.CategoryName;
            if (l.ManufacturerName != null) d["manufacturer"] = l.ManufacturerName;
            return d;
        }

        public static Dictionary<string, string> For(StockItemView s, int quantity = 1) => new()
        {
            ["cost"] = Money(s.UnitCost * Math.Max(quantity, 1)),
            ["quantity"] = quantity.ToString(CultureInfo.InvariantCulture),
            ["location"] = s.LocationCode,
            ["region"] = s.Region,
            ["category"] = s.CategoryName ?? "",
        };

        public static Dictionary<string, string> For(RequestView r) => new()
        {
            ["assetTag"] = r.AssetTag,
            ["cost"] = Money(r.AssetCost),
            ["location"] = r.LocationCode,
            ["region"] = r.Region,
            ["category"] = r.CategoryName,
            ["status"] = r.StatusName,
            ["requestedByEmpCode"] = r.RequestedByEmpCode,
            ["requestStatus"] = r.Status.ToString(),
        };

        /// <summary>Maintenance: the asset's attributes, with cost = the job's cost and assetCost = the asset's.</summary>
        public static Dictionary<string, string> For(MaintenanceView m) => new()
        {
            ["assetTag"] = m.AssetTag,
            ["cost"] = Money(m.Cost),
            ["assetCost"] = Money(m.AssetCost),
            ["location"] = m.LocationCode,
            ["region"] = m.Region,
            ["category"] = m.CategoryName,
            ["status"] = m.StatusName,
        };

        public static Dictionary<string, string> Location(string? code)
        {
            var d = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(code)) d["location"] = code;
            return d;
        }

        /// <summary>Who the item goes to (policies such as "no high-value assets to contract staff" use these).</summary>
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

    public static class DbResults
    {
        /// <summary>Turns a failed stored-procedure result into the matching API error.</summary>
        public static DbResult EnsureOk(this DbResult r) => r.Ok ? r : throw new AmsException(r.Code, r.Message);
    }

    public static class Csv
    {
        public static string Write(IEnumerable<string> header, IEnumerable<object?[]> rows)
        {
            static string Cell(object? v)
            {
                var s = v switch
                {
                    null => "",
                    DateTime t when t.TimeOfDay == TimeSpan.Zero => t.ToString("yyyy-MM-dd"),
                    DateTime t => t.ToString("yyyy-MM-dd HH:mm"),
                    decimal m => ResourceAttrs.Money(m),
                    _ => Convert.ToString(v, CultureInfo.InvariantCulture) ?? ""
                };
                // neutralise spreadsheet formulas and quote when needed
                if (s.Length > 0 && "=+-@".Contains(s[0])) s = "'" + s;
                return s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
            }
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", header.Select(Cell)));
            foreach (var r in rows) sb.AppendLine(string.Join(",", r.Select(Cell)));
            return sb.ToString();
        }
    }

    public static class Depreciation
    {
        /// <summary>Straight-line book value over the category's depreciation period (0 months = not depreciated).</summary>
        public static decimal CurrentValue(AssetView a, DateTime today, out int months)
        {
            months = a.PurchaseDate == null ? 0 : Math.Max(0, (today.Year - a.PurchaseDate.Value.Year) * 12 + today.Month - a.PurchaseDate.Value.Month);
            if (a.DepreciationMonths <= 0) return a.PurchaseCost;
            return Math.Round(a.PurchaseCost * Math.Max(0m, 1m - (decimal)months / a.DepreciationMonths), 2);
        }
    }

    /// <summary>India Standard Time unless configured otherwise.</summary>
    public sealed class BusinessClock : IBusinessClock
    {
        private readonly TimeProvider _time;
        private readonly TimeZoneInfo _zone;

        public BusinessClock(TimeProvider time, string timeZone = "Asia/Kolkata")
        {
            _time = time;
            try { _zone = TimeZoneInfo.FindSystemTimeZoneById(timeZone); }
            catch (TimeZoneNotFoundException) { _zone = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time"); }
        }

        public DateTime Now
        {
            get
            {
                var local = TimeZoneInfo.ConvertTime(_time.GetUtcNow(), _zone).DateTime;
                return new DateTime(local.Ticks - local.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Unspecified);
            }
        }
    }
}

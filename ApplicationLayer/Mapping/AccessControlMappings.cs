using ApplicationLayer.Features.AccessControl.DTOs;
using DomainLayer.Entities;
using DomainLayer.Services;
using DomainLayer.ValueObjects;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ApplicationLayer.Mapping
{
    // Hand-written mappings: the access-control aggregates use constructors and private collections,
    // which AutoMapper cannot populate without extra configuration.
    public static class AccessControlMappings
    {
        public static TenantDto ToDto(this Tenant t) => new(t.Id, t.Code, t.Name, t.IsActive, t.CreatedOn);

        public static ApplicationDto ToDto(this Application a) => new(a.Id, a.Code, a.Name, a.Type, a.Vendor, a.OwnerDept, a.Description, a.IsActive,
            a.TenantIds.ToList(),
            a.Modules.Select(m => new AppModuleDto(m.Id, m.Code, m.Name,
                m.Resources.Select(r => new AppResourceDto(r.Id, r.Code, r.Name, r.Actions.ToList())).ToList())).ToList());

        public static List<AppModule> ToModules(this IEnumerable<AppModuleDto> modules) =>
            modules.Select(m => new AppModule(m.Id == Guid.Empty ? Guid.NewGuid() : m.Id, m.Code.Trim().ToUpperInvariant(), m.Name.Trim(),
                m.Resources.Select(r => new AppResource(r.Id == Guid.Empty ? Guid.NewGuid() : r.Id, r.Code.Trim().ToUpperInvariant(), r.Name.Trim(),
                    r.Actions.Distinct().ToList())).ToList())).ToList();

        public static RoleDto ToDto(this Role r) =>
            new(r.Id, r.TenantId, r.Code, r.Name, r.Description, r.AppId, r.Permissions.ToList(), r.IsSystem, r.IsActive);

        public static RoleAssignmentDto ToDto(this RoleAssignment a) =>
            new(a.RoleId, new Dictionary<string, string>(a.Scope), a.ValidFrom, a.ValidTo, a.AssignedBy, a.AssignedOn);

        public static RoleAssignment ToAssignment(this RoleAssignmentDto a, string assignedBy, DateOnly today) =>
            new(a.RoleId,
                a.Scope.Where(kv => !string.IsNullOrWhiteSpace(kv.Key)).ToDictionary(kv => kv.Key.Trim(), kv => kv.Value.Trim()),
                a.ValidFrom, a.ValidTo, a.AssignedBy ?? assignedBy, a.AssignedOn ?? today);

        public static UserAccountDto ToAccountDto(this User u) => new(u.Id, u.TenantId, u.EmpCode, u.Name, u.Email.Value, u.Phone?.ToDisplay(),
            u.Department, u.Designation, u.Location, u.Region, u.EmploymentType, u.Status, u.MfaEnabled, u.LastLogin, u.AccountExpiry,
            new Dictionary<string, string>(u.Attributes), u.RoleAssignments.Select(a => a.ToDto()).ToList());

        private static string ToDisplay(this PhoneNumber p) => $"{p.CountryCode} {p.Number}";

        /// <summary>Accepts "+91 98100 00000", "+91-9810000000" or "9810000000" (defaults to +91). Returns null when empty.</summary>
        public static PhoneNumber? ParsePhone(string? phone)
        {
            if (string.IsNullOrWhiteSpace(phone)) return null;
            var m = Regex.Match(phone.Trim(), @"^(\+\d{1,3})?[\s-]*(.*)$");
            var country = m.Groups[1].Success ? m.Groups[1].Value : "+91";
            var digits = Regex.Replace(m.Groups[2].Value, @"\D", "");
            return PhoneNumber.Create(country, digits);
        }

        public static PolicyDto ToDto(this Policy p, IReadOnlyList<Schedule> schedules) => new(p.Id, p.TenantId, p.Name, p.Description, p.Effect,
            new PolicyTargetDto(p.Target.App, p.Target.Module, p.Target.Resource, p.Target.Actions.ToList(), p.Target.Roles.ToList()),
            p.Match, p.Conditions.Select(c => new PolicyConditionDto(c.Id, c.Attribute, c.Operator, c.Value, c.ValueIsAttribute)).ToList(),
            p.Priority, p.IsActive, p.CreatedBy, p.CreatedAt, ConditionEvaluator.Expression(p, schedules));

        public static PolicyTarget ToTarget(this PolicyTargetDto t)
        {
            static string Seg(string s) => string.IsNullOrWhiteSpace(s) ? "*" : s.Trim().ToUpperInvariant();
            var actions = t.Actions.Count == 0 || t.Actions.Contains("*") ? new List<string> { "*" } : t.Actions.Distinct().ToList();
            return new PolicyTarget(Seg(t.App), Seg(t.Module), Seg(t.Resource), actions, t.Roles.Select(r => r.Trim().ToUpperInvariant()).Distinct().ToList());
        }

        public static List<PolicyCondition> ToConditions(this IEnumerable<PolicyConditionDto> conditions) =>
            conditions.Select(c => new PolicyCondition(c.Id == Guid.Empty ? Guid.NewGuid() : c.Id, c.Attribute.Trim(), c.Operator, c.Value?.Trim() ?? string.Empty,
                c.ValueIsAttribute)).ToList();

        public static ScheduleDto ToDto(this Schedule s) => new(s.Id, s.TenantId, s.Code, s.Name, s.Timezone, s.ExcludeHolidays,
            s.Windows.Select(w => new TimeWindowDto(w.Days.ToList(), w.Start, w.End)).ToList());

        public static HolidayDto ToDto(this Holiday h) => new(h.Id, h.TenantId, h.Date, h.Name, h.Regions.ToList());

        public static AuditLogDto ToDto(this AuditLog a) => new(a.Id, a.TenantId, a.Timestamp, a.Actor, a.Entity, a.EntityId, a.Action, a.Summary,
            ParseJson(a.OldValues), ParseJson(a.NewValues), a.Ip, a.Device);

        private static JsonElement? ParseJson(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return JsonSerializer.Deserialize<JsonElement>(json); }
            catch (JsonException) { return JsonSerializer.SerializeToElement(json); }
        }

        public static UserActionLogDto ToDto(this UserActionLog a) =>
            new(a.Id, a.TenantId, a.Timestamp, a.UserId, a.UserName, a.App, a.Route, a.Action, a.Decision, a.Reason, a.Ip);

        public static ExceptionLogDto ToDto(this ExceptionLog e) =>
            new(e.Id, e.TenantId, e.Timestamp, e.App, e.Severity, e.Message, e.StackTrace, e.UserName, e.RequestPath, e.Ip, e.Resolved);
    }
}

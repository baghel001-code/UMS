using ApplicationLayer.Features.AccessControl.DTOs;
using DomainLayer.Enumerations;

namespace UMS.AdminPortal.Services
{
    // Mutable copies of the API DTOs (which are immutable records) for two-way binding in forms.

    public class ResourceEdit
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public HashSet<AccessAction> Actions { get; set; } = new() { AccessAction.View };
    }

    public class ModuleEdit
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public List<ResourceEdit> Resources { get; set; } = new();
    }

    public class ApplicationEdit
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public AppType Type { get; set; } = AppType.InHouse;
        public string Vendor { get; set; } = "VMM IT";
        public string OwnerDept { get; set; } = "";
        public string Description { get; set; } = "";
        public bool IsActive { get; set; } = true;
        public HashSet<Guid> TenantIds { get; set; } = new();
        public List<ModuleEdit> Modules { get; set; } = new();

        public static ApplicationEdit From(ApplicationDto a) => new()
        {
            Id = a.Id, Code = a.Code, Name = a.Name, Type = a.Type, Vendor = a.Vendor, OwnerDept = a.OwnerDept, Description = a.Description,
            IsActive = a.IsActive, TenantIds = a.TenantIds.ToHashSet(),
            Modules = a.Modules.Select(m => new ModuleEdit
            {
                Id = m.Id, Code = m.Code, Name = m.Name,
                Resources = m.Resources.Select(r => new ResourceEdit { Id = r.Id, Code = r.Code, Name = r.Name, Actions = r.Actions.ToHashSet() }).ToList()
            }).ToList()
        };

        public ApplicationDto ToDto() => new(Id, Code.Trim().ToUpperInvariant(), Name, Type, Vendor, OwnerDept, Description, IsActive, TenantIds.ToList(),
            Modules.Select(m => new AppModuleDto(m.Id, m.Code.Trim().ToUpperInvariant(), m.Name,
                m.Resources.Select(r => new AppResourceDto(r.Id, r.Code.Trim().ToUpperInvariant(), r.Name, Enum.GetValues<AccessAction>().Where(r.Actions.Contains).ToList())).ToList())).ToList());
    }

    public class RoleEdit
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid TenantId { get; set; }
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public Guid? AppId { get; set; }
        public List<string> Permissions { get; set; } = new();
        public bool IsSystem { get; set; }
        public bool IsActive { get; set; } = true;

        public static RoleEdit From(RoleDto r) => new()
        {
            Id = r.Id, TenantId = r.TenantId, Code = r.Code, Name = r.Name, Description = r.Description, AppId = r.AppId,
            Permissions = r.Permissions.ToList(), IsSystem = r.IsSystem, IsActive = r.IsActive
        };

        public RoleDto ToDto() => new(Id, TenantId, Code.Trim().ToUpperInvariant(), Name, Description, AppId, Permissions.ToList(), IsSystem, IsActive);
    }

    public class AssignmentEdit
    {
        public Guid RoleId { get; set; }
        public Dictionary<string, string> Scope { get; set; } = new();
        public DateTime? ValidFrom { get; set; }
        public DateTime? ValidTo { get; set; }
        public string? AssignedBy { get; set; }
        public DateOnly? AssignedOn { get; set; }

        public static AssignmentEdit From(RoleAssignmentDto a) => new()
        {
            RoleId = a.RoleId, Scope = new Dictionary<string, string>(a.Scope), AssignedBy = a.AssignedBy, AssignedOn = a.AssignedOn,
            ValidFrom = a.ValidFrom?.ToDateTime(TimeOnly.MinValue), ValidTo = a.ValidTo?.ToDateTime(TimeOnly.MinValue),
        };

        public RoleAssignmentDto ToDto() => new(RoleId, new Dictionary<string, string>(Scope),
            ValidFrom is null ? null : DateOnly.FromDateTime(ValidFrom.Value), ValidTo is null ? null : DateOnly.FromDateTime(ValidTo.Value), AssignedBy, AssignedOn);

        public bool IsActiveOn(DateOnly day) =>
            (ValidFrom is null || day >= DateOnly.FromDateTime(ValidFrom.Value)) && (ValidTo is null || day <= DateOnly.FromDateTime(ValidTo.Value));
    }

    public class UserEdit
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid TenantId { get; set; }
        public string EmpCode { get; set; } = "";
        public string FullName { get; set; } = "";
        public string Email { get; set; } = "";
        public string? Phone { get; set; }
        public string Department { get; set; } = "";
        public string Designation { get; set; } = "";
        public string Location { get; set; } = "";
        public string Region { get; set; } = "North";
        public EmploymentType EmploymentType { get; set; } = EmploymentType.Permanent;
        public UserStatus Status { get; set; } = UserStatus.Active;
        public bool MfaEnabled { get; set; } = true;
        public DateTime? LastLogin { get; set; }
        public DateTime? AccountExpiry { get; set; }
        public Dictionary<string, string> Attributes { get; set; } = new();
        public List<AssignmentEdit> Roles { get; set; } = new();

        public static UserEdit From(UserAccountDto u) => new()
        {
            Id = u.Id, TenantId = u.TenantId, EmpCode = u.EmpCode, FullName = u.FullName, Email = u.Email, Phone = u.Phone, Department = u.Department,
            Designation = u.Designation, Location = u.Location, Region = u.Region, EmploymentType = u.EmploymentType, Status = u.Status,
            MfaEnabled = u.MfaEnabled, LastLogin = u.LastLogin, AccountExpiry = u.AccountExpiry?.ToDateTime(TimeOnly.MinValue),
            Attributes = new Dictionary<string, string>(u.Attributes), Roles = u.Roles.Select(AssignmentEdit.From).ToList(),
        };

        public UserAccountDto ToDto() => new(Id, TenantId, EmpCode.Trim(), FullName.Trim(), Email.Trim(), string.IsNullOrWhiteSpace(Phone) ? null : Phone,
            Department, Designation, Location.Trim().ToUpperInvariant(), Region, EmploymentType, Status, MfaEnabled, LastLogin,
            AccountExpiry is null ? null : DateOnly.FromDateTime(AccountExpiry.Value), new Dictionary<string, string>(Attributes), Roles.Select(r => r.ToDto()).ToList());
    }

    public class ConditionEdit
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Attribute { get; set; } = "user.department";
        public ConditionOperator Operator { get; set; } = ConditionOperator.Eq;
        public string Value { get; set; } = "";
        public bool ValueIsAttribute { get; set; }
        public bool IsSchedule => Operator is ConditionOperator.InSchedule or ConditionOperator.NotInSchedule;
    }

    public class PolicyEdit
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid TenantId { get; set; }
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public PolicyEffect Effect { get; set; } = PolicyEffect.Restrict;
        public string App { get; set; } = "*";
        public string Module { get; set; } = "*";
        public string Resource { get; set; } = "*";
        public bool AllActions { get; set; } = true;
        public HashSet<AccessAction> Actions { get; set; } = new();
        public List<string> Roles { get; set; } = new();
        public ConditionMatch Match { get; set; } = ConditionMatch.All;
        public List<ConditionEdit> Conditions { get; set; } = new();
        public int Priority { get; set; } = 100;
        public bool IsActive { get; set; } = true;
        public string? CreatedBy { get; set; }
        public DateTime? CreatedAt { get; set; }

        public static PolicyEdit From(PolicyDto p) => new()
        {
            Id = p.Id, TenantId = p.TenantId, Name = p.Name, Description = p.Description, Effect = p.Effect, App = p.Target.App, Module = p.Target.Module,
            Resource = p.Target.Resource, AllActions = p.Target.Actions.Contains("*"),
            Actions = p.Target.Actions.Where(a => a != "*").Select(a => Enum.Parse<AccessAction>(a, true)).ToHashSet(),
            Roles = p.Target.Roles.ToList(), Match = p.Match, Priority = p.Priority, IsActive = p.IsActive, CreatedBy = p.CreatedBy, CreatedAt = p.CreatedAt,
            Conditions = p.Conditions.Select(c => new ConditionEdit { Id = c.Id, Attribute = c.Attribute, Operator = c.Operator, Value = c.Value, ValueIsAttribute = c.ValueIsAttribute }).ToList(),
        };

        public PolicyDto ToDto() => new(Id, TenantId, Name, Description, Effect,
            new PolicyTargetDto(App, Module, Resource, AllActions || Actions.Count == 0 ? new List<string> { "*" } : Actions.Select(a => a.ToString()).ToList(), Roles.ToList()),
            Match, Conditions.Select(c => new PolicyConditionDto(c.Id, c.Attribute, c.Operator, c.Value, c.ValueIsAttribute)).ToList(),
            Priority, IsActive, CreatedBy, CreatedAt, null);
    }

    public class WindowEdit
    {
        public HashSet<Weekday> Days { get; set; } = new() { Weekday.Mon, Weekday.Tue, Weekday.Wed, Weekday.Thu, Weekday.Fri };
        public TimeSpan? Start { get; set; } = new(9, 30, 0);
        public TimeSpan? End { get; set; } = new(18, 30, 0);
    }

    public class ScheduleEdit
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid TenantId { get; set; }
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public string Timezone { get; set; } = "Asia/Kolkata";
        public bool ExcludeHolidays { get; set; } = true;
        public List<WindowEdit> Windows { get; set; } = new() { new WindowEdit() };

        private static TimeSpan Parse(string hhmm) => TimeSpan.ParseExact(hhmm, @"hh\:mm", null);
        private static string Format(TimeSpan? t) => (t ?? TimeSpan.Zero).ToString(@"hh\:mm");

        public static ScheduleEdit From(ScheduleDto s) => new()
        {
            Id = s.Id, TenantId = s.TenantId, Code = s.Code, Name = s.Name, Timezone = s.Timezone, ExcludeHolidays = s.ExcludeHolidays,
            Windows = s.Windows.Select(w => new WindowEdit { Days = w.Days.ToHashSet(), Start = Parse(w.Start), End = Parse(w.End) }).ToList(),
        };

        public ScheduleDto ToDto() => new(Id, TenantId, Code, Name, Timezone, ExcludeHolidays,
            Windows.Select(w => new TimeWindowDto(Enum.GetValues<Weekday>().Where(w.Days.Contains).ToList(), Format(w.Start), Format(w.End))).ToList());
    }
}

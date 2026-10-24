using System.Text.Json.Serialization;

namespace DomainLayer.Enumerations
{
    /// <summary>Atomic operations that can be granted on a resource.</summary>
    public enum AccessAction
    {
        View = 1,
        Create = 2,
        Edit = 3,
        Delete = 4,
        Approve = 5,
        Export = 6,
        Print = 7,
        Void = 8,
        Refund = 9,
        // asset-lifecycle actions (used by the Asset Management System and any app that hands items to people)
        Checkout = 10,
        Checkin = 11,
        Audit = 12,
        Request = 13
    }

    public enum AppType
    {
        [JsonStringEnumMemberName("In-house")] InHouse = 1,
        [JsonStringEnumMemberName("Third-party")] ThirdParty = 2
    }

    public enum UserStatus
    {
        Active = 1,
        Inactive = 2,
        Locked = 3
    }

    public enum EmploymentType
    {
        Permanent = 1,
        Contract = 2,
        Vendor = 3,
        Intern = 4
    }

    /// <summary>
    /// Restrict: conditions MUST be true, otherwise access is denied (e.g. office hours only).
    /// Deny:     if conditions are true, access is denied (e.g. refund above limit).
    /// Grant:    if conditions are true, access is allowed even without an RBAC permission
    ///           (e.g. the approver named on a PO). Still subject to Restrict/Deny.
    /// </summary>
    public enum PolicyEffect
    {
        Restrict = 1,
        Deny = 2,
        Grant = 3
    }

    public enum ConditionMatch
    {
        [JsonStringEnumMemberName("all")] All = 1,
        [JsonStringEnumMemberName("any")] Any = 2
    }

    public enum ConditionOperator
    {
        [JsonStringEnumMemberName("eq")] Eq = 1,
        [JsonStringEnumMemberName("neq")] Neq = 2,
        [JsonStringEnumMemberName("in")] In = 3,
        [JsonStringEnumMemberName("notIn")] NotIn = 4,
        [JsonStringEnumMemberName("gt")] Gt = 5,
        [JsonStringEnumMemberName("gte")] Gte = 6,
        [JsonStringEnumMemberName("lt")] Lt = 7,
        [JsonStringEnumMemberName("lte")] Lte = 8,
        [JsonStringEnumMemberName("contains")] Contains = 9,
        [JsonStringEnumMemberName("startsWith")] StartsWith = 10,
        [JsonStringEnumMemberName("inSchedule")] InSchedule = 11,
        [JsonStringEnumMemberName("notInSchedule")] NotInSchedule = 12
    }

    public enum Weekday
    {
        Mon = 1,
        Tue = 2,
        Wed = 3,
        Thu = 4,
        Fri = 5,
        Sat = 6,
        Sun = 7
    }

    public enum AuditAction
    {
        Create = 1,
        Update = 2,
        Delete = 3,
        Assign = 4,
        Revoke = 5,
        Login = 6,
        Reset = 7
    }

    public enum AccessDecisionResult
    {
        Allow = 1,
        Deny = 2
    }

    public enum ExceptionSeverity
    {
        Warning = 1,
        Error = 2,
        Critical = 3
    }
}

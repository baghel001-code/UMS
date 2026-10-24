using DomainLayer.Entities;
using DomainLayer.Enumerations;
using System.Text.Json.Serialization;

namespace DomainLayer.Authorization
{
    /// <summary>When / where / how the request is made.</summary>
    /// <param name="DateTime">Local wall-clock time of the tenant (schedule time zone), e.g. 2026-10-23T11:00.</param>
    /// <param name="Network">Office LAN | VPN | Internet</param>
    /// <param name="Device">Managed | Unmanaged</param>
    /// <param name="Channel">Web | Mobile | POS Terminal | API</param>
    public sealed record AccessEnvironment(DateTime DateTime, string Ip, string Network, string Device, string Channel);

    /// <summary>Can <c>UserId</c> perform <c>Action</c> on <c>Resource</c> in <c>Module</c> of <c>App</c>, with these record attributes, in this environment?</summary>
    public sealed record AccessRequest(
        Guid UserId,
        string App,
        string Module,
        string Resource,
        AccessAction Action,
        IReadOnlyDictionary<string, string> ResourceAttributes,
        AccessEnvironment Env);

    public enum StepStatus
    {
        [JsonStringEnumMemberName("pass")] Pass = 1,
        [JsonStringEnumMemberName("fail")] Fail = 2,
        [JsonStringEnumMemberName("skip")] Skip = 3,
        [JsonStringEnumMemberName("info")] Info = 4
    }

    public sealed record DecisionStep(string Layer, StepStatus Status, string Detail);

    public sealed record AccessDecision(bool Allowed, string Reason, IReadOnlyList<DecisionStep> Steps);

    /// <summary>Everything the decision engine needs, already loaded from storage.</summary>
    public sealed record AccessContext(
        IReadOnlyList<Tenant> Tenants,
        IReadOnlyList<Application> Applications,
        IReadOnlyList<Role> Roles,
        IReadOnlyList<User> Users,
        IReadOnlyList<Policy> Policies,
        IReadOnlyList<Schedule> Schedules,
        IReadOnlyList<Holiday> Holidays);
}

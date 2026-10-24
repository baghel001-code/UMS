using DomainLayer.Authorization;
using DomainLayer.Entities;
using DomainLayer.Enumerations;
using DomainLayer.ValueObjects;

namespace DomainLayer.Services
{
    /// <summary>
    /// Policy Decision Point. Evaluates an access request layer by layer and explains every step:
    ///   Tenant → Identity → Application → Module → Resource.Action (RBAC) → Role scope → ABAC policies (deny-overrides).
    /// Used by POST /api/v1/authorize, the admin API's own permission filter and the admin portal's Access Simulator.
    /// </summary>
    public static class AccessDecisionService
    {
        private sealed record Grant(string Key, Role Role, RoleAssignment Assignment);

        public static IReadOnlyList<(Role Role, RoleAssignment Assignment)> ActiveAssignments(User user, IEnumerable<Role> roles, DateOnly day) =>
            user.RoleAssignments
                .Select(a => (Role: roles.FirstOrDefault(r => r.Id == a.RoleId), Assignment: a))
                .Where(x => x.Role is { IsActive: true } && x.Assignment.IsActiveOn(day))
                .Select(x => (x.Role!, x.Assignment))
                .ToList();

        public static Dictionary<string, string> BuildContext(User user, IReadOnlyList<Role> activeRoles, AccessRequest req, IEnumerable<Holiday> holidays)
        {
            var at = req.Env.DateTime;
            var ctx = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["user.id"] = user.Id.ToString(),
                ["user.empCode"] = user.EmpCode,
                ["user.department"] = user.Department,
                ["user.designation"] = user.Designation,
                ["user.location"] = user.Location,
                ["user.region"] = user.Region,
                ["user.employmentType"] = user.EmploymentType.ToString(),
                ["user.mfaEnabled"] = user.MfaEnabled ? "true" : "false",
                ["user.roles"] = string.Join(",", activeRoles.Select(r => r.Code)),
                ["resource.app"] = req.App,
                ["resource.module"] = req.Module,
                ["resource.type"] = req.Resource,
                ["action"] = req.Action.ToString(),
                ["env.date"] = at.ToString("yyyy-MM-dd"),
                ["env.time"] = at.ToString("HH:mm"),
                ["env.day"] = ScheduleEvaluator.WeekdayOf(at).ToString(),
                ["env.ip"] = req.Env.Ip,
                ["env.network"] = req.Env.Network,
                ["env.device"] = req.Env.Device,
                ["env.channel"] = req.Env.Channel,
                ["env.isHoliday"] = ScheduleEvaluator.FindHoliday(holidays, user.TenantId, DateOnly.FromDateTime(at), user.Region) != null ? "true" : "false",
            };
            foreach (var (k, v) in user.Attributes) ctx[$"user.{k}"] = v;
            foreach (var (k, v) in req.ResourceAttributes) ctx[$"resource.{k}"] = v;
            return ctx;
        }

        public static bool PolicyApplies(Policy p, string app, string module, string resource, AccessAction action, IReadOnlyCollection<string> roleCodes)
        {
            static bool Seg(string pattern, string value) => pattern == "*" || string.Equals(pattern, value, StringComparison.OrdinalIgnoreCase);
            var t = p.Target;
            if (!Seg(t.App, app) || !Seg(t.Module, module) || !Seg(t.Resource, resource)) return false;
            if (!t.Actions.Contains("*") && !t.Actions.Any(a => string.Equals(a, action.ToString(), StringComparison.OrdinalIgnoreCase))) return false;
            if (t.Roles.Count > 0 && !t.Roles.Any(roleCodes.Contains)) return false;
            return true;
        }

        private static (bool Result, List<string> Details) EvaluatePolicy(Policy p, IReadOnlyDictionary<string, string> ctx, DateTime at, AccessContext data)
        {
            var results = p.Conditions.Select(c => ConditionEvaluator.Evaluate(c, ctx, at, data.Schedules, data.Holidays)).ToList();
            var result = results.Count == 0 || (p.Match == ConditionMatch.All ? results.All(r => r.Ok) : results.Any(r => r.Ok));
            return (result, results.Select(r => $"{(r.Ok ? "✓" : "✗")} {r.Text}").ToList());
        }

        private static string? ScopeMismatch(RoleAssignment assignment, IReadOnlyDictionary<string, string> resourceAttrs)
        {
            foreach (var (k, v) in assignment.Scope)
            {
                if (resourceAttrs.TryGetValue(k, out var rv) &&
                    !v.Split(',').Any(x => string.Equals(x.Trim(), rv.Trim(), StringComparison.OrdinalIgnoreCase)))
                    return $"{k}: role scoped to '{v}', resource is '{rv}'";
            }
            return null;
        }

        public static AccessDecision Decide(AccessRequest req, AccessContext data)
        {
            var steps = new List<DecisionStep>();
            var at = req.Env.DateTime;
            var day = DateOnly.FromDateTime(at);
            AccessDecision Deny(string layer, string detail)
            {
                steps.Add(new DecisionStep(layer, StepStatus.Fail, detail));
                return new AccessDecision(false, $"{layer}: {detail}", steps);
            }

            // 1. identity & tenant
            var user = data.Users.FirstOrDefault(u => u.Id == req.UserId);
            if (user == null) return Deny("Identity", "user not found");
            var tenant = data.Tenants.FirstOrDefault(t => t.Id == user.TenantId);
            if (tenant == null || !tenant.IsActive) return Deny("Tenant", $"tenant {tenant?.Name ?? user.TenantId.ToString()} is inactive");
            steps.Add(new DecisionStep("Tenant", StepStatus.Pass, $"{tenant.Name} is active"));

            if (user.Status != UserStatus.Active) return Deny("Identity", $"account is {user.Status}");
            if (user.AccountExpiry is { } expiry && day > expiry) return Deny("Identity", $"account expired on {expiry:yyyy-MM-dd}");
            steps.Add(new DecisionStep("Identity", StepStatus.Pass, $"{user.Name} ({user.EmpCode}) is active"));

            // 2. active roles
            var tenantRoles = data.Roles.Where(r => r.TenantId == user.TenantId).ToList();
            var active = ActiveAssignments(user, tenantRoles, day);
            var ignored = user.RoleAssignments.Count - active.Count;
            steps.Add(new DecisionStep("Roles", active.Count > 0 ? StepStatus.Info : StepStatus.Fail,
                active.Count > 0
                    ? $"active roles: {string.Join(", ", active.Select(a => a.Role.Code))}{(ignored > 0 ? $" ({ignored} expired/inactive ignored)" : "")}"
                    : "no active role assignments"));
            var roleCodes = active.Select(a => a.Role.Code).ToList();

            var ctx = BuildContext(user, active.Select(a => a.Role).ToList(), req, data.Holidays);
            var policies = data.Policies
                .Where(p => p.IsActive && p.TenantId == user.TenantId && PolicyApplies(p, req.App, req.Module, req.Resource, req.Action, roleCodes))
                .OrderBy(p => p.Priority)
                .ToList();

            // Grant policies can stand in for a missing RBAC permission
            Policy? grantedBy = null;
            Policy? FindGrant()
            {
                if (grantedBy != null) return grantedBy;
                foreach (var p in policies.Where(x => x.Effect == PolicyEffect.Grant))
                    if (EvaluatePolicy(p, ctx, at, data).Result) return grantedBy = p;
                return null;
            }
            static string Via(IEnumerable<Grant> g) => string.Join(", ", g.Select(x => x.Role.Code).Distinct());

            // 3. application
            var app = data.Applications.FirstOrDefault(a => string.Equals(a.Code, req.App, StringComparison.OrdinalIgnoreCase));
            if (app == null) return Deny("Application", $"application {req.App} not registered");
            if (!app.IsActive) return Deny("Application", $"{app.Name} is disabled");
            if (!app.IsEnabledFor(user.TenantId)) return Deny("Application", $"{app.Name} is not enabled for {tenant.Name}");

            var perms = active.SelectMany(a => a.Role.Permissions.Select(k => new Grant(k, a.Role, a.Assignment))).ToList();
            var appPerms = perms.Where(p => PermissionKey.Matches(p.Key, PermissionKey.Of(app.Code, "*", "*", "*"))).ToList();
            if (appPerms.Count == 0)
            {
                var g = FindGrant();
                if (g == null) return Deny("Application", $"no role grants access to {app.Name}");
                steps.Add(new DecisionStep("Application", StepStatus.Pass, $"{app.Name} via grant policy '{g.Name}'"));
            }
            else steps.Add(new DecisionStep("Application", StepStatus.Pass, $"{app.Name} via {Via(appPerms)}"));

            // 4. module
            var mod = app.Modules.FirstOrDefault(m => string.Equals(m.Code, req.Module, StringComparison.OrdinalIgnoreCase));
            if (mod == null) return Deny("Module", $"module {req.Module} not found in {app.Code}");
            var modPerms = appPerms.Where(p => PermissionKey.Matches(p.Key, PermissionKey.Of(app.Code, mod.Code, "*", "*"))).ToList();
            if (modPerms.Count == 0)
            {
                var g = FindGrant();
                if (g == null) return Deny("Module", $"no permission on module {mod.Name}");
                steps.Add(new DecisionStep("Module", StepStatus.Pass, $"{mod.Name} via grant policy '{g.Name}'"));
            }
            else steps.Add(new DecisionStep("Module", StepStatus.Pass, $"{mod.Name} via {Via(modPerms)}"));

            // 5. resource + action
            var res = mod.Resources.FirstOrDefault(r => string.Equals(r.Code, req.Resource, StringComparison.OrdinalIgnoreCase));
            if (res == null) return Deny("Resource", $"resource {req.Resource} not found in {mod.Code}");
            if (!res.Actions.Contains(req.Action)) return Deny("Action", $"{req.Action} is not a valid action on {res.Name}");
            var key = PermissionKey.Of(app.Code, mod.Code, res.Code, req.Action.ToString());
            var actionPerms = modPerms.Where(p => PermissionKey.Matches(p.Key, key)).ToList();
            var rbacGranted = false;
            if (actionPerms.Count == 0)
            {
                var g = FindGrant();
                if (g == null) return Deny("Permission", $"missing {key}");
                steps.Add(new DecisionStep("Permission", StepStatus.Pass, $"{key} granted by policy '{g.Name}'"));
            }
            else
            {
                rbacGranted = true;
                steps.Add(new DecisionStep("Permission", StepStatus.Pass,
                    $"{key} matched {string.Join(", ", actionPerms.Select(p => $"{p.Key} ({p.Role.Code})"))}"));
            }

            // 6. role scope (resource-based access): at least one granting assignment must be in scope
            if (rbacGranted)
            {
                var scoped = actionPerms.Where(p => p.Assignment.Scope.Count > 0).ToList();
                if (scoped.Count > 0)
                {
                    var inScope = actionPerms.FirstOrDefault(p => ScopeMismatch(p.Assignment, req.ResourceAttributes) == null);
                    if (inScope == null)
                    {
                        var g = FindGrant();
                        if (g == null) return Deny("Scope", ScopeMismatch(scoped[0].Assignment, req.ResourceAttributes)!);
                        steps.Add(new DecisionStep("Scope", StepStatus.Pass, $"out of role scope, allowed by grant policy '{g.Name}'"));
                    }
                    else
                    {
                        var s = string.Join(", ", inScope.Assignment.Scope.Select(kv => $"{kv.Key}={kv.Value}"));
                        steps.Add(new DecisionStep("Scope", StepStatus.Pass, s.Length > 0 ? $"resource within role scope ({s})" : "unscoped role assignment"));
                    }
                }
                else steps.Add(new DecisionStep("Scope", StepStatus.Skip, "role assignment has no scope restriction"));
            }

            // 7. ABAC policies: deny-overrides
            var enforce = policies.Where(p => p.Effect != PolicyEffect.Grant).ToList();
            if (enforce.Count == 0) steps.Add(new DecisionStep("Policy", StepStatus.Skip, "no Restrict/Deny policy applies"));
            foreach (var p in enforce)
            {
                var (result, details) = EvaluatePolicy(p, ctx, at, data);
                var violated = p.Effect == PolicyEffect.Restrict ? !result : result;
                var head = $"{p.Effect} '{p.Name}' — {(violated ? (p.Effect == PolicyEffect.Restrict ? "condition not met" : "deny rule matched") : "ok")}";
                if (violated) return Deny("Policy", $"{head}; {string.Join("; ", details)}");
                steps.Add(new DecisionStep("Policy", StepStatus.Pass, $"{head}; {string.Join("; ", details)}"));
            }

            steps.Add(new DecisionStep("Decision", StepStatus.Pass, "PERMIT"));
            return new AccessDecision(true, $"{key} permitted", steps);
        }

        /// <summary>Every concrete APP.MODULE.RESOURCE.ACTION the user's active roles grant today (RBAC only, before policies).</summary>
        public static IReadOnlyList<(string Key, IReadOnlyList<string> ViaRoles)> EffectivePermissions(User user, IEnumerable<Role> roles,
            IEnumerable<Application> applications, DateOnly day)
        {
            var active = ActiveAssignments(user, roles, day);
            var result = new List<(string, IReadOnlyList<string>)>();
            foreach (var app in applications)
                foreach (var m in app.Modules)
                    foreach (var r in m.Resources)
                        foreach (var a in r.Actions)
                        {
                            var key = PermissionKey.Of(app.Code, m.Code, r.Code, a.ToString());
                            var via = active.Where(x => x.Role.Grants(key)).Select(x => x.Role.Code).ToList();
                            if (via.Count > 0) result.Add((key, via));
                        }
            return result;
        }
    }
}

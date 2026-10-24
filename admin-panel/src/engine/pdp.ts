// Policy Decision Point (PDP).
// Evaluates an access request layer by layer:
//   Tenant -> Identity -> Application -> Module -> Resource.Action (RBAC) -> Role scope -> ABAC policies
// The same algorithm is intended to run server-side (IAuthorizationService) — see docs/ARCHITECTURE.md.

import type {
  AccessRequest,
  Application,
  Condition,
  Decision,
  DecisionStep,
  Holiday,
  Policy,
  Role,
  RoleAssignment,
  Schedule,
  Tenant,
  User,
  Weekday,
} from '../types';
import { WEEKDAYS } from '../types';

export interface PdpData {
  tenants: Tenant[];
  applications: Application[];
  roles: Role[];
  users: User[];
  policies: Policy[];
  schedules: Schedule[];
  holidays: Holiday[];
}

// ---------------------------------------------------------------- permissions

export function permissionKey(app: string, module: string, resource: string, action: string): string {
  return `${app}.${module}.${resource}.${action}`.toUpperCase();
}

/** True when a granted key (which may contain `*` segments) covers the requested key. */
export function permissionMatches(granted: string, requested: string): boolean {
  const g = granted.toUpperCase().split('.');
  const r = requested.toUpperCase().split('.');
  if (g.length !== 4 || r.length !== 4) return false;
  return g.every((seg, i) => seg === '*' || r[i] === '*' || seg === r[i]);
}

/** Stricter than permissionMatches: a `*` in the requested key is only covered by a `*` in the grant. */
export function permissionCovers(granted: string, requested: string): boolean {
  const g = granted.toUpperCase().split('.');
  const r = requested.toUpperCase().split('.');
  if (g.length !== 4 || r.length !== 4) return false;
  return g.every((seg, i) => seg === '*' || seg === r[i]);
}

function segMatch(pattern: string, value: string): boolean {
  return pattern === '*' || pattern.toUpperCase() === value.toUpperCase();
}

// ---------------------------------------------------------------- dates & schedules

export function isAssignmentActive(a: RoleAssignment, at: Date): boolean {
  const day = toDateStr(at);
  if (a.validFrom && day < a.validFrom) return false;
  if (a.validTo && day > a.validTo) return false;
  return true;
}

export function toDateStr(d: Date): string {
  const p = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`;
}

function toTimeStr(d: Date): string {
  const p = (n: number) => String(n).padStart(2, '0');
  return `${p(d.getHours())}:${p(d.getMinutes())}`;
}

function weekdayOf(d: Date): Weekday {
  // JS: 0 = Sunday
  return WEEKDAYS[(d.getDay() + 6) % 7];
}

export function isHoliday(holidays: Holiday[], tenantId: string, date: string, region?: string): Holiday | undefined {
  return holidays.find(
    (h) => h.tenantId === tenantId && h.date === date && (h.regions.length === 0 || (region !== undefined && h.regions.includes(region))),
  );
}

/** Whether `at` falls inside one of the schedule's windows. Handles overnight windows (e.g. 22:00-06:00). */
export function isInSchedule(schedule: Schedule, at: Date, holidays: Holiday[], region?: string): boolean {
  if (schedule.excludeHolidays && isHoliday(holidays, schedule.tenantId, toDateStr(at), region)) return false;
  const time = toTimeStr(at);
  const today = weekdayOf(at);
  const yesterday = WEEKDAYS[(WEEKDAYS.indexOf(today) + 6) % 7];
  return schedule.windows.some((w) => {
    if (w.start <= w.end) return w.days.includes(today) && time >= w.start && time < w.end;
    // overnight: the part after start belongs to the start day, the part before end to the next day
    return (w.days.includes(today) && time >= w.start) || (w.days.includes(yesterday) && time < w.end);
  });
}

// ---------------------------------------------------------------- attribute context

export type AttrContext = Record<string, string>;

export function buildContext(user: User, roles: Role[], req: AccessRequest, holidays: Holiday[]): AttrContext {
  const at = new Date(req.env.dateTime);
  const ctx: AttrContext = {
    'user.id': user.id,
    'user.empCode': user.empCode,
    'user.department': user.department,
    'user.designation': user.designation,
    'user.location': user.location,
    'user.region': user.region,
    'user.employmentType': user.employmentType,
    'user.mfaEnabled': String(user.mfaEnabled),
    'user.roles': roles.map((r) => r.code).join(','),
    'resource.app': req.app,
    'resource.module': req.module,
    'resource.type': req.resource,
    'action': req.action,
    'env.date': toDateStr(at),
    'env.time': toTimeStr(at),
    'env.day': weekdayOf(at),
    'env.ip': req.env.ip,
    'env.network': req.env.network,
    'env.device': req.env.device,
    'env.channel': req.env.channel,
    'env.isHoliday': String(Boolean(isHoliday(holidays, user.tenantId, toDateStr(at), user.region))),
  };
  for (const [k, v] of Object.entries(user.attributes)) ctx[`user.${k}`] = v;
  for (const [k, v] of Object.entries(req.resourceAttributes)) ctx[`resource.${k}`] = v;
  return ctx;
}

// ---------------------------------------------------------------- conditions

export const OPERATOR_LABELS: Record<Condition['operator'], string> = {
  eq: '==',
  neq: '!=',
  in: 'in',
  notIn: 'not in',
  gt: '>',
  gte: '>=',
  lt: '<',
  lte: '<=',
  contains: 'contains',
  startsWith: 'starts with',
  inSchedule: 'within schedule',
  notInSchedule: 'outside schedule',
};

function compare(a: string, b: string): number {
  const na = Number(a);
  const nb = Number(b);
  if (a.trim() !== '' && b.trim() !== '' && !Number.isNaN(na) && !Number.isNaN(nb)) return na - nb;
  return a.localeCompare(b, undefined, { sensitivity: 'base' });
}

const eqi = (a: string, b: string) => a.trim().toLowerCase() === b.trim().toLowerCase();

export interface ConditionResult {
  ok: boolean;
  text: string;
}

export function evaluateCondition(
  c: Condition,
  ctx: AttrContext,
  at: Date,
  schedules: Schedule[],
  holidays: Holiday[],
): ConditionResult {
  const left = ctx[c.attribute];
  const right = c.valueIsAttribute ? ctx[c.value] : c.value;
  const shownRight = c.valueIsAttribute ? `${c.value} (${right ?? 'missing'})` : `'${c.value}'`;
  const text = `${c.attribute} (${left ?? 'missing'}) ${OPERATOR_LABELS[c.operator]} ${shownRight}`;

  if (c.operator === 'inSchedule' || c.operator === 'notInSchedule') {
    const s = schedules.find((x) => x.id === c.value || x.code === c.value);
    if (!s) return { ok: false, text: `schedule '${c.value}' not found` };
    const inside = isInSchedule(s, at, holidays, ctx['user.region']);
    const ok = c.operator === 'inSchedule' ? inside : !inside;
    return { ok, text: `${ctx['env.day']} ${ctx['env.time']} ${inside ? 'is within' : 'is outside'} ${s.name}` };
  }
  // missing attributes never satisfy a condition (fail closed)
  if (left === undefined || right === undefined) return { ok: false, text };

  const list = right.split(',').map((s) => s.trim());
  let ok: boolean;
  switch (c.operator) {
    case 'eq':
      ok = eqi(left, right);
      break;
    case 'neq':
      ok = !eqi(left, right);
      break;
    case 'in':
      ok = left.split(',').some((l) => list.some((x) => eqi(l, x)));
      break;
    case 'notIn':
      ok = !left.split(',').some((l) => list.some((x) => eqi(l, x)));
      break;
    case 'gt':
      ok = compare(left, right) > 0;
      break;
    case 'gte':
      ok = compare(left, right) >= 0;
      break;
    case 'lt':
      ok = compare(left, right) < 0;
      break;
    case 'lte':
      ok = compare(left, right) <= 0;
      break;
    case 'contains':
      ok = left.toLowerCase().includes(right.toLowerCase());
      break;
    case 'startsWith':
      ok = left.toLowerCase().startsWith(right.toLowerCase());
      break;
    default:
      ok = false;
  }
  return { ok, text };
}

/** Human readable rule expression, as stored in Policies.RuleExpression on the server. */
export function policyExpression(p: Pick<Policy, 'conditions' | 'match'>, schedules: Schedule[] = []): string {
  if (p.conditions.length === 0) return 'true';
  const parts = p.conditions.map((c) => {
    if (c.operator === 'inSchedule' || c.operator === 'notInSchedule') {
      const s = schedules.find((x) => x.id === c.value);
      const fn = `InSchedule(env.now, '${s?.code ?? c.value}')`;
      return c.operator === 'inSchedule' ? fn : `!${fn}`;
    }
    const v = c.valueIsAttribute
      ? c.value
      : c.operator === 'in' || c.operator === 'notIn'
        ? `[${c.value
            .split(',')
            .map((x) => `'${x.trim()}'`)
            .join(', ')}]`
        : Number.isNaN(Number(c.value)) || c.value.trim() === ''
          ? `'${c.value}'`
          : c.value;
    return `${c.attribute} ${OPERATOR_LABELS[c.operator]} ${v}`;
  });
  return parts.join(p.match === 'all' ? ' && ' : ' || ');
}

export function policyApplies(p: Policy, req: Pick<AccessRequest, 'app' | 'module' | 'resource' | 'action'>, roleCodes: string[]): boolean {
  const t = p.target;
  if (!segMatch(t.app, req.app) || !segMatch(t.module, req.module) || !segMatch(t.resource, req.resource)) return false;
  const actions = t.actions as string[];
  if (!actions.includes('*') && !actions.includes(req.action)) return false;
  if (t.roles.length > 0 && !t.roles.some((r) => roleCodes.includes(r))) return false;
  return true;
}

function evaluatePolicy(p: Policy, ctx: AttrContext, at: Date, data: PdpData): { result: boolean; details: string[] } {
  const results = p.conditions.map((c) => evaluateCondition(c, ctx, at, data.schedules, data.holidays));
  const result =
    results.length === 0 ? true : p.match === 'all' ? results.every((r) => r.ok) : results.some((r) => r.ok);
  return { result, details: results.map((r) => `${r.ok ? '✓' : '✗'} ${r.text}`) };
}

// ---------------------------------------------------------------- effective permissions

export interface EffectivePermission {
  key: string;
  role: Role;
  assignment: RoleAssignment;
}

export function activeAssignments(user: User, roles: Role[], at: Date): { role: Role; assignment: RoleAssignment }[] {
  return user.roles
    .map((a) => ({ assignment: a, role: roles.find((r) => r.id === a.roleId) }))
    .filter((x): x is { role: Role; assignment: RoleAssignment } => !!x.role && x.role.isActive && isAssignmentActive(x.assignment, at));
}

export function effectivePermissions(user: User, roles: Role[], at: Date): EffectivePermission[] {
  return activeAssignments(user, roles, at).flatMap(({ role, assignment }) =>
    role.permissions.map((key) => ({ key, role, assignment })),
  );
}

function scopeMismatch(assignment: RoleAssignment, resourceAttrs: Record<string, string>): string | null {
  for (const [k, v] of Object.entries(assignment.scope)) {
    const rv = resourceAttrs[k];
    if (rv !== undefined && !v.split(',').some((x) => eqi(x, rv))) return `${k}: role scoped to '${v}', resource is '${rv}'`;
  }
  return null;
}

// ---------------------------------------------------------------- decision

export function decide(req: AccessRequest, data: PdpData): Decision {
  const steps: DecisionStep[] = [];
  const at = new Date(req.env.dateTime);
  const deny = (layer: string, detail: string): Decision => {
    steps.push({ layer, status: 'fail', detail });
    return { allowed: false, reason: `${layer}: ${detail}`, steps };
  };

  // 1. identity & tenant
  const user = data.users.find((u) => u.id === req.userId);
  if (!user) return deny('Identity', 'user not found');
  const tenant = data.tenants.find((t) => t.id === user.tenantId);
  if (!tenant || !tenant.isActive) return deny('Tenant', `tenant ${tenant?.name ?? user.tenantId} is inactive`);
  steps.push({ layer: 'Tenant', status: 'pass', detail: `${tenant.name} is active` });

  if (user.status !== 'Active') return deny('Identity', `account is ${user.status}`);
  if (user.accountExpiry && toDateStr(at) > user.accountExpiry) return deny('Identity', `account expired on ${user.accountExpiry}`);
  steps.push({ layer: 'Identity', status: 'pass', detail: `${user.fullName} (${user.empCode}) is active` });

  // 2. active roles
  const tenantRoles = data.roles.filter((r) => r.tenantId === user.tenantId);
  const active = activeAssignments(user, tenantRoles, at);
  const expired = user.roles.length - active.length;
  steps.push({
    layer: 'Roles',
    status: active.length ? 'info' : 'fail',
    detail: active.length
      ? `active roles: ${active.map((a) => a.role.code).join(', ')}${expired ? ` (${expired} expired/inactive ignored)` : ''}`
      : 'no active role assignments',
  });
  const roleCodes = active.map((a) => a.role.code);

  const ctx = buildContext(user, active.map((a) => a.role), req, data.holidays);
  const policies = data.policies
    .filter((p) => p.isActive && p.tenantId === user.tenantId && policyApplies(p, req, roleCodes))
    .sort((a, b) => a.priority - b.priority);

  // Grant policies can stand in for a missing RBAC permission
  let grantedBy: Policy | null = null;
  const findGrant = (): Policy | null => {
    if (grantedBy) return grantedBy;
    for (const p of policies.filter((x) => x.effect === 'Grant')) {
      if (evaluatePolicy(p, ctx, at, data).result) return (grantedBy = p);
    }
    return null;
  };

  // 3. application
  const app = data.applications.find((a) => a.code.toUpperCase() === req.app.toUpperCase());
  if (!app) return deny('Application', `application ${req.app} not registered`);
  if (!app.isActive) return deny('Application', `${app.name} is disabled`);
  if (!app.tenantIds.includes(user.tenantId)) return deny('Application', `${app.name} is not enabled for ${tenant.name}`);

  const perms = active.flatMap(({ role, assignment }) => role.permissions.map((key) => ({ key, role, assignment })));
  const appPerms = perms.filter((p) => permissionMatches(p.key, permissionKey(app.code, '*', '*', '*')));
  if (appPerms.length === 0) {
    const g = findGrant();
    if (!g) return deny('Application', `no role grants access to ${app.name}`);
    steps.push({ layer: 'Application', status: 'pass', detail: `${app.name} via grant policy '${g.name}'` });
  } else {
    steps.push({ layer: 'Application', status: 'pass', detail: `${app.name} via ${[...new Set(appPerms.map((p) => p.role.code))].join(', ')}` });
  }

  // 4. module
  const mod = app.modules.find((m) => m.code.toUpperCase() === req.module.toUpperCase());
  if (!mod) return deny('Module', `module ${req.module} not found in ${app.code}`);
  const modPerms = appPerms.filter((p) => permissionMatches(p.key, permissionKey(app.code, mod.code, '*', '*')));
  if (modPerms.length === 0) {
    const g = findGrant();
    if (!g) return deny('Module', `no permission on module ${mod.name}`);
    steps.push({ layer: 'Module', status: 'pass', detail: `${mod.name} via grant policy '${g.name}'` });
  } else {
    steps.push({ layer: 'Module', status: 'pass', detail: `${mod.name} via ${[...new Set(modPerms.map((p) => p.role.code))].join(', ')}` });
  }

  // 5. resource + action
  const res = mod.resources.find((r) => r.code.toUpperCase() === req.resource.toUpperCase());
  if (!res) return deny('Resource', `resource ${req.resource} not found in ${mod.code}`);
  if (!res.actions.includes(req.action)) return deny('Action', `${req.action} is not a valid action on ${res.name}`);
  const key = permissionKey(app.code, mod.code, res.code, req.action);
  const actionPerms = modPerms.filter((p) => permissionMatches(p.key, key));
  let rbacGranted = false;
  if (actionPerms.length === 0) {
    const g = findGrant();
    if (!g) return deny('Permission', `missing ${key}`);
    steps.push({ layer: 'Permission', status: 'pass', detail: `${key} granted by policy '${g.name}'` });
  } else {
    rbacGranted = true;
    steps.push({
      layer: 'Permission',
      status: 'pass',
      detail: `${key} matched ${actionPerms.map((p) => `${p.key} (${p.role.code})`).join(', ')}`,
    });
  }

  // 6. role scope (resource-based access): at least one granting assignment must be in scope
  if (rbacGranted) {
    const scoped = actionPerms.filter((p) => Object.keys(p.assignment.scope).length > 0);
    if (scoped.length > 0) {
      const inScope = actionPerms.find((p) => scopeMismatch(p.assignment, req.resourceAttributes) === null);
      if (!inScope) {
        const g = findGrant();
        if (!g) return deny('Scope', scopeMismatch(scoped[0].assignment, req.resourceAttributes)!);
        steps.push({ layer: 'Scope', status: 'pass', detail: `out of role scope, allowed by grant policy '${g.name}'` });
      } else {
        const s = Object.entries(inScope.assignment.scope).map(([k, v]) => `${k}=${v}`).join(', ');
        steps.push({ layer: 'Scope', status: 'pass', detail: s ? `resource within role scope (${s})` : 'unscoped role assignment' });
      }
    } else {
      steps.push({ layer: 'Scope', status: 'skip', detail: 'role assignment has no scope restriction' });
    }
  }

  // 7. ABAC policies: deny-overrides
  const enforce = policies.filter((p) => p.effect !== 'Grant');
  if (enforce.length === 0) steps.push({ layer: 'Policy', status: 'skip', detail: 'no Restrict/Deny policy applies' });
  for (const p of enforce) {
    const { result, details } = evaluatePolicy(p, ctx, at, data);
    const violated = p.effect === 'Restrict' ? !result : result;
    const head = `${p.effect} '${p.name}' — ${violated ? (p.effect === 'Restrict' ? 'condition not met' : 'deny rule matched') : 'ok'}`;
    if (violated) return deny('Policy', `${head}; ${details.join('; ')}`);
    steps.push({ layer: 'Policy', status: 'pass', detail: `${head}; ${details.join('; ')}` });
  }

  steps.push({ layer: 'Decision', status: 'pass', detail: 'PERMIT' });
  return { allowed: true, reason: `${key} permitted`, steps };
}

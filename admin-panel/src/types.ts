// Domain model for the Vishal Mega Mart User Management System (UMS).
// Mirrors the backend tables described in docs/ARCHITECTURE.md.

export type Id = string;

export interface Tenant {
  id: Id;
  code: string;
  name: string;
  isActive: boolean;
  createdOn: string;
}

export type AppType = 'In-house' | 'Third-party';

/** Atomic operations an admin can grant on a resource. */
export const ALL_ACTIONS = [
  'View',
  'Create',
  'Edit',
  'Delete',
  'Approve',
  'Export',
  'Print',
  'Void',
  'Refund',
] as const;
export type Action = (typeof ALL_ACTIONS)[number];

export interface Resource {
  id: Id;
  code: string; // e.g. INVOICE
  name: string;
  actions: Action[]; // actions that make sense for this resource
}

export interface AppModule {
  id: Id;
  code: string; // e.g. BILLING
  name: string;
  resources: Resource[];
}

export interface Application {
  id: Id;
  code: string; // e.g. POS
  name: string;
  type: AppType;
  vendor: string;
  ownerDept: string;
  description: string;
  isActive: boolean;
  tenantIds: Id[]; // tenants that have this application enabled
  modules: AppModule[];
}

/**
 * A permission key is `APP.MODULE.RESOURCE.ACTION`. Any segment may be `*`.
 * e.g. `POS.BILLING.INVOICE.REFUND`, `HRMS.*.*.VIEW`.
 */
export type PermissionKey = string;

export interface Role {
  id: Id;
  tenantId: Id;
  code: string;
  name: string;
  description: string;
  /** Application this role belongs to, or null for a cross-application role. */
  appId: Id | null;
  permissions: PermissionKey[];
  isSystem: boolean;
  isActive: boolean;
}

export interface RoleAssignment {
  roleId: Id;
  /** Optional scope; when a resource carries the same attribute, the values must match. */
  scope: Record<string, string>;
  validFrom: string | null; // ISO date
  validTo: string | null; // ISO date
  assignedBy: string;
  assignedOn: string;
}

export type UserStatus = 'Active' | 'Inactive' | 'Locked';
export type EmploymentType = 'Permanent' | 'Contract' | 'Vendor' | 'Intern';

export interface User {
  id: Id;
  tenantId: Id;
  empCode: string;
  fullName: string;
  email: string;
  phone: string;
  department: string;
  designation: string;
  location: string; // store / DC / office code
  region: string;
  employmentType: EmploymentType;
  status: UserStatus;
  mfaEnabled: boolean;
  lastLogin: string | null;
  accountExpiry: string | null;
  /** Free-form ABAC attributes (UserAttributes table). */
  attributes: Record<string, string>;
  roles: RoleAssignment[];
}

// ---------- ABAC / Policies ----------

export type Operator =
  | 'eq'
  | 'neq'
  | 'in'
  | 'notIn'
  | 'gt'
  | 'gte'
  | 'lt'
  | 'lte'
  | 'contains'
  | 'startsWith'
  | 'inSchedule'
  | 'notInSchedule';

export interface Condition {
  id: Id;
  /** Left side attribute path: user.X | resource.X | env.X */
  attribute: string;
  operator: Operator;
  /** Literal value, comma separated list for in/notIn, schedule id for inSchedule, or attribute path. */
  value: string;
  valueIsAttribute: boolean;
}

/**
 * Restrict: conditions MUST be true, otherwise access is denied (e.g. office hours only).
 * Deny:     if conditions are true, access is denied (e.g. refund above limit).
 * Grant:    if conditions are true, access is allowed even without an RBAC permission
 *           (e.g. employees can view their own payslip). Still subject to Restrict/Deny.
 */
export type PolicyEffect = 'Restrict' | 'Deny' | 'Grant';

export interface PolicyTarget {
  app: string; // app code or *
  module: string; // module code or *
  resource: string; // resource code or *
  actions: Action[] | ['*'];
  /** Only applies to users holding one of these role codes (empty = all users). */
  roles: string[];
}

export interface Policy {
  id: Id;
  tenantId: Id;
  name: string;
  description: string;
  effect: PolicyEffect;
  target: PolicyTarget;
  match: 'all' | 'any';
  conditions: Condition[];
  priority: number;
  isActive: boolean;
  createdBy: string;
  createdAt: string;
}

// ---------- Schedules (office hours) ----------

export type Weekday = 'Mon' | 'Tue' | 'Wed' | 'Thu' | 'Fri' | 'Sat' | 'Sun';
export const WEEKDAYS: Weekday[] = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];

export interface TimeWindow {
  days: Weekday[];
  start: string; // HH:mm
  end: string; // HH:mm (may be < start for overnight shifts)
}

export interface Schedule {
  id: Id;
  tenantId: Id;
  code: string;
  name: string;
  timezone: string;
  windows: TimeWindow[];
  excludeHolidays: boolean;
}

export interface Holiday {
  id: Id;
  tenantId: Id;
  date: string; // yyyy-mm-dd
  name: string;
  regions: string[]; // empty = all regions
}

// ---------- Logs ----------

export interface AuditLog {
  id: Id;
  tenantId: Id;
  timestamp: string;
  actor: string;
  entity: string;
  entityId: string;
  action: 'Create' | 'Update' | 'Delete' | 'Assign' | 'Revoke' | 'Login' | 'Reset';
  summary: string;
  oldValues: unknown;
  newValues: unknown;
  ip: string;
  device: string;
}

export interface UserActionLog {
  id: Id;
  tenantId: Id;
  timestamp: string;
  userId: Id;
  userName: string;
  app: string;
  route: string;
  action: string;
  decision: 'Allow' | 'Deny';
  reason: string;
  ip: string;
}

export interface ExceptionLog {
  id: Id;
  tenantId: Id;
  timestamp: string;
  app: string;
  severity: 'Error' | 'Critical' | 'Warning';
  message: string;
  stackTrace: string;
  userName: string | null;
  requestPath: string;
  ip: string;
  resolved: boolean;
}

export interface SecuritySettings {
  passwordMinLength: number;
  passwordExpiryDays: number;
  maxFailedLogins: number;
  lockoutMinutes: number;
  sessionTimeoutMinutes: number;
  mfaRequiredForAdmins: boolean;
  mfaRequiredOutsideOffice: boolean;
  jwtExpiryMinutes: number;
  refreshTokenDays: number;
  allowedIpRanges: string;
}

// ---------- Access check (Policy Decision Point) ----------

export interface Environment {
  dateTime: string; // local ISO 'yyyy-mm-ddTHH:mm'
  ip: string;
  network: 'Office LAN' | 'VPN' | 'Internet';
  device: 'Managed' | 'Unmanaged';
  channel: 'Web' | 'Mobile' | 'POS Terminal' | 'API';
}

export interface AccessRequest {
  userId: Id;
  app: string;
  module: string;
  resource: string;
  action: Action;
  resourceAttributes: Record<string, string>;
  env: Environment;
}

export type StepStatus = 'pass' | 'fail' | 'skip' | 'info';

export interface DecisionStep {
  layer: string;
  status: StepStatus;
  detail: string;
}

export interface Decision {
  allowed: boolean;
  reason: string;
  steps: DecisionStep[];
}

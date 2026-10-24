// Demo data for the admin panel. In production this comes from the UMS API.
import type {
  Action,
  AppModule,
  AppType,
  Application,
  AuditLog,
  EmploymentType,
  ExceptionLog,
  Holiday,
  Policy,
  Role,
  RoleAssignment,
  Schedule,
  SecuritySettings,
  Tenant,
  User,
  UserActionLog,
} from '../types';

export interface UmsState {
  tenants: Tenant[];
  applications: Application[];
  roles: Role[];
  users: User[];
  policies: Policy[];
  schedules: Schedule[];
  holidays: Holiday[];
  auditLogs: AuditLog[];
  actionLogs: UserActionLog[];
  exceptionLogs: ExceptionLog[];
  settings: Record<string, SecuritySettings>;
}

// deterministic PRNG so the demo looks the same on every load
function mulberry32(seed: number) {
  return () => {
    seed |= 0;
    seed = (seed + 0x6d2b79f5) | 0;
    let t = Math.imul(seed ^ (seed >>> 15), 1 | seed);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}
const rnd = mulberry32(20261024);
const pick = <T,>(arr: readonly T[]): T => arr[Math.floor(rnd() * arr.length)];

export const T_VMM = 't-vmm';
export const T_VENDOR = 't-vendor';
export const T_UAT = 't-uat';

const tenants: Tenant[] = [
  { id: T_VMM, code: 'VMM', name: 'Vishal Mega Mart Ltd', isActive: true, createdOn: '2025-04-01' },
  { id: T_VENDOR, code: 'VMM-PARTNERS', name: 'Vendor & Partner Network', isActive: true, createdOn: '2025-06-15' },
  { id: T_UAT, code: 'VMM-UAT', name: 'UAT / Sandbox', isActive: true, createdOn: '2025-05-10' },
];

// ---------------------------------------------------------------- applications

type ResDef = [code: string, name: string, actions: Action[]];
type ModDef = [code: string, name: string, resources: ResDef[]];

let idSeq = 0;
const nid = (p: string) => `${p}-${(++idSeq).toString(36)}`;

function app(
  code: string,
  name: string,
  type: AppType,
  vendor: string,
  ownerDept: string,
  description: string,
  tenantIds: string[],
  modules: ModDef[],
): Application {
  return {
    id: `app-${code.toLowerCase()}`,
    code,
    name,
    type,
    vendor,
    ownerDept,
    description,
    isActive: true,
    tenantIds,
    modules: modules.map(
      ([mc, mn, res]): AppModule => ({
        id: nid('mod'),
        code: mc,
        name: mn,
        resources: res.map(([rc, rn, actions]) => ({ id: nid('res'), code: rc, name: rn, actions })),
      }),
    ),
  };
}

const ALL_T = [T_VMM, T_UAT];

const applications: Application[] = [
  app('UMS', 'User Management System', 'In-house', 'VMM IT', 'IT', 'Central identity, roles and access policies for all applications.', ALL_T, [
    ['USERS', 'Users', [['USER', 'User', ['View', 'Create', 'Edit', 'Delete', 'Export']]]],
    ['ROLES', 'Roles & Permissions', [['ROLE', 'Role', ['View', 'Create', 'Edit', 'Delete', 'Approve']]]],
    ['POLICIES', 'ABAC Policies', [['POLICY', 'Policy', ['View', 'Create', 'Edit', 'Delete', 'Approve']]]],
    ['LOGS', 'Audit & Logs', [['AUDIT_LOG', 'Audit log', ['View', 'Export']]]],
  ]),
  app('POS', 'Point of Sale', 'In-house', 'VMM IT', 'Retail Operations', 'Store billing, returns and shift management on POS terminals.', ALL_T, [
    ['BILLING', 'Billing', [
      ['INVOICE', 'Sales invoice', ['View', 'Create', 'Void', 'Refund', 'Print']],
      ['DISCOUNT', 'Manual discount', ['View', 'Create', 'Approve']],
      ['PAYMENT', 'Payment', ['View', 'Create', 'Refund']],
    ]],
    ['RETURNS', 'Returns & Exchange', [['RETURN', 'Return', ['View', 'Create', 'Approve']]]],
    ['SHIFT', 'Shift & Cash', [
      ['CASH_DRAWER', 'Cash drawer', ['View', 'Create', 'Edit', 'Approve']],
      ['SHIFT_REPORT', 'Shift report', ['View', 'Print', 'Export']],
    ]],
    ['CONFIG', 'Terminal Config', [['TERMINAL', 'POS terminal', ['View', 'Create', 'Edit', 'Delete']]]],
  ]),
  app('WMS', 'Warehouse & Inventory', 'In-house', 'VMM IT', 'Supply Chain', 'Stock, GRN, stock transfers and dispatch across DCs and stores.', ALL_T, [
    ['INVENTORY', 'Inventory', [
      ['STOCK', 'Stock', ['View', 'Edit', 'Export']],
      ['STOCK_ADJ', 'Stock adjustment', ['View', 'Create', 'Approve']],
    ]],
    ['INBOUND', 'Inbound', [['GRN', 'Goods receipt (GRN)', ['View', 'Create', 'Edit', 'Approve']]]],
    ['OUTBOUND', 'Outbound', [
      ['STO', 'Stock transfer order', ['View', 'Create', 'Approve']],
      ['DISPATCH', 'Dispatch', ['View', 'Create', 'Print']],
    ]],
  ]),
  app('MERCH', 'Merchandising & Pricing', 'In-house', 'VMM IT', 'Merchandising', 'Article master, pricing, promotions and OTB planning.', ALL_T, [
    ['CATALOG', 'Catalog', [['ARTICLE', 'Article', ['View', 'Create', 'Edit', 'Delete', 'Export']]]],
    ['PRICING', 'Pricing', [
      ['PRICE', 'Price', ['View', 'Edit', 'Approve']],
      ['PROMO', 'Promotion', ['View', 'Create', 'Edit', 'Approve', 'Delete']],
    ]],
    ['PLANNING', 'Planning', [['OTB', 'Open-to-buy', ['View', 'Edit', 'Approve']]]],
  ]),
  app('FIN', 'Finance & Accounts (ERP)', 'Third-party', 'ERP vendor', 'Finance', 'Accounts payable, general ledger and financial reporting.', ALL_T, [
    ['AP', 'Accounts Payable', [
      ['VENDOR_INVOICE', 'Vendor invoice', ['View', 'Create', 'Edit', 'Approve', 'Export']],
      ['PAYMENT_RUN', 'Payment run', ['View', 'Create', 'Approve']],
    ]],
    ['GL', 'General Ledger', [['JOURNAL', 'Journal entry', ['View', 'Create', 'Approve', 'Export']]]],
    ['REPORTS', 'Financial Reports', [['PNL', 'Profit & loss', ['View', 'Export', 'Print']]]],
  ]),
  app('HRMS', 'Human Resources (HRMS)', 'Third-party', 'HRMS vendor', 'Human Resources', 'Employee master, attendance, leave and employee self-service.', ALL_T, [
    ['CORE', 'Employee Master', [['EMPLOYEE', 'Employee record', ['View', 'Create', 'Edit', 'Delete', 'Export']]]],
    ['ATTENDANCE', 'Attendance', [['ATTENDANCE', 'Attendance', ['View', 'Edit', 'Approve']]]],
    ['LEAVE', 'Leave', [['LEAVE_REQ', 'Leave request', ['View', 'Create', 'Approve']]]],
    ['ESS', 'Employee Self Service', [
      ['PAYSLIP', 'Payslip', ['View', 'Print']],
      ['PROFILE', 'My profile', ['View', 'Edit']],
    ]],
  ]),
  app('PAYROLL', 'Payroll', 'Third-party', 'Payroll vendor', 'Human Resources', 'Monthly payroll processing and statutory compliance.', ALL_T, [
    ['PROCESS', 'Payroll Processing', [
      ['PAYROLL_RUN', 'Payroll run', ['View', 'Create', 'Approve']],
      ['SALARY', 'Salary structure', ['View', 'Edit', 'Export']],
    ]],
    ['COMPLIANCE', 'Statutory', [['PF_ESI', 'PF / ESI returns', ['View', 'Export']]]],
  ]),
  app('PROC', 'Procurement & Vendor Portal', 'In-house', 'VMM IT', 'Procurement', 'Purchase orders, RFQs and vendor onboarding.', [T_VMM, T_VENDOR, T_UAT], [
    ['PO', 'Purchase Orders', [['PURCHASE_ORDER', 'Purchase order', ['View', 'Create', 'Edit', 'Approve', 'Delete', 'Print']]]],
    ['VENDOR', 'Vendor Master', [['VENDOR', 'Vendor', ['View', 'Create', 'Edit', 'Approve']]]],
    ['RFQ', 'RFQ', [['RFQ', 'Request for quotation', ['View', 'Create', 'Approve']]]],
    ['INVOICE', 'Vendor Invoicing', [['VENDOR_BILL', 'Vendor bill', ['View', 'Create', 'Edit']]]],
  ]),
  app('CRM', 'Loyalty & CRM', 'Third-party', 'CRM vendor', 'Marketing', 'Customer profiles, loyalty points and campaigns.', ALL_T, [
    ['CUSTOMER', 'Customers', [['CUSTOMER', 'Customer', ['View', 'Edit', 'Export', 'Delete']]]],
    ['LOYALTY', 'Loyalty', [['POINTS', 'Loyalty points', ['View', 'Edit', 'Approve']]]],
    ['CAMPAIGN', 'Campaigns', [['CAMPAIGN', 'Campaign', ['View', 'Create', 'Approve']]]],
  ]),
  app('ECOM', 'Online Orders', 'In-house', 'VMM IT', 'E-commerce', 'Online orders and store fulfilment.', ALL_T, [
    ['ORDERS', 'Orders', [['ORDER', 'Customer order', ['View', 'Edit', 'Void', 'Refund']]]],
    ['FULFILMENT', 'Fulfilment', [['PICKLIST', 'Pick list', ['View', 'Create', 'Print']]]],
  ]),
  app('BI', 'BI & Reporting', 'Third-party', 'BI vendor', 'Strategy', 'Dashboards and analytical reports.', ALL_T, [
    ['SALES', 'Sales Analytics', [
      ['SALES_DASH', 'Sales dashboard', ['View', 'Export']],
      ['STORE_PL', 'Store P&L', ['View', 'Export']],
    ]],
    ['HR', 'HR Analytics', [['HR_DASH', 'HR dashboard', ['View', 'Export']]]],
  ]),
  app('STOREOPS', 'Store Operations', 'In-house', 'VMM IT', 'Retail Operations', 'Store tasks, audits and asset register.', ALL_T, [
    ['TASKS', 'Tasks', [['TASK', 'Task', ['View', 'Create', 'Edit', 'Approve']]]],
    ['AUDIT', 'Store Audit', [['STORE_AUDIT', 'Store audit', ['View', 'Create', 'Approve']]]],
    ['ASSETS', 'Assets', [['ASSET', 'Asset', ['View', 'Create', 'Edit']]]],
  ]),
  app('ITSM', 'IT Helpdesk', 'Third-party', 'ITSM vendor', 'IT', 'Tickets and access requests.', ALL_T, [
    ['TICKETS', 'Tickets', [['TICKET', 'Ticket', ['View', 'Create', 'Edit', 'Approve']]]],
    ['ACCESS', 'Access Requests', [['ACCESS_REQ', 'Access request', ['View', 'Create', 'Approve']]]],
  ]),
];

// ---------------------------------------------------------------- roles

function role(tenantId: string, code: string, name: string, appCode: string | null, description: string, permissions: string[], isSystem = false): Role {
  return {
    id: `role-${tenantId}-${code.toLowerCase()}`,
    tenantId,
    code,
    name,
    description,
    appId: appCode ? `app-${appCode.toLowerCase()}` : null,
    permissions,
    isSystem,
    isActive: true,
  };
}

const each = (prefix: string, actions: Action[]) => actions.map((a) => `${prefix}.${a.toUpperCase()}`);

const vmmRoles: Role[] = [
  role(T_VMM, 'SUPER_ADMIN', 'Super Administrator', null, 'Unrestricted access to every application. Break-glass only.', ['*.*.*.*'], true),
  role(T_VMM, 'UMS_ADMIN', 'UMS Administrator', 'UMS', 'Manages users, roles and policies.', ['UMS.*.*.*'], true),
  role(T_VMM, 'AUDITOR', 'Internal Auditor', null, 'Read-only access across applications.', ['*.*.*.VIEW', '*.*.*.EXPORT'], true),
  role(T_VMM, 'EMPLOYEE', 'Employee (Self Service)', null, 'Default role for every employee.', [
    'HRMS.ESS.*.VIEW', 'HRMS.ESS.PAYSLIP.PRINT', 'HRMS.ESS.PROFILE.EDIT', ...each('HRMS.LEAVE.LEAVE_REQ', ['View', 'Create']), ...each('ITSM.TICKETS.TICKET', ['View', 'Create']), ...each('ITSM.ACCESS.ACCESS_REQ', ['View', 'Create']),
  ], true),
  role(T_VMM, 'CASHIER', 'Cashier', 'POS', 'Billing on POS terminals.', [
    ...each('POS.BILLING.INVOICE', ['View', 'Create', 'Print']), ...each('POS.BILLING.PAYMENT', ['View', 'Create']), ...each('POS.RETURNS.RETURN', ['View', 'Create']), ...each('POS.SHIFT.CASH_DRAWER', ['View', 'Create']),
  ]),
  role(T_VMM, 'SHIFT_SUPERVISOR', 'Shift Supervisor', 'POS', 'Approves voids, refunds and discounts during a shift.', ['POS.BILLING.*.*', 'POS.RETURNS.*.*', 'POS.SHIFT.*.*']),
  role(T_VMM, 'STORE_MANAGER', 'Store Manager', null, 'Runs a store: POS, store ops, store stock and team attendance.', [
    'POS.*.*.*', 'STOREOPS.*.*.*', 'WMS.INVENTORY.*.VIEW', 'WMS.INBOUND.GRN.*', 'BI.SALES.STORE_PL.VIEW', 'HRMS.ATTENDANCE.*.*', 'HRMS.LEAVE.LEAVE_REQ.APPROVE', 'ECOM.FULFILMENT.*.*',
  ]),
  role(T_VMM, 'WH_EXEC', 'Warehouse Executive', 'WMS', 'Receives and dispatches goods.', [
    'WMS.INVENTORY.STOCK.VIEW', ...each('WMS.INBOUND.GRN', ['View', 'Create', 'Edit']), 'WMS.OUTBOUND.DISPATCH.*', 'WMS.OUTBOUND.STO.VIEW',
  ]),
  role(T_VMM, 'WH_MANAGER', 'Warehouse Manager', 'WMS', 'Full warehouse control incl. stock adjustments.', ['WMS.*.*.*']),
  role(T_VMM, 'MERCHANDISER', 'Merchandiser', 'MERCH', 'Maintains articles and proposes prices.', ['MERCH.CATALOG.*.*', ...each('MERCH.PRICING.PRICE', ['View', 'Edit']), 'MERCH.PRICING.PROMO.VIEW', 'MERCH.PLANNING.OTB.VIEW']),
  role(T_VMM, 'CATEGORY_HEAD', 'Category Head', null, 'Approves prices, promos and OTB.', ['MERCH.*.*.*', 'BI.SALES.*.*']),
  role(T_VMM, 'FIN_EXEC', 'Finance Executive', 'FIN', 'Books vendor invoices and journals.', [...each('FIN.AP.VENDOR_INVOICE', ['View', 'Create', 'Edit']), ...each('FIN.GL.JOURNAL', ['View', 'Create']), 'FIN.REPORTS.PNL.VIEW']),
  role(T_VMM, 'FIN_CONTROLLER', 'Financial Controller', null, 'Approves payments and journals.', ['FIN.*.*.*', 'PAYROLL.PROCESS.PAYROLL_RUN.APPROVE', 'BI.*.*.VIEW']),
  role(T_VMM, 'HR_EXEC', 'HR Executive', 'HRMS', 'Maintains employee master and attendance.', [...each('HRMS.CORE.EMPLOYEE', ['View', 'Create', 'Edit']), 'HRMS.ATTENDANCE.*.*', 'HRMS.LEAVE.*.VIEW']),
  role(T_VMM, 'HR_MANAGER', 'HR Manager', null, 'Full HRMS plus payroll view.', ['HRMS.*.*.*', 'PAYROLL.*.*.VIEW', 'BI.HR.*.*']),
  role(T_VMM, 'PAYROLL_ADMIN', 'Payroll Administrator', 'PAYROLL', 'Runs monthly payroll.', ['PAYROLL.*.*.*']),
  role(T_VMM, 'BUYER', 'Buyer', 'PROC', 'Raises POs and RFQs.', [...each('PROC.PO.PURCHASE_ORDER', ['View', 'Create', 'Edit', 'Print']), 'PROC.RFQ.*.*', 'PROC.VENDOR.VENDOR.VIEW']),
  role(T_VMM, 'PROC_HEAD', 'Procurement Head', 'PROC', 'Approves POs and vendors.', ['PROC.*.*.*']),
  role(T_VMM, 'CRM_EXEC', 'CRM Executive', 'CRM', 'Handles loyalty queries.', ['CRM.CUSTOMER.CUSTOMER.VIEW', ...each('CRM.LOYALTY.POINTS', ['View', 'Edit']), 'CRM.CAMPAIGN.*.VIEW']),
  role(T_VMM, 'ECOM_OPS', 'E-commerce Operations', 'ECOM', 'Manages online orders.', ['ECOM.*.*.*']),
  role(T_VMM, 'IT_SUPPORT', 'IT Support Engineer', null, 'Helpdesk and user look-up.', ['ITSM.*.*.*', 'UMS.USERS.USER.VIEW', 'POS.CONFIG.*.*']),
];

const vendorRoles: Role[] = [
  role(T_VENDOR, 'VENDOR_USER', 'Vendor User', 'PROC', 'Supplier staff: view POs, respond to RFQs, raise bills.', [
    ...each('PROC.PO.PURCHASE_ORDER', ['View', 'Print']), 'PROC.RFQ.RFQ.VIEW', ...each('PROC.INVOICE.VENDOR_BILL', ['View', 'Create', 'Edit']),
  ]),
  role(T_VENDOR, 'VENDOR_ADMIN', 'Vendor Administrator', 'PROC', 'Supplier admin; manages own vendor profile.', ['PROC.PO.*.VIEW', 'PROC.PO.*.PRINT', 'PROC.RFQ.*.*', 'PROC.INVOICE.*.*', ...each('PROC.VENDOR.VENDOR', ['View', 'Edit'])]),
];

const uatRoles: Role[] = [
  role(T_UAT, 'SUPER_ADMIN', 'Super Administrator', null, 'UAT full access.', ['*.*.*.*'], true),
  role(T_UAT, 'TESTER', 'QA Tester', null, 'Functional testing across apps (no deletes).', ['*.*.*.VIEW', '*.*.*.CREATE', '*.*.*.EDIT']),
];

const roles = [...vmmRoles, ...vendorRoles, ...uatRoles];
const rid = (tenant: string, code: string) => `role-${tenant}-${code.toLowerCase()}`;

// ---------------------------------------------------------------- schedules & holidays

const schedules: Schedule[] = [
  { id: 'sch-general', tenantId: T_VMM, code: 'GENERAL', name: 'General Office Hours', timezone: 'Asia/Kolkata', excludeHolidays: true, windows: [{ days: ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'], start: '09:30', end: '18:30' }] },
  { id: 'sch-store', tenantId: T_VMM, code: 'STORE', name: 'Store Operating Hours', timezone: 'Asia/Kolkata', excludeHolidays: false, windows: [{ days: ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'], start: '08:00', end: '23:00' }] },
  { id: 'sch-wh-night', tenantId: T_VMM, code: 'WH_NIGHT', name: 'Warehouse Night Shift', timezone: 'Asia/Kolkata', excludeHolidays: false, windows: [{ days: ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'], start: '22:00', end: '06:00' }] },
  { id: 'sch-fin-close', tenantId: T_VMM, code: 'FIN_CLOSE', name: 'Finance Month-end Extended', timezone: 'Asia/Kolkata', excludeHolidays: true, windows: [{ days: ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'], start: '08:00', end: '22:00' }] },
  { id: 'sch-vendor', tenantId: T_VENDOR, code: 'VENDOR_HOURS', name: 'Vendor Portal Hours', timezone: 'Asia/Kolkata', excludeHolidays: true, windows: [{ days: ['Mon', 'Tue', 'Wed', 'Thu', 'Fri'], start: '09:00', end: '19:00' }] },
];

const holidays: Holiday[] = [
  ['2026-01-26', 'Republic Day', []],
  ['2026-01-14', 'Makar Sankranti / Pongal', ['South']],
  ['2026-03-04', 'Holi', ['North', 'Central', 'West']],
  ['2026-08-15', 'Independence Day', []],
  ['2026-10-02', 'Gandhi Jayanti', []],
  ['2026-10-20', 'Dussehra', []],
  ['2026-11-08', 'Diwali', []],
  ['2026-12-25', 'Christmas', []],
].map(([date, name, regions], i) => ({ id: `hol-${i}`, tenantId: T_VMM, date: date as string, name: name as string, regions: regions as string[] }));

// ---------------------------------------------------------------- policies

let cid = 0;
const cond = (attribute: string, operator: Policy['conditions'][number]['operator'], value: string, valueIsAttribute = false) => ({
  id: `c-${++cid}`,
  attribute,
  operator,
  value,
  valueIsAttribute,
});

function policy(p: Omit<Policy, 'id' | 'tenantId' | 'isActive' | 'createdBy' | 'createdAt'> & { tenantId?: string }): Policy {
  return { id: `pol-${++cid}`, tenantId: T_VMM, isActive: true, createdBy: 'ums.admin', createdAt: '2026-06-01T10:00:00', ...p };
}

const policies: Policy[] = [
  policy({
    name: 'Finance executives only during office hours',
    description: 'Finance executives can use the ERP only within General Office Hours (holidays excluded). Controllers are exempt.',
    effect: 'Restrict', priority: 10, match: 'all',
    target: { app: 'FIN', module: '*', resource: '*', actions: ['*'], roles: ['FIN_EXEC'] },
    conditions: [cond('env.now', 'inSchedule', 'sch-general')],
  }),
  policy({
    name: 'Payroll access only during office hours',
    description: 'Payroll processing allowed only Mon–Sat 09:30–18:30, not on holidays.',
    effect: 'Restrict', priority: 10, match: 'all',
    target: { app: 'PAYROLL', module: '*', resource: '*', actions: ['*'], roles: [] },
    conditions: [cond('env.now', 'inSchedule', 'sch-general')],
  }),
  policy({
    name: 'POS staff: store hours on POS terminal',
    description: 'Cashiers and supervisors can bill only during store hours and only from a POS terminal.',
    effect: 'Restrict', priority: 20, match: 'all',
    target: { app: 'POS', module: '*', resource: '*', actions: ['*'], roles: ['CASHIER', 'SHIFT_SUPERVISOR'] },
    conditions: [cond('env.now', 'inSchedule', 'sch-store'), cond('env.channel', 'eq', 'POS Terminal')],
  }),
  policy({
    name: 'Supervisor refund limit ₹10,000',
    description: 'Refunds above ₹10,000 must be done by the Store Manager.',
    effect: 'Deny', priority: 30, match: 'all',
    target: { app: 'POS', module: 'BILLING', resource: '*', actions: ['Refund'], roles: ['SHIFT_SUPERVISOR'] },
    conditions: [cond('resource.amount', 'gt', '10000')],
  }),
  policy({
    name: 'HR executives edit employees of own region',
    description: 'Region of the employee record must equal the HR executive’s region.',
    effect: 'Restrict', priority: 40, match: 'all',
    target: { app: 'HRMS', module: 'CORE', resource: 'EMPLOYEE', actions: ['Edit', 'Delete'], roles: ['HR_EXEC'] },
    conditions: [cond('resource.region', 'eq', 'user.region', true)],
  }),
  policy({
    name: 'Payslip: own record or HR/Payroll',
    description: 'An employee can only see their own payslip unless they are HR Manager / Payroll Admin.',
    effect: 'Restrict', priority: 40, match: 'any',
    target: { app: 'HRMS', module: 'ESS', resource: 'PAYSLIP', actions: ['*'], roles: [] },
    conditions: [cond('resource.ownerEmpCode', 'eq', 'user.empCode', true), cond('user.roles', 'in', 'HR_MANAGER,PAYROLL_ADMIN')],
  }),
  policy({
    name: 'Designated approver may view & approve PO',
    description: 'Resource-based grant: the approver named on a PO can view/approve it even without a procurement role.',
    effect: 'Grant', priority: 50, match: 'all',
    target: { app: 'PROC', module: 'PO', resource: 'PURCHASE_ORDER', actions: ['View', 'Approve'], roles: [] },
    conditions: [cond('resource.approverEmpCode', 'eq', 'user.empCode', true)],
  }),
  policy({
    name: 'No export from unmanaged devices',
    description: 'Data export is blocked on personal / unmanaged devices.',
    effect: 'Deny', priority: 5, match: 'all',
    target: { app: '*', module: '*', resource: '*', actions: ['Export'], roles: [] },
    conditions: [cond('env.device', 'eq', 'Unmanaged')],
  }),
  policy({
    name: 'Contract & vendor staff cannot delete',
    description: 'Delete actions are restricted to permanent employees.',
    effect: 'Deny', priority: 5, match: 'all',
    target: { app: '*', module: '*', resource: '*', actions: ['Delete'], roles: [] },
    conditions: [cond('user.employmentType', 'in', 'Contract,Intern,Vendor')],
  }),
  policy({
    name: 'Finance approvals need MFA on office network/VPN',
    description: 'Approvals in Finance require MFA and an Office LAN or VPN connection.',
    effect: 'Restrict', priority: 15, match: 'all',
    target: { app: 'FIN', module: '*', resource: '*', actions: ['Approve'], roles: [] },
    conditions: [cond('user.mfaEnabled', 'eq', 'true'), cond('env.network', 'in', 'Office LAN,VPN')],
  }),
  policy({
    name: 'Store managers: own store only',
    description: 'Store managers may act only on store-tagged records of their own location.',
    effect: 'Restrict', priority: 25, match: 'all',
    target: { app: 'STOREOPS', module: '*', resource: '*', actions: ['*'], roles: ['STORE_MANAGER'] },
    conditions: [cond('resource.store', 'eq', 'user.location', true)],
  }),
  policy({
    tenantId: T_VENDOR,
    name: 'Vendors see only their own POs',
    description: 'PO vendor code must match the vendor user’s vendor code.',
    effect: 'Restrict', priority: 10, match: 'all',
    target: { app: 'PROC', module: '*', resource: '*', actions: ['*'], roles: [] },
    conditions: [cond('resource.vendorCode', 'eq', 'user.vendorCode', true), cond('env.now', 'inSchedule', 'sch-vendor')],
  }),
];

// ---------------------------------------------------------------- users

const FIRST = ['Aarav', 'Vivaan', 'Aditya', 'Vihaan', 'Arjun', 'Sai', 'Reyansh', 'Krishna', 'Ishaan', 'Rohan', 'Ananya', 'Diya', 'Priya', 'Saanvi', 'Aadhya', 'Kavya', 'Isha', 'Neha', 'Pooja', 'Riya', 'Rahul', 'Amit', 'Sunil', 'Deepak', 'Manish', 'Sneha', 'Meera', 'Nisha', 'Anjali', 'Vikram', 'Karan', 'Suresh', 'Ramesh', 'Lakshmi', 'Divya', 'Farhan', 'Imran', 'Simran', 'Harpreet', 'Gaurav'];
const LAST = ['Sharma', 'Verma', 'Gupta', 'Singh', 'Kumar', 'Agarwal', 'Mehta', 'Jain', 'Reddy', 'Nair', 'Iyer', 'Patel', 'Shah', 'Das', 'Banerjee', 'Chauhan', 'Yadav', 'Mishra', 'Pandey', 'Khan', 'Bhardwaj', 'Joshi', 'Malhotra', 'Kapoor', 'Saxena'];
export const REGIONS = ['North', 'East', 'West', 'South', 'Central'];
const STORES: Record<string, string[]> = {
  North: ['S001', 'S012', 'S045', 'S046', 'S078'],
  East: ['S101', 'S115', 'S130'],
  West: ['S201', 'S214', 'S233'],
  South: ['S301', 'S322', 'S340'],
  Central: ['S401', 'S418'],
};
const DCS: Record<string, string> = { North: 'DC-NR1', East: 'DC-ER1', West: 'DC-WR1', South: 'DC-SR1', Central: 'DC-CR1' };

/** Local ISO timestamp without zone, e.g. 2026-10-24T11:05:00 */
export function localIso(d: Date): string {
  const p = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}T${p(d.getHours())}:${p(d.getMinutes())}:${p(d.getSeconds())}`;
}
/** Current local time as used by datetime-local inputs, e.g. 2026-10-24T11:05 */
export const nowLocal = () => localIso(new Date()).slice(0, 16);

// demo logs and expiries are generated relative to the moment the seed is created
const today = new Date();
today.setSeconds(0, 0);
const daysAgo = (n: number) => {
  const d = new Date(today);
  d.setDate(d.getDate() - n);
  return d;
};
const iso = localIso;
const dstr = (d: Date) => iso(d).slice(0, 10);

const assign = (roleId: string, scope: Record<string, string> = {}, validTo: string | null = null, validFrom: string | null = null): RoleAssignment => ({
  roleId,
  scope,
  validFrom,
  validTo,
  assignedBy: 'ums.admin',
  assignedOn: '2026-04-01',
});

interface Profile {
  dept: string;
  designation: string;
  roles: string[];
  weight: number;
  site: 'store' | 'dc' | 'ho';
  scopeStore?: boolean;
}

const PROFILES: Profile[] = [
  { dept: 'Retail Operations', designation: 'Cashier', roles: ['CASHIER'], weight: 30, site: 'store', scopeStore: true },
  { dept: 'Retail Operations', designation: 'Shift Supervisor', roles: ['SHIFT_SUPERVISOR'], weight: 10, site: 'store', scopeStore: true },
  { dept: 'Retail Operations', designation: 'Store Manager', roles: ['STORE_MANAGER'], weight: 6, site: 'store', scopeStore: true },
  { dept: 'Supply Chain', designation: 'Warehouse Executive', roles: ['WH_EXEC'], weight: 12, site: 'dc' },
  { dept: 'Supply Chain', designation: 'Warehouse Manager', roles: ['WH_MANAGER'], weight: 3, site: 'dc' },
  { dept: 'Merchandising', designation: 'Merchandiser', roles: ['MERCHANDISER'], weight: 6, site: 'ho' },
  { dept: 'Merchandising', designation: 'Category Head', roles: ['CATEGORY_HEAD'], weight: 2, site: 'ho' },
  { dept: 'Finance', designation: 'Accounts Executive', roles: ['FIN_EXEC'], weight: 6, site: 'ho' },
  { dept: 'Finance', designation: 'Financial Controller', roles: ['FIN_CONTROLLER'], weight: 1, site: 'ho' },
  { dept: 'Human Resources', designation: 'HR Executive', roles: ['HR_EXEC'], weight: 5, site: 'ho' },
  { dept: 'Human Resources', designation: 'HR Manager', roles: ['HR_MANAGER'], weight: 1, site: 'ho' },
  { dept: 'Human Resources', designation: 'Payroll Specialist', roles: ['PAYROLL_ADMIN'], weight: 1, site: 'ho' },
  { dept: 'Procurement', designation: 'Buyer', roles: ['BUYER'], weight: 4, site: 'ho' },
  { dept: 'Marketing', designation: 'CRM Executive', roles: ['CRM_EXEC'], weight: 3, site: 'ho' },
  { dept: 'E-commerce', designation: 'E-com Operations', roles: ['ECOM_OPS'], weight: 3, site: 'ho' },
  { dept: 'IT', designation: 'IT Support Engineer', roles: ['IT_SUPPORT'], weight: 3, site: 'ho' },
  { dept: 'Internal Audit', designation: 'Internal Auditor', roles: ['AUDITOR'], weight: 1, site: 'ho' },
];
const totalWeight = PROFILES.reduce((s, p) => s + p.weight, 0);
function pickProfile(): Profile {
  let r = rnd() * totalWeight;
  for (const p of PROFILES) if ((r -= p.weight) < 0) return p;
  return PROFILES[0];
}

const usedEmails = new Set<string>();
function makeUser(n: number, over: Partial<User> & { profile?: Profile } = {}): User {
  const first = pick(FIRST);
  const last = pick(LAST);
  const profile = over.profile ?? pickProfile();
  const region = over.region ?? pick(REGIONS);
  const location = over.location ?? (profile.site === 'store' ? pick(STORES[region]) : profile.site === 'dc' ? DCS[region] : 'HO-GGN');
  const fullName = over.fullName ?? `${first} ${last}`;
  let email = `${fullName.toLowerCase().replace(/[^a-z]+/g, '.')}@vmm.example`;
  if (usedEmails.has(email)) email = email.replace('@', `${n}@`);
  usedEmails.add(email);
  const employmentType: EmploymentType = over.employmentType ?? (rnd() < 0.12 ? 'Contract' : 'Permanent');
  const r = rnd();
  const { profile: _p, ...rest } = over;
  void _p;
  return {
    id: `u-${n}`,
    tenantId: T_VMM,
    empCode: `VMM${String(10000 + n)}`,
    fullName,
    email,
    phone: `+91 9${String(Math.floor(rnd() * 1e9)).padStart(9, '0')}`,
    department: profile.dept,
    designation: profile.designation,
    location,
    region,
    employmentType,
    status: r < 0.04 ? 'Locked' : r < 0.09 ? 'Inactive' : 'Active',
    mfaEnabled: profile.site === 'ho' ? rnd() < 0.85 : rnd() < 0.3,
    lastLogin: iso(daysAgo(Math.floor(rnd() * 20))),
    accountExpiry: employmentType === 'Contract' ? dstr(daysAgo(-Math.floor(rnd() * 200))) : null,
    attributes: { costCenter: `CC-${profile.dept.slice(0, 3).toUpperCase()}-${region.slice(0, 1)}`, grade: pick(['L1', 'L2', 'L3', 'M1', 'M2']) },
    roles: [
      assign(rid(T_VMM, 'EMPLOYEE')),
      ...profile.roles.map((code) => assign(rid(T_VMM, code), profile.scopeStore ? { store: location } : {})),
    ],
    ...rest,
  };
}

const named: User[] = [
  makeUser(1, { fullName: 'Aarav Sharma', profile: PROFILES[2], region: 'North', location: 'S045', status: 'Active', mfaEnabled: true, employmentType: 'Permanent', accountExpiry: null }),
  makeUser(2, { fullName: 'Priya Verma', profile: PROFILES[0], region: 'North', location: 'S045', status: 'Active', employmentType: 'Permanent', accountExpiry: null }),
  makeUser(3, { fullName: 'Rohit Gupta', profile: PROFILES[1], region: 'North', location: 'S045', status: 'Active', employmentType: 'Permanent', accountExpiry: null }),
  makeUser(4, { fullName: 'Neha Singh', profile: PROFILES[7], region: 'North', status: 'Active', mfaEnabled: false, employmentType: 'Permanent', accountExpiry: null }),
  makeUser(5, { fullName: 'Vikram Mehta', profile: PROFILES[8], region: 'North', status: 'Active', mfaEnabled: true, employmentType: 'Permanent', accountExpiry: null }),
  makeUser(6, { fullName: 'Kavya Nair', profile: PROFILES[9], region: 'South', status: 'Active', mfaEnabled: true, employmentType: 'Permanent', accountExpiry: null }),
  makeUser(7, { fullName: 'Imran Khan', profile: PROFILES[3], region: 'West', status: 'Active', employmentType: 'Contract', accountExpiry: '2027-03-31' }),
  makeUser(8, { fullName: 'Sneha Iyer', profile: PROFILES[15], region: 'North', status: 'Active', mfaEnabled: true, employmentType: 'Permanent', accountExpiry: null }),
  makeUser(9, { fullName: 'Ankit Saxena', profile: { dept: 'IT', designation: 'Head – IT Applications', roles: ['UMS_ADMIN', 'SUPER_ADMIN'], weight: 0, site: 'ho' }, region: 'North', status: 'Active', mfaEnabled: true, employmentType: 'Permanent', accountExpiry: null }),
  makeUser(10, { fullName: 'Rahul Agarwal', profile: PROFILES[12], region: 'North', status: 'Active', mfaEnabled: true, employmentType: 'Permanent', accountExpiry: null }),
];
// temporary access example: Rahul covers procurement head duties for one week (already expired)
named[9].roles.push({ ...assign(rid(T_VMM, 'PROC_HEAD'), {}, '2026-10-10', '2026-10-03') });
// store manager covering two stores
named[0].roles = [assign(rid(T_VMM, 'EMPLOYEE')), assign(rid(T_VMM, 'STORE_MANAGER'), { store: 'S045,S046' })];

const generated: User[] = Array.from({ length: 290 }, (_, i) => makeUser(i + 11));

const vendorUsers: User[] = [
  ['Ravi Traders', 'V1001'],
  ['Shree Textiles', 'V1002'],
  ['FreshFarm Foods', 'V1003'],
  ['Metro Plastics', 'V1004'],
].flatMap(([company, code], i) => [
  {
    id: `vu-${i}a`, tenantId: T_VENDOR, empCode: `${code}-01`, fullName: `${pick(FIRST)} ${pick(LAST)}`, email: `admin@${String(company).toLowerCase().replace(/\s+/g, '')}.example`,
    phone: '+91 98100 00000', department: String(company), designation: 'Vendor Admin', location: 'EXT', region: pick(REGIONS), employmentType: 'Vendor' as const,
    status: 'Active' as const, mfaEnabled: true, lastLogin: iso(daysAgo(i + 1)), accountExpiry: '2027-03-31',
    attributes: { vendorCode: String(code), company: String(company) }, roles: [assign(rid(T_VENDOR, 'VENDOR_ADMIN'))],
  },
  {
    id: `vu-${i}b`, tenantId: T_VENDOR, empCode: `${code}-02`, fullName: `${pick(FIRST)} ${pick(LAST)}`, email: `sales@${String(company).toLowerCase().replace(/\s+/g, '')}.example`,
    phone: '+91 98100 00001', department: String(company), designation: 'Sales Executive', location: 'EXT', region: pick(REGIONS), employmentType: 'Vendor' as const,
    status: 'Active' as const, mfaEnabled: false, lastLogin: iso(daysAgo(i + 3)), accountExpiry: '2027-03-31',
    attributes: { vendorCode: String(code), company: String(company) }, roles: [assign(rid(T_VENDOR, 'VENDOR_USER'))],
  },
]);

const uatUsers: User[] = [1, 2, 3].map((n) => ({
  ...makeUser(900 + n, { profile: PROFILES[15] }),
  id: `uat-${n}`,
  tenantId: T_UAT,
  designation: 'QA Engineer',
  department: 'IT',
  roles: [assign(rid(T_UAT, 'TESTER'))],
}));

const users = [...named, ...generated, ...vendorUsers, ...uatUsers];

// ---------------------------------------------------------------- logs

const IPS = ['10.12.4.21', '10.12.8.5', '10.40.1.77', '172.16.20.4', '10.88.3.19', '49.36.112.8'];

const auditLogs: AuditLog[] = [
  ['Role', 'Update', 'Added POS.BILLING.DISCOUNT.APPROVE to SHIFT_SUPERVISOR'],
  ['User', 'Assign', 'Assigned STORE_MANAGER (store=S045,S046) to Aarav Sharma'],
  ['User', 'Create', 'Created user Imran Khan (Contract, valid to 2027-03-31)'],
  ['Policy', 'Create', "Created policy 'No export from unmanaged devices'"],
  ['User', 'Update', 'Locked account after 5 failed logins'],
  ['Policy', 'Update', "Changed priority of 'Supervisor refund limit ₹10,000' to 30"],
  ['User', 'Revoke', 'Revoked FIN_EXEC from transferred employee'],
  ['Application', 'Update', 'Enabled Procurement & Vendor Portal for Vendor & Partner Network'],
  ['Schedule', 'Update', 'Updated General Office Hours: Mon–Sat 09:30–18:30'],
  ['User', 'Reset', 'Password reset & MFA re-enrolment'],
  ['Role', 'Create', 'Created role ECOM_OPS'],
  ['User', 'Assign', 'Temporary PROC_HEAD to Rahul Agarwal (03-Oct → 10-Oct)'],
].map(([entity, action, summary], i) => ({
  id: `al-${i}`,
  tenantId: T_VMM,
  timestamp: iso(daysAgo(i * 0.7)),
  actor: i % 3 === 0 ? 'ankit.saxena' : i % 3 === 1 ? 'sneha.iyer' : 'ums.admin',
  entity: entity as string,
  entityId: `${entity.toLowerCase()}-${100 + i}`,
  action: action as AuditLog['action'],
  summary: summary as string,
  oldValues: action === 'Update' ? { status: 'Active' } : null,
  newValues: action === 'Update' ? { status: i === 4 ? 'Locked' : 'Active', changed: summary } : { summary },
  ip: pick(IPS),
  device: pick(['Chrome / Windows 11', 'Edge / Windows 10', 'Safari / macOS']),
}));

const ACTION_SAMPLES: [string, string, string, 'Allow' | 'Deny', string][] = [
  ['POS', '/billing/invoice/new', 'POS.BILLING.INVOICE.CREATE', 'Allow', 'permitted'],
  ['POS', '/billing/invoice/8812/refund', 'POS.BILLING.INVOICE.REFUND', 'Deny', "Policy: Deny 'Supervisor refund limit ₹10,000'"],
  ['FIN', '/ap/vendor-invoice/220/approve', 'FIN.AP.VENDOR_INVOICE.APPROVE', 'Deny', 'Permission: missing FIN.AP.VENDOR_INVOICE.APPROVE'],
  ['FIN', '/gl/journal', 'FIN.GL.JOURNAL.VIEW', 'Deny', "Policy: Restrict 'Finance executives only during office hours'"],
  ['HRMS', '/ess/payslip/2026-09', 'HRMS.ESS.PAYSLIP.VIEW', 'Allow', 'permitted'],
  ['WMS', '/inbound/grn/new', 'WMS.INBOUND.GRN.CREATE', 'Allow', 'permitted'],
  ['MERCH', '/pricing/price/approve', 'MERCH.PRICING.PRICE.APPROVE', 'Deny', 'Permission: missing MERCH.PRICING.PRICE.APPROVE'],
  ['BI', '/sales/dashboard/export', 'BI.SALES.SALES_DASH.EXPORT', 'Deny', "Policy: Deny 'No export from unmanaged devices'"],
  ['STOREOPS', '/tasks', 'STOREOPS.TASKS.TASK.VIEW', 'Allow', 'permitted'],
  ['PROC', '/po/4410/approve', 'PROC.PO.PURCHASE_ORDER.APPROVE', 'Allow', "granted by policy 'Designated approver may view & approve PO'"],
];
const actionLogs: UserActionLog[] = Array.from({ length: 80 }, (_, i) => {
  const u = pick(named.concat(generated.slice(0, 40)));
  const [appCode, route, action, decision, reason] = pick(ACTION_SAMPLES);
  const d = new Date(today);
  d.setMinutes(d.getMinutes() - i * 37);
  return { id: `ua-${i}`, tenantId: T_VMM, timestamp: iso(d), userId: u.id, userName: u.fullName, app: appCode, route, action, decision, reason, ip: pick(IPS) };
});

const EXC: [string, ExceptionLog['severity'], string, string][] = [
  ['POS', 'Error', 'TimeoutException: Payment gateway did not respond in 30s', '/api/billing/payment'],
  ['WMS', 'Error', 'DbUpdateConcurrencyException: Stock row was modified by another user', '/api/inventory/stock/88231'],
  ['HRMS', 'Warning', 'SCIM sync: 3 employees skipped (missing manager)', '/scim/v2/Users'],
  ['UMS', 'Critical', 'Redis connection lost; falling back to DB for token validation', '/connect/token'],
  ['FIN', 'Error', 'HttpRequestException: ERP connector returned 502 Bad Gateway', '/api/ap/vendor-invoice'],
  ['UMS', 'Error', 'PolicyEvaluationException: attribute resource.amount is not numeric', '/api/authorize'],
  ['ECOM', 'Warning', 'Order 55120 refund retried 3 times', '/api/orders/55120/refund'],
  ['CRM', 'Error', 'NullReferenceException in LoyaltyPointsService.Redeem', '/api/loyalty/redeem'],
];
const exceptionLogs: ExceptionLog[] = Array.from({ length: 18 }, (_, i) => {
  const [appCode, severity, message, path] = EXC[i % EXC.length];
  const d = new Date(today);
  d.setHours(d.getHours() - i * 5);
  return {
    id: `ex-${i}`,
    tenantId: T_VMM,
    timestamp: iso(d),
    app: appCode,
    severity,
    message,
    stackTrace: `${message.split(':')[0]}\n   at VMM.${appCode}.Services.Handler.ExecuteAsync()\n   at VMM.Common.Middleware.ExceptionLoggerMiddleware.Invoke(HttpContext ctx)\n   at Microsoft.AspNetCore.Routing.EndpointMiddleware.Invoke(HttpContext ctx)`,
    userName: i % 3 === 0 ? null : pick(named).fullName,
    requestPath: path,
    ip: pick(IPS),
    resolved: i > 9,
  };
});

const defaultSettings: SecuritySettings = {
  passwordMinLength: 12,
  passwordExpiryDays: 90,
  maxFailedLogins: 5,
  lockoutMinutes: 30,
  sessionTimeoutMinutes: 30,
  mfaRequiredForAdmins: true,
  mfaRequiredOutsideOffice: true,
  jwtExpiryMinutes: 15,
  refreshTokenDays: 7,
  allowedIpRanges: '10.0.0.0/8, 172.16.0.0/12',
};

export function createSeed(): UmsState {
  return {
    tenants,
    applications,
    roles,
    users,
    policies,
    schedules,
    holidays,
    auditLogs,
    actionLogs,
    exceptionLogs,
    settings: { [T_VMM]: defaultSettings, [T_VENDOR]: { ...defaultSettings, sessionTimeoutMinutes: 15 }, [T_UAT]: { ...defaultSettings, mfaRequiredForAdmins: false } },
  };
}


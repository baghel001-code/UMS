import { describe, expect, it } from 'vitest';
import { createSeed } from '../data/seed';
import type { AccessRequest, Environment, Schedule } from '../types';
import { decide, isInSchedule, permissionMatches, policyExpression } from './pdp';

const data = createSeed();
const officeEnv: Environment = { dateTime: '2026-10-23T11:00', ip: '10.12.4.21', network: 'Office LAN', device: 'Managed', channel: 'Web' };
const posEnv: Environment = { ...officeEnv, channel: 'POS Terminal' };

const req = (over: Partial<AccessRequest>): AccessRequest => ({
  userId: 'u-1',
  app: 'POS',
  module: 'BILLING',
  resource: 'INVOICE',
  action: 'View',
  resourceAttributes: {},
  env: officeEnv,
  ...over,
});

describe('permissionMatches', () => {
  it('supports wildcards per segment', () => {
    expect(permissionMatches('POS.*.*.*', 'POS.BILLING.INVOICE.REFUND')).toBe(true);
    expect(permissionMatches('*.*.*.VIEW', 'HRMS.CORE.EMPLOYEE.VIEW')).toBe(true);
    expect(permissionMatches('*.*.*.VIEW', 'HRMS.CORE.EMPLOYEE.EDIT')).toBe(false);
    expect(permissionMatches('POS.BILLING.INVOICE.VIEW', 'POS.*.*.*')).toBe(true);
    expect(permissionMatches('WMS.*.*.*', 'POS.*.*.*')).toBe(false);
  });
});

describe('schedules', () => {
  const general = data.schedules.find((s) => s.code === 'GENERAL')!;
  const night = data.schedules.find((s) => s.code === 'WH_NIGHT')!;
  it('office hours on a working day', () => {
    expect(isInSchedule(general, new Date('2026-10-23T10:00'), data.holidays)).toBe(true);
    expect(isInSchedule(general, new Date('2026-10-23T19:00'), data.holidays)).toBe(false);
    expect(isInSchedule(general, new Date('2026-10-25T10:00'), data.holidays)).toBe(false); // Sunday
  });
  it('excludes holidays', () => {
    expect(isInSchedule(general, new Date('2026-10-02T10:00'), data.holidays)).toBe(false);
  });
  it('regional holidays only apply to that region', () => {
    expect(isInSchedule(general, new Date('2026-01-14T10:00'), data.holidays, 'South')).toBe(false);
    expect(isInSchedule(general, new Date('2026-01-14T10:00'), data.holidays, 'North')).toBe(true);
  });
  it('overnight windows', () => {
    expect(isInSchedule(night, new Date('2026-10-23T23:30'), data.holidays)).toBe(true);
    expect(isInSchedule(night, new Date('2026-10-24T05:59'), data.holidays)).toBe(true);
    expect(isInSchedule(night, new Date('2026-10-24T06:00'), data.holidays)).toBe(false);
  });
});

describe('decide — layered checks', () => {
  it('cashier can create an invoice at a POS terminal in store hours', () => {
    const d = decide(req({ userId: 'u-2', action: 'Create', env: posEnv, resourceAttributes: { store: 'S045' } }), data);
    expect(d.allowed).toBe(true);
  });

  it('cashier is blocked outside store hours (office-hours policy)', () => {
    const d = decide(req({ userId: 'u-2', action: 'Create', env: { ...posEnv, dateTime: '2026-10-23T23:30' } }), data);
    expect(d.allowed).toBe(false);
    expect(d.reason).toMatch(/store hours/);
  });

  it('cashier is blocked from a web browser', () => {
    const d = decide(req({ userId: 'u-2', action: 'Create' }), data);
    expect(d.allowed).toBe(false);
  });

  it('cashier has no refund permission (action layer)', () => {
    const d = decide(req({ userId: 'u-2', action: 'Refund', env: posEnv }), data);
    expect(d.allowed).toBe(false);
    expect(d.steps.at(-1)!.layer).toBe('Permission');
  });

  it('cashier has no access to Finance (application layer)', () => {
    const d = decide(req({ userId: 'u-2', app: 'FIN', module: 'GL', resource: 'JOURNAL' }), data);
    expect(d.allowed).toBe(false);
    expect(d.steps.at(-1)!.layer).toBe('Application');
  });

  it('cashier cannot open POS terminal config (module layer)', () => {
    const d = decide(req({ userId: 'u-2', module: 'CONFIG', resource: 'TERMINAL', env: posEnv }), data);
    expect(d.allowed).toBe(false);
    expect(d.steps.at(-1)!.layer).toBe('Module');
  });

  it('role scope: cashier of S045 cannot bill for another store', () => {
    const d = decide(req({ userId: 'u-2', action: 'Create', env: posEnv, resourceAttributes: { store: 'S101' } }), data);
    expect(d.allowed).toBe(false);
    expect(d.steps.at(-1)!.layer).toBe('Scope');
  });

  it('supervisor refund above limit is denied, below is allowed', () => {
    expect(decide(req({ userId: 'u-3', action: 'Refund', env: posEnv, resourceAttributes: { store: 'S045', amount: '15000' } }), data).allowed).toBe(false);
    expect(decide(req({ userId: 'u-3', action: 'Refund', env: posEnv, resourceAttributes: { store: 'S045', amount: '2500' } }), data).allowed).toBe(true);
  });

  it('finance approval requires MFA (Neha has none) — and Neha lacks Approve anyway', () => {
    const d = decide(req({ userId: 'u-4', app: 'FIN', module: 'AP', resource: 'VENDOR_INVOICE', action: 'Approve' }), data);
    expect(d.allowed).toBe(false);
  });

  it('controller approves on office LAN, not from internet', () => {
    const base = { userId: 'u-5', app: 'FIN', module: 'AP', resource: 'VENDOR_INVOICE', action: 'Approve' as const };
    expect(decide(req(base), data).allowed).toBe(true);
    expect(decide(req({ ...base, env: { ...officeEnv, network: 'Internet' } }), data).allowed).toBe(false);
  });

  it('finance executive blocked after office hours', () => {
    const base = { userId: 'u-4', app: 'FIN', module: 'GL', resource: 'JOURNAL', action: 'View' as const };
    expect(decide(req(base), data).allowed).toBe(true);
    expect(decide(req({ ...base, env: { ...officeEnv, dateTime: '2026-10-23T20:15' } }), data).allowed).toBe(false);
  });

  it('payslip: own allowed, someone else denied', () => {
    const base = { userId: 'u-2', app: 'HRMS', module: 'ESS', resource: 'PAYSLIP', action: 'View' as const };
    expect(decide(req({ ...base, resourceAttributes: { ownerEmpCode: 'VMM10002' } }), data).allowed).toBe(true);
    expect(decide(req({ ...base, resourceAttributes: { ownerEmpCode: 'VMM10003' } }), data).allowed).toBe(false);
  });

  it('grant policy: named approver can approve PO without a procurement role', () => {
    const base = { userId: 'u-5', app: 'PROC', module: 'PO', resource: 'PURCHASE_ORDER', action: 'Approve' as const };
    expect(decide(req({ ...base, resourceAttributes: { approverEmpCode: 'VMM10005' } }), data).allowed).toBe(true);
    expect(decide(req({ ...base, resourceAttributes: { approverEmpCode: 'VMM10099' } }), data).allowed).toBe(false);
  });

  it('expired temporary role is ignored', () => {
    const d = decide(req({ userId: 'u-10', app: 'PROC', module: 'PO', resource: 'PURCHASE_ORDER', action: 'Approve' }), data);
    expect(d.allowed).toBe(false);
  });

  it('contract staff cannot delete even with permission', () => {
    const contract = { ...data, users: data.users.map((u) => (u.id === 'u-9' ? { ...u, employmentType: 'Contract' as const } : u)) };
    const d = decide(req({ userId: 'u-9', app: 'UMS', module: 'USERS', resource: 'USER', action: 'Delete' }), contract);
    expect(d.allowed).toBe(false);
    expect(decide(req({ userId: 'u-9', app: 'UMS', module: 'USERS', resource: 'USER', action: 'Delete' }), data).allowed).toBe(true);
  });

  it('locked users are denied at the identity layer', () => {
    const locked = { ...data, users: data.users.map((u) => (u.id === 'u-2' ? { ...u, status: 'Locked' as const } : u)) };
    const d = decide(req({ userId: 'u-2', env: posEnv }), locked);
    expect(d.allowed).toBe(false);
    expect(d.steps.at(-1)!.layer).toBe('Identity');
  });
});

describe('policyExpression', () => {
  it('renders a readable rule', () => {
    const schedules: Schedule[] = data.schedules;
    const p = data.policies.find((x) => x.name.startsWith('POS staff'))!;
    expect(policyExpression(p, schedules)).toBe("InSchedule(env.now, 'STORE') && env.channel == 'POS Terminal'");
  });
});

import { useMemo, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { Field, KeyValueEditor, PageHead } from '../components/ui';
import { nowLocal } from '../data/seed';
import { decide, type PdpData } from '../engine/pdp';
import { newId, useStore, useTenantData } from '../store/store';
import { WEEKDAYS, type AccessRequest, type Action, type Environment } from '../types';

interface Scenario {
  label: string;
  req: Partial<Omit<AccessRequest, 'env'>> & { env?: Partial<Environment> };
}

const SCENARIOS: Scenario[] = [
  { label: 'Cashier bills on POS terminal', req: { userId: 'u-2', app: 'POS', module: 'BILLING', resource: 'INVOICE', action: 'Create', resourceAttributes: { store: 'S045' }, env: { channel: 'POS Terminal', dateTime: '2026-10-24T11:00' } } },
  { label: 'Cashier bills at 23:30', req: { userId: 'u-2', app: 'POS', module: 'BILLING', resource: 'INVOICE', action: 'Create', resourceAttributes: { store: 'S045' }, env: { channel: 'POS Terminal', dateTime: '2026-10-24T23:30' } } },
  { label: 'Cashier bills for another store', req: { userId: 'u-2', app: 'POS', module: 'BILLING', resource: 'INVOICE', action: 'Create', resourceAttributes: { store: 'S101' }, env: { channel: 'POS Terminal' } } },
  { label: 'Supervisor refund ₹15,000', req: { userId: 'u-3', app: 'POS', module: 'BILLING', resource: 'INVOICE', action: 'Refund', resourceAttributes: { store: 'S045', amount: '15000' }, env: { channel: 'POS Terminal' } } },
  { label: 'Finance exec at 8:15 pm', req: { userId: 'u-4', app: 'FIN', module: 'GL', resource: 'JOURNAL', action: 'Create', resourceAttributes: {}, env: { dateTime: '2026-10-23T20:15' } } },
  { label: 'Controller approves from Internet', req: { userId: 'u-5', app: 'FIN', module: 'AP', resource: 'VENDOR_INVOICE', action: 'Approve', resourceAttributes: { amount: '240000' }, env: { network: 'Internet' } } },
  { label: "View someone else's payslip", req: { userId: 'u-2', app: 'HRMS', module: 'ESS', resource: 'PAYSLIP', action: 'View', resourceAttributes: { ownerEmpCode: 'VMM10003' } } },
  { label: 'Named approver approves PO', req: { userId: 'u-5', app: 'PROC', module: 'PO', resource: 'PURCHASE_ORDER', action: 'Approve', resourceAttributes: { approverEmpCode: 'VMM10005', amount: '480000' } } },
  { label: 'Export on personal laptop', req: { userId: 'u-8', app: 'UMS', module: 'USERS', resource: 'USER', action: 'Export', env: { device: 'Unmanaged' } } },
];

// scenarios run on a fixed working day (Fri 11:00) so they behave the same whenever the demo is opened
const SCENARIO_TIME = '2026-10-23T11:00';
const defaultEnv = (): Environment => ({ dateTime: nowLocal(), ip: '10.12.4.21', network: 'Office LAN', device: 'Managed', channel: 'Web' });
const STATUS_ICON = { pass: '✓', fail: '✕', skip: '–', info: 'i' };

export default function AccessSimulator() {
  const { state, setState, tenantId, toast } = useStore();
  const d = useTenantData();
  const [params] = useSearchParams();
  const firstApp = d.applications.find((a) => a.code === params.get('app')) ?? d.applications[0];
  const firstMod = firstApp?.modules.find((m) => m.code === params.get('module')) ?? firstApp?.modules[0];
  const firstRes = firstMod?.resources.find((r) => r.code === params.get('resource')) ?? firstMod?.resources[0];

  const [req, setReq] = useState<AccessRequest>(() => ({
    userId: params.get('user') && d.users.some((u) => u.id === params.get('user')) ? params.get('user')! : d.users[0]?.id ?? '',
    app: firstApp?.code ?? '',
    module: firstMod?.code ?? '',
    resource: firstRes?.code ?? '',
    action: firstRes?.actions[0] ?? 'View',
    resourceAttributes: {},
    env: defaultEnv(),
  }));
  const [userQ, setUserQ] = useState('');

  const app = d.applications.find((a) => a.code === req.app);
  const mod = app?.modules.find((m) => m.code === req.module);
  const res = mod?.resources.find((r) => r.code === req.resource);
  const user = d.users.find((u) => u.id === req.userId);

  const data: PdpData = state;
  const decision = useMemo(() => (req.userId ? decide(req, data) : null), [req, data]);

  // allowed / denied for each hour of the selected week
  const week = useMemo(() => {
    if (!req.userId) return [];
    const base = new Date(req.env.dateTime);
    base.setDate(base.getDate() - ((base.getDay() + 6) % 7)); // back to Monday
    return WEEKDAYS.map((day, di) =>
      Array.from({ length: 24 }, (_, h) => {
        const at = new Date(base);
        at.setDate(base.getDate() + di);
        at.setHours(h, 30, 0, 0);
        const p = (n: number) => String(n).padStart(2, '0');
        const dt = `${at.getFullYear()}-${p(at.getMonth() + 1)}-${p(at.getDate())}T${p(h)}:30`;
        const r = decide({ ...req, env: { ...req.env, dateTime: dt } }, data);
        return { day, h, ok: r.allowed, reason: r.reason, dt };
      }),
    );
  }, [req, data]);

  const users = d.users.filter((u) => (u.fullName + u.empCode + u.designation + u.location).toLowerCase().includes(userQ.toLowerCase())).slice(0, 200);

  const applyScenario = (s: Scenario) => {
    const target = d.users.find((u) => u.id === s.req.userId);
    if (!target) return toast('Scenario users exist in the Vishal Mega Mart Ltd tenant', 'error');
    setReq({ ...req, resourceAttributes: {}, ...s.req, env: { ...defaultEnv(), dateTime: SCENARIO_TIME, ...s.req.env } } as AccessRequest);
  };

  const setApp = (code: string) => {
    const a = d.applications.find((x) => x.code === code);
    const m = a?.modules[0];
    const r = m?.resources[0];
    setReq({ ...req, app: code, module: m?.code ?? '', resource: r?.code ?? '', action: r?.actions[0] ?? 'View' });
  };
  const setModule = (code: string) => {
    const m = app?.modules.find((x) => x.code === code);
    const r = m?.resources[0];
    setReq({ ...req, module: code, resource: r?.code ?? '', action: r?.actions[0] ?? 'View' });
  };
  const setResource = (code: string) => {
    const r = mod?.resources.find((x) => x.code === code);
    setReq({ ...req, resource: code, action: r?.actions.includes(req.action) ? req.action : r?.actions[0] ?? 'View' });
  };

  const logDecision = () => {
    if (!decision || !user) return;
    setState((s) => ({
      ...s,
      actionLogs: [
        {
          id: newId('ua'), tenantId, timestamp: new Date().toISOString().slice(0, 19), userId: user.id, userName: user.fullName, app: req.app,
          route: `/simulator/${req.module}/${req.resource}`.toLowerCase(), action: `${req.app}.${req.module}.${req.resource}.${req.action}`.toUpperCase(),
          decision: decision.allowed ? 'Allow' : 'Deny', reason: decision.reason, ip: req.env.ip,
        },
        ...s.actionLogs,
      ],
    }));
    toast('Decision recorded in the user action trail');
  };

  return (
    <>
      <PageHead
        title="Access Simulator"
        subtitle="Ask the policy engine: can this user perform this action on this resource, right now, from here? Every layer of the decision is explained."
      />
      <div className="row" style={{ marginBottom: 14 }}>
        <span className="small muted">Scenarios:</span>
        {SCENARIOS.map((s) => (
          <button key={s.label} className="btn sm" onClick={() => applyScenario(s)}>
            {s.label}
          </button>
        ))}
      </div>

      <div className="grid-2" style={{ alignItems: 'start' }}>
        <div className="stack">
          <div className="card">
            <div className="card-head">
              <h3>Who</h3>
            </div>
            <div className="card-body stack">
              <input placeholder="Filter users…" value={userQ} onChange={(e) => setUserQ(e.target.value)} />
              <select value={req.userId} onChange={(e) => setReq({ ...req, userId: e.target.value })} size={5}>
                {user && !users.includes(user) && <option value={user.id}>{user.fullName}</option>}
                {users.map((u) => (
                  <option key={u.id} value={u.id}>
                    {u.fullName} · {u.designation} · {u.location} ({u.status})
                  </option>
                ))}
              </select>
              {user && (
                <div className="small muted">
                  {user.empCode} · {user.department} · {user.region} · {user.employmentType} · MFA {user.mfaEnabled ? 'on' : 'off'} · roles:{' '}
                  {user.roles.map((r) => d.roles.find((x) => x.id === r.roleId)?.code).join(', ')}
                </div>
              )}
            </div>
          </div>

          <div className="card">
            <div className="card-head">
              <h3>What</h3>
              <code className="small">
                {req.app}.{req.module}.{req.resource}.{req.action.toUpperCase()}
              </code>
            </div>
            <div className="card-body">
              <div className="grid-2">
                <Field label="Application">
                  <select value={req.app} onChange={(e) => setApp(e.target.value)}>
                    {d.applications.map((a) => (
                      <option key={a.id} value={a.code}>
                        {a.name}
                      </option>
                    ))}
                  </select>
                </Field>
                <Field label="Module">
                  <select value={req.module} onChange={(e) => setModule(e.target.value)}>
                    {app?.modules.map((m) => (
                      <option key={m.id} value={m.code}>
                        {m.name}
                      </option>
                    ))}
                  </select>
                </Field>
                <Field label="Resource">
                  <select value={req.resource} onChange={(e) => setResource(e.target.value)}>
                    {mod?.resources.map((r) => (
                      <option key={r.id} value={r.code}>
                        {r.name}
                      </option>
                    ))}
                  </select>
                </Field>
                <Field label="Action">
                  <select value={req.action} onChange={(e) => setReq({ ...req, action: e.target.value as Action })}>
                    {res?.actions.map((a) => (
                      <option key={a}>{a}</option>
                    ))}
                  </select>
                </Field>
              </div>
              <div style={{ marginTop: 12 }}>
                <Field label="Resource attributes (the specific record)" hint="e.g. store=S045, amount=15000, ownerEmpCode=VMM10002, region=North">
                  <KeyValueEditor value={req.resourceAttributes} onChange={(resourceAttributes) => setReq({ ...req, resourceAttributes })} keyPlaceholder="store / amount / ownerEmpCode" />
                </Field>
              </div>
            </div>
          </div>

          <div className="card">
            <div className="card-head">
              <h3>When · Where · How</h3>
            </div>
            <div className="card-body grid-2">
              <Field label="Date & time (IST)">
                <input type="datetime-local" value={req.env.dateTime} onChange={(e) => setReq({ ...req, env: { ...req.env, dateTime: e.target.value } })} />
              </Field>
              <Field label="Network">
                <select value={req.env.network} onChange={(e) => setReq({ ...req, env: { ...req.env, network: e.target.value as Environment['network'] } })}>
                  <option>Office LAN</option>
                  <option>VPN</option>
                  <option>Internet</option>
                </select>
              </Field>
              <Field label="Device">
                <select value={req.env.device} onChange={(e) => setReq({ ...req, env: { ...req.env, device: e.target.value as Environment['device'] } })}>
                  <option>Managed</option>
                  <option>Unmanaged</option>
                </select>
              </Field>
              <Field label="Channel">
                <select value={req.env.channel} onChange={(e) => setReq({ ...req, env: { ...req.env, channel: e.target.value as Environment['channel'] } })}>
                  <option>Web</option>
                  <option>Mobile</option>
                  <option>POS Terminal</option>
                  <option>API</option>
                </select>
              </Field>
              <Field label="IP address">
                <input value={req.env.ip} onChange={(e) => setReq({ ...req, env: { ...req.env, ip: e.target.value } })} />
              </Field>
            </div>
          </div>
        </div>

        <div className="stack">
          {decision && (
            <div className="card">
              <div className="card-body">
                <div className={`verdict ${decision.allowed ? 'ok' : 'bad'}`}>
                  <b>{decision.allowed ? '✓ PERMIT' : '✕ DENY'}</b>
                  <span className="small" style={{ fontWeight: 500 }}>
                    {decision.reason}
                  </span>
                </div>
                <ol className="trace" style={{ marginTop: 12 }}>
                  {decision.steps.map((s, i) => (
                    <li key={i}>
                      <span className={`dot ${s.status}`}>{STATUS_ICON[s.status]}</span>
                      <b>{s.layer}</b>
                      <span className="small">{s.detail}</span>
                    </li>
                  ))}
                </ol>
                <div className="row" style={{ marginTop: 12 }}>
                  <span className="grow small muted">Same algorithm the API's authorization middleware runs.</span>
                  <button className="btn sm" onClick={logDecision}>
                    Record in action trail
                  </button>
                </div>
              </div>
            </div>
          )}

          <div className="card">
            <div className="card-head">
              <h3>When is this allowed? (week of the selected date)</h3>
            </div>
            <div className="card-body">
              <div className="week">
                <span />
                {Array.from({ length: 24 }, (_, h) => (
                  <span key={h} className="h">
                    {h % 3 === 0 ? h : ''}
                  </span>
                ))}
                {week.map((row, di) => [
                  <span key={WEEKDAYS[di]} className="d">
                    {WEEKDAYS[di]}
                  </span>,
                  ...row.map((c) => (
                    <span
                      key={c.dt}
                      className="c"
                      style={{ background: c.ok ? 'var(--ok)' : '#f2c9c2', cursor: 'pointer' }}
                      title={`${c.day} ${String(c.h).padStart(2, '0')}:30 — ${c.ok ? 'PERMIT' : c.reason}`}
                      onClick={() => setReq({ ...req, env: { ...req.env, dateTime: c.dt } })}
                    />
                  )),
                ])}
              </div>
              <p className="small muted" style={{ marginBottom: 0 }}>
                Green = permitted. Hover for the deny reason, click a cell to evaluate that time.
              </p>
            </div>
          </div>
        </div>
      </div>
    </>
  );
}

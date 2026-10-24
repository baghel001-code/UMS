import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { Badge, Field, Modal, PageHead } from '../components/ui';
import { OPERATOR_LABELS, policyExpression } from '../engine/pdp';
import { CURRENT_ADMIN, newId, useStore, useTenantData } from '../store/store';
import { ALL_ACTIONS, type Action, type Condition, type Operator, type Policy, type PolicyEffect } from '../types';

export const KNOWN_ATTRIBUTES = [
  'user.department', 'user.designation', 'user.location', 'user.region', 'user.employmentType', 'user.empCode', 'user.mfaEnabled', 'user.roles',
  'resource.store', 'resource.region', 'resource.department', 'resource.amount', 'resource.status', 'resource.ownerEmpCode', 'resource.approverEmpCode', 'resource.vendorCode',
  'env.now', 'env.time', 'env.day', 'env.date', 'env.isHoliday', 'env.network', 'env.device', 'env.channel', 'env.ip',
];

const EFFECT_HELP: Record<PolicyEffect, string> = {
  Restrict: 'Conditions MUST hold, otherwise the request is denied. Use for office hours, own-store-only, MFA required…',
  Deny: 'If the conditions hold, the request is denied. Use for limits and blocks: refund > ₹10,000, export on unmanaged device…',
  Grant: 'If the conditions hold, access is granted even without a role permission (still subject to Restrict/Deny). Use for owner / named-approver access.',
};
const EFFECT_KIND = { Restrict: 'info', Deny: 'bad', Grant: 'ok' } as const;

let ci = 0;
const c = (attribute: string, operator: Operator, value: string, valueIsAttribute = false): Condition => ({ id: `nc-${Date.now()}-${++ci}`, attribute, operator, value, valueIsAttribute });

const TEMPLATES: { label: string; apply: (scheduleId: string) => Partial<Policy> }[] = [
  { label: 'Office hours only', apply: (s) => ({ effect: 'Restrict', match: 'all', conditions: [c('env.now', 'inSchedule', s)] }) },
  { label: 'Own store only', apply: () => ({ effect: 'Restrict', match: 'all', conditions: [c('resource.store', 'eq', 'user.location', true)] }) },
  { label: 'Same department', apply: () => ({ effect: 'Restrict', match: 'all', conditions: [c('resource.department', 'eq', 'user.department', true)] }) },
  { label: 'Amount limit', apply: () => ({ effect: 'Deny', match: 'all', conditions: [c('resource.amount', 'gt', '50000')] }) },
  { label: 'MFA + office network', apply: () => ({ effect: 'Restrict', match: 'all', conditions: [c('user.mfaEnabled', 'eq', 'true'), c('env.network', 'in', 'Office LAN,VPN')] }) },
  { label: 'Owner can access', apply: () => ({ effect: 'Grant', match: 'all', conditions: [c('resource.ownerEmpCode', 'eq', 'user.empCode', true)] }) },
  { label: 'Block on weekends', apply: () => ({ effect: 'Deny', match: 'all', conditions: [c('env.day', 'in', 'Sat,Sun')] }) },
];

export default function Policies() {
  const { tenantId, upsert } = useStore();
  const d = useTenantData();
  const [q, setQ] = useState('');
  const [effect, setEffect] = useState<'' | PolicyEffect>('');
  const [editing, setEditing] = useState<Policy | null>(null);

  const rows = d.policies
    .filter((p) => (!effect || p.effect === effect) && (p.name + p.description + JSON.stringify(p.target)).toLowerCase().includes(q.toLowerCase()))
    .sort((a, b) => a.priority - b.priority);

  const blank = (): Policy => ({
    id: newId('pol'), tenantId, name: '', description: '', effect: 'Restrict', priority: 100, match: 'all', isActive: true, createdBy: CURRENT_ADMIN, createdAt: new Date().toISOString().slice(0, 19),
    target: { app: '*', module: '*', resource: '*', actions: ['*'], roles: [] }, conditions: [],
  });

  return (
    <>
      <PageHead
        title="ABAC Policies"
        subtitle="Attribute-based rules evaluated after RBAC on every request: who (user.*), what (resource.*), and when / where / how (env.*)."
        actions={
          <button className="btn primary" onClick={() => setEditing(blank())}>
            + New policy
          </button>
        }
      />
      <div className="card">
        <div className="card-head">
          <div className="row grow">
            <input className="search" placeholder="Search policies…" value={q} onChange={(e) => setQ(e.target.value)} />
            <select value={effect} onChange={(e) => setEffect(e.target.value as PolicyEffect | '')}>
              <option value="">All effects</option>
              <option>Restrict</option>
              <option>Deny</option>
              <option>Grant</option>
            </select>
          </div>
          <span className="small muted">Evaluated in priority order · deny-overrides</span>
        </div>
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Prio</th>
                <th>Policy</th>
                <th>Effect</th>
                <th>Applies to</th>
                <th>Rule</th>
                <th>Active</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((p) => (
                <tr key={p.id} className="clickable" onClick={() => setEditing(structuredClone(p))}>
                  <td>{p.priority}</td>
                  <td style={{ maxWidth: 280 }}>
                    <b>{p.name}</b>
                    <div className="small muted">{p.description}</div>
                  </td>
                  <td>
                    <Badge kind={EFFECT_KIND[p.effect]}>{p.effect}</Badge>
                  </td>
                  <td className="small">
                    <code>
                      {p.target.app}.{p.target.module}.{p.target.resource}.{(p.target.actions as string[]).join('|')}
                    </code>
                    {p.target.roles.length > 0 && <div className="muted">roles: {p.target.roles.join(', ')}</div>}
                  </td>
                  <td style={{ maxWidth: 380 }}>
                    <code className="small">{policyExpression(p, d.schedules)}</code>
                  </td>
                  <td onClick={(e) => e.stopPropagation()}>
                    <input
                      type="checkbox"
                      aria-label={`Toggle ${p.name}`}
                      checked={p.isActive}
                      onChange={(e) => upsert('policies', { ...p, isActive: e.target.checked }, `${e.target.checked ? 'Enabled' : 'Disabled'} policy '${p.name}'`)}
                    />
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          {rows.length === 0 && <div className="empty">No policies.</div>}
        </div>
      </div>
      {editing && <PolicyEditor policy={editing} onClose={() => setEditing(null)} />}
    </>
  );
}

function PolicyEditor({ policy: initial, onClose }: { policy: Policy; onClose: () => void }) {
  const { state, upsert, remove, toast } = useStore();
  const d = useTenantData();
  const nav = useNavigate();
  const [p, setP] = useState(initial);
  const isNew = !state.policies.some((x) => x.id === p.id);
  const set = (x: Partial<Policy>) => setP({ ...p, ...x });
  const setTarget = (x: Partial<Policy['target']>) => setP({ ...p, target: { ...p.target, ...x } });
  const setCond = (i: number, x: Partial<Condition>) => set({ conditions: p.conditions.map((cc, j) => (j === i ? { ...cc, ...x } : cc)) });

  const app = d.applications.find((a) => a.code === p.target.app);
  const mod = app?.modules.find((m) => m.code === p.target.module);
  const allActions = (p.target.actions as string[]).includes('*');
  const defaultSchedule = d.schedules[0]?.id ?? '';

  const save = () => {
    if (!p.name.trim()) return toast('Policy name is required', 'error');
    if (p.effect !== 'Restrict' && p.conditions.length === 0) return toast(`${p.effect} policy without conditions would match every request`, 'error');
    if (p.conditions.some((x) => !x.attribute || (!x.value && !x.operator.includes('Schedule')))) return toast('Every condition needs an attribute and value', 'error');
    upsert('policies', p, `${isNew ? 'Created' : 'Updated'} policy '${p.name}' (${p.effect})`);
    toast('Policy saved');
    onClose();
  };

  return (
    <Modal
      wide
      title={isNew ? 'New ABAC policy' : `Policy: ${p.name}`}
      onClose={onClose}
      footer={
        <>
          {!isNew && (
            <button
              className="btn danger"
              onClick={() => {
                if (confirm(`Delete policy '${p.name}'?`)) {
                  remove('policies', p.id);
                  onClose();
                }
              }}
            >
              Delete
            </button>
          )}
          <button
            className="btn"
            onClick={() =>
              nav(`/simulator?app=${p.target.app === '*' ? '' : p.target.app}&module=${p.target.module === '*' ? '' : p.target.module}&resource=${p.target.resource === '*' ? '' : p.target.resource}`)
            }
          >
            ▶ Test in simulator
          </button>
          <div className="grow" />
          <button className="btn" onClick={onClose}>
            Cancel
          </button>
          <button className="btn primary" onClick={save}>
            Save policy
          </button>
        </>
      }
    >
      <div className="grid-3" style={{ marginBottom: 12 }}>
        <div style={{ gridColumn: 'span 2' }}>
          <Field label="Name">
            <input value={p.name} onChange={(e) => set({ name: e.target.value })} placeholder="e.g. Payroll only during office hours" />
          </Field>
        </div>
        <div className="row">
          <Field label="Priority (lower first)">
            <input type="number" value={p.priority} onChange={(e) => set({ priority: Number(e.target.value) })} style={{ width: 110 }} />
          </Field>
          <label className="row small" style={{ marginTop: 18 }}>
            <input type="checkbox" checked={p.isActive} onChange={(e) => set({ isActive: e.target.checked })} /> Active
          </label>
        </div>
      </div>
      <Field label="Description">
        <input value={p.description} onChange={(e) => set({ description: e.target.value })} />
      </Field>

      <h3 style={{ margin: '18px 0 8px' }}>1 · Effect</h3>
      <div className="grid-3">
        {(['Restrict', 'Deny', 'Grant'] as PolicyEffect[]).map((e) => (
          <label key={e} className="card card-body" style={{ cursor: 'pointer', borderColor: p.effect === e ? 'var(--primary)' : undefined }}>
            <div className="row">
              <input type="radio" name="effect" checked={p.effect === e} onChange={() => set({ effect: e })} />
              <Badge kind={EFFECT_KIND[e]}>{e}</Badge>
            </div>
            <div className="small muted" style={{ marginTop: 6 }}>
              {EFFECT_HELP[e]}
            </div>
          </label>
        ))}
      </div>

      <h3 style={{ margin: '18px 0 8px' }}>2 · Target — which requests this policy applies to</h3>
      <div className="grid-4">
        <Field label="Application">
          <select value={p.target.app} onChange={(e) => setTarget({ app: e.target.value, module: '*', resource: '*' })}>
            <option value="*">* All applications</option>
            {d.applications.map((a) => (
              <option key={a.id} value={a.code}>
                {a.name}
              </option>
            ))}
          </select>
        </Field>
        <Field label="Module">
          <select value={p.target.module} disabled={!app} onChange={(e) => setTarget({ module: e.target.value, resource: '*' })}>
            <option value="*">* All modules</option>
            {app?.modules.map((m) => (
              <option key={m.id} value={m.code}>
                {m.name}
              </option>
            ))}
          </select>
        </Field>
        <Field label="Resource">
          <select value={p.target.resource} disabled={!mod} onChange={(e) => setTarget({ resource: e.target.value })}>
            <option value="*">* All resources</option>
            {mod?.resources.map((r) => (
              <option key={r.id} value={r.code}>
                {r.name}
              </option>
            ))}
          </select>
        </Field>
        <Field label="Only for users with role (optional)">
          <select
            value=""
            onChange={(e) => e.target.value && !p.target.roles.includes(e.target.value) && setTarget({ roles: [...p.target.roles, e.target.value] })}
          >
            <option value="">+ add role filter</option>
            {d.roles.map((r) => (
              <option key={r.id} value={r.code}>
                {r.name}
              </option>
            ))}
          </select>
        </Field>
      </div>
      <div className="row" style={{ marginTop: 8 }}>
        <span className="small muted">Actions:</span>
        <label className="row small">
          <input type="checkbox" checked={allActions} onChange={(e) => setTarget({ actions: e.target.checked ? ['*'] : ['View'] })} /> All
        </label>
        {!allActions &&
          ALL_ACTIONS.map((a) => (
            <label key={a} className="row small">
              <input
                type="checkbox"
                checked={(p.target.actions as Action[]).includes(a)}
                onChange={(e) => {
                  const cur = p.target.actions as Action[];
                  const next = e.target.checked ? [...cur, a] : cur.filter((x) => x !== a);
                  setTarget({ actions: next.length ? next : ['*'] });
                }}
              />
              {a}
            </label>
          ))}
        <span className="grow" />
        {p.target.roles.map((r) => (
          <span key={r} className="chip">
            {r}
            <button onClick={() => setTarget({ roles: p.target.roles.filter((x) => x !== r) })}>✕</button>
          </span>
        ))}
        {p.target.roles.length === 0 && <span className="small muted">applies to all users</span>}
      </div>

      <h3 style={{ margin: '18px 0 8px' }}>3 · Conditions</h3>
      <div className="row" style={{ marginBottom: 10 }}>
        <span className="small muted">Templates:</span>
        {TEMPLATES.map((t) => (
          <button key={t.label} className="btn sm" onClick={() => set({ ...t.apply(defaultSchedule) })}>
            {t.label}
          </button>
        ))}
      </div>
      <div className="row small" style={{ marginBottom: 8 }}>
        Match
        <select value={p.match} onChange={(e) => set({ match: e.target.value as Policy['match'] })}>
          <option value="all">ALL conditions (AND)</option>
          <option value="any">ANY condition (OR)</option>
        </select>
      </div>
      <datalist id="attrs">
        {KNOWN_ATTRIBUTES.map((a) => (
          <option key={a} value={a} />
        ))}
      </datalist>
      <div className="stack" style={{ gap: 8 }}>
        {p.conditions.map((cond, i) => {
          const sched = cond.operator === 'inSchedule' || cond.operator === 'notInSchedule';
          return (
            <div className="cond-row" key={cond.id}>
              <input
                list="attrs"
                className="mono"
                value={cond.attribute}
                placeholder="user.department"
                onChange={(e) => {
                  const attribute = e.target.value;
                  setCond(i, attribute === 'env.now' ? { attribute, operator: 'inSchedule', value: defaultSchedule, valueIsAttribute: false } : { attribute });
                }}
              />
              <select value={cond.operator} onChange={(e) => setCond(i, { operator: e.target.value as Operator, ...(e.target.value.includes('Schedule') ? { value: defaultSchedule, valueIsAttribute: false, attribute: 'env.now' } : {}) })}>
                {(Object.keys(OPERATOR_LABELS) as Operator[]).map((o) => (
                  <option key={o} value={o}>
                    {OPERATOR_LABELS[o]}
                  </option>
                ))}
              </select>
              {sched ? (
                <select value={cond.value} onChange={(e) => setCond(i, { value: e.target.value })}>
                  {d.schedules.map((s) => (
                    <option key={s.id} value={s.id}>
                      {s.name}
                    </option>
                  ))}
                </select>
              ) : (
                <input
                  className={cond.valueIsAttribute ? 'mono' : ''}
                  list={cond.valueIsAttribute ? 'attrs' : undefined}
                  value={cond.value}
                  placeholder={cond.valueIsAttribute ? 'user.location' : cond.operator === 'in' || cond.operator === 'notIn' ? 'a,b,c' : 'value'}
                  onChange={(e) => setCond(i, { value: e.target.value })}
                />
              )}
              <label className="row small" title="Compare with another attribute instead of a fixed value">
                <input type="checkbox" disabled={sched} checked={cond.valueIsAttribute} onChange={(e) => setCond(i, { valueIsAttribute: e.target.checked, value: '' })} /> attr
              </label>
              <button className="btn sm ghost danger" onClick={() => set({ conditions: p.conditions.filter((_, j) => j !== i) })} aria-label="Remove condition">
                ✕
              </button>
            </div>
          );
        })}
        <div>
          <button className="btn sm" onClick={() => set({ conditions: [...p.conditions, c('user.department', 'eq', '')] })}>
            + Add condition
          </button>
        </div>
      </div>

      <h3 style={{ margin: '18px 0 8px' }}>Rule expression (stored as Policies.RuleExpression)</h3>
      <div className="expr">
        {p.effect.toUpperCase()} {p.target.app}.{p.target.module}.{p.target.resource}.{(p.target.actions as string[]).join('|')}
        {p.target.roles.length ? ` FOR ROLES [${p.target.roles.join(', ')}]` : ''}
        {'\n'}WHEN {policyExpression(p, d.schedules)}
      </div>
    </Modal>
  );
}

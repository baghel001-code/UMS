import { useMemo, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { Badge, Drawer, Field, KeyValueEditor, Modal, PageHead, StatusBadge, Tabs, downloadCsv, fmtDateTime, usePaged } from '../components/ui';
import { nowLocal, REGIONS } from '../data/seed';
import { isAssignmentActive, permissionKey, permissionMatches } from '../engine/pdp';
import { CURRENT_ADMIN, newId, useStore, useTenantData } from '../store/store';
import type { EmploymentType, RoleAssignment, User, UserStatus } from '../types';

const STATUSES: UserStatus[] = ['Active', 'Inactive', 'Locked'];
const EMP_TYPES: EmploymentType[] = ['Permanent', 'Contract', 'Vendor', 'Intern'];

function blankUser(tenantId: string, n: number): User {
  return {
    id: newId('u'),
    tenantId,
    empCode: `VMM${20000 + n}`,
    fullName: '',
    email: '',
    phone: '',
    department: '',
    designation: '',
    location: '',
    region: 'North',
    employmentType: 'Permanent',
    status: 'Active',
    mfaEnabled: true,
    lastLogin: null,
    accountExpiry: null,
    attributes: {},
    roles: [],
  };
}

export default function Users() {
  const { tenantId, state, setState, audit, toast } = useStore();
  const d = useTenantData();
  const [q, setQ] = useState('');
  const [f, setF] = useState({ dept: '', status: '', role: '', region: '', emp: '' });
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [open, setOpen] = useState<User | null>(null);
  const [bulkRole, setBulkRole] = useState(false);
  const fileRef = useRef<HTMLInputElement>(null);

  const depts = useMemo(() => [...new Set(d.users.map((u) => u.department))].sort(), [d.users]);
  const rows = useMemo(
    () =>
      d.users.filter(
        (u) =>
          (u.fullName + u.email + u.empCode + u.location + u.designation).toLowerCase().includes(q.toLowerCase()) &&
          (!f.dept || u.department === f.dept) &&
          (!f.status || u.status === f.status) &&
          (!f.region || u.region === f.region) &&
          (!f.emp || u.employmentType === f.emp) &&
          (!f.role || u.roles.some((r) => r.roleId === f.role)),
      ),
    [d.users, q, f],
  );
  const paged = usePaged(rows, 20);
  const roleName = (id: string) => d.roles.find((r) => r.id === id)?.code ?? '?';

  const bulkStatus = (status: UserStatus) => {
    const ids = selected;
    setState((s) => ({ ...s, users: s.users.map((u) => (ids.has(u.id) ? { ...u, status } : u)) }));
    audit({ entity: 'User', entityId: [...ids].join(','), action: 'Update', summary: `Bulk set status ${status} on ${ids.size} user(s)`, newValues: { status } });
    toast(`${ids.size} user(s) set to ${status}`);
    setSelected(new Set());
  };

  const importCsv = async (file: File) => {
    const text = await file.text();
    const [head, ...lines] = text.trim().split(/\r?\n/);
    const cols = head.split(',').map((c) => c.trim().replace(/^"|"$/g, ''));
    const need = ['empCode', 'fullName', 'email', 'department', 'designation', 'location', 'region'];
    const missing = need.filter((c) => !cols.includes(c));
    if (missing.length) return toast(`CSV missing columns: ${missing.join(', ')}`, 'error');
    const existing = new Set(state.users.map((u) => u.empCode));
    const employee = d.roles.find((r) => r.code === 'EMPLOYEE');
    const created: User[] = [];
    lines.forEach((line, i) => {
      const vals = line.split(',').map((v) => v.trim().replace(/^"|"$/g, ''));
      const rec = Object.fromEntries(cols.map((c, j) => [c, vals[j] ?? '']));
      if (!rec.empCode || existing.has(rec.empCode)) return;
      created.push({
        ...blankUser(tenantId, i),
        ...rec,
        employmentType: (EMP_TYPES.includes(rec.employmentType as EmploymentType) ? rec.employmentType : 'Permanent') as EmploymentType,
        roles: employee ? [{ roleId: employee.id, scope: {}, validFrom: null, validTo: null, assignedBy: CURRENT_ADMIN, assignedOn: nowLocal().slice(0, 10) }] : [],
      } as User);
    });
    setState((s) => ({ ...s, users: [...s.users, ...created] }));
    audit({ entity: 'User', entityId: 'bulk-import', action: 'Create', summary: `Bulk imported ${created.length} user(s) from ${file.name}` });
    toast(`Imported ${created.length} user(s); ${lines.length - created.length} skipped (duplicate / blank empCode)`);
  };

  return (
    <>
      <PageHead
        title="Users"
        subtitle={`${d.users.length.toLocaleString('en-IN')} identities in ${d.tenant.name}. Click a user to manage roles, attributes and effective access.`}
        actions={
          <>
            <input ref={fileRef} type="file" accept=".csv" hidden onChange={(e) => e.target.files?.[0] && importCsv(e.target.files[0])} />
            <button className="btn" onClick={() => fileRef.current?.click()} title="Columns: empCode,fullName,email,department,designation,location,region[,employmentType]">
              ⇪ Bulk import CSV
            </button>
            <button
              className="btn"
              onClick={() =>
                downloadCsv(
                  'users.csv',
                  rows.map((u) => ({
                    empCode: u.empCode, fullName: u.fullName, email: u.email, department: u.department, designation: u.designation,
                    location: u.location, region: u.region, employmentType: u.employmentType, status: u.status, mfa: u.mfaEnabled,
                    roles: u.roles.map((r) => roleName(r.roleId)).join(' '),
                  })),
                )
              }
            >
              ⇩ Export
            </button>
            <button className="btn primary" onClick={() => setOpen(blankUser(tenantId, d.users.length))}>
              + New user
            </button>
          </>
        }
      />

      <div className="card">
        <div className="card-head" style={{ flexWrap: 'wrap' }}>
          <div className="row grow">
            <input
              className="search"
              placeholder="Search name, email, emp code, store…"
              value={q}
              onChange={(e) => {
                setQ(e.target.value);
                paged.reset();
              }}
            />
            <select value={f.dept} onChange={(e) => setF({ ...f, dept: e.target.value })}>
              <option value="">All departments</option>
              {depts.map((x) => (
                <option key={x}>{x}</option>
              ))}
            </select>
            <select value={f.role} onChange={(e) => setF({ ...f, role: e.target.value })}>
              <option value="">All roles</option>
              {d.roles.map((r) => (
                <option key={r.id} value={r.id}>
                  {r.name}
                </option>
              ))}
            </select>
            <select value={f.region} onChange={(e) => setF({ ...f, region: e.target.value })}>
              <option value="">All regions</option>
              {REGIONS.map((x) => (
                <option key={x}>{x}</option>
              ))}
            </select>
            <select value={f.emp} onChange={(e) => setF({ ...f, emp: e.target.value })}>
              <option value="">All employment types</option>
              {EMP_TYPES.map((x) => (
                <option key={x}>{x}</option>
              ))}
            </select>
            <select value={f.status} onChange={(e) => setF({ ...f, status: e.target.value })}>
              <option value="">All statuses</option>
              {STATUSES.map((x) => (
                <option key={x}>{x}</option>
              ))}
            </select>
          </div>
        </div>
        {selected.size > 0 && (
          <div className="card-head" style={{ background: 'var(--primary-weak)' }}>
            <b>{selected.size} selected</b>
            <div className="row">
              <button className="btn sm" onClick={() => setBulkRole(true)}>
                Assign role…
              </button>
              <button className="btn sm" onClick={() => bulkStatus('Active')}>
                Activate
              </button>
              <button className="btn sm" onClick={() => bulkStatus('Inactive')}>
                Deactivate
              </button>
              <button className="btn sm danger" onClick={() => bulkStatus('Locked')}>
                Lock
              </button>
              <button className="btn sm ghost" onClick={() => setSelected(new Set())}>
                Clear
              </button>
            </div>
          </div>
        )}
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th style={{ width: 32 }}>
                  <input
                    type="checkbox"
                    aria-label="Select page"
                    checked={paged.rows.length > 0 && paged.rows.every((u) => selected.has(u.id))}
                    onChange={(e) => {
                      const n = new Set(selected);
                      paged.rows.forEach((u) => (e.target.checked ? n.add(u.id) : n.delete(u.id)));
                      setSelected(n);
                    }}
                  />
                </th>
                <th>User</th>
                <th>Department / Designation</th>
                <th>Location</th>
                <th>Type</th>
                <th>Roles</th>
                <th>MFA</th>
                <th>Last login</th>
                <th>Status</th>
              </tr>
            </thead>
            <tbody>
              {paged.rows.map((u) => (
                <tr key={u.id} className="clickable" onClick={() => setOpen(structuredClone(u))}>
                  <td onClick={(e) => e.stopPropagation()}>
                    <input
                      type="checkbox"
                      aria-label={`Select ${u.fullName}`}
                      checked={selected.has(u.id)}
                      onChange={(e) => {
                        const n = new Set(selected);
                        if (e.target.checked) n.add(u.id);
                        else n.delete(u.id);
                        setSelected(n);
                      }}
                    />
                  </td>
                  <td>
                    <b>{u.fullName}</b>
                    <div className="small muted">
                      {u.empCode} · {u.email}
                    </div>
                  </td>
                  <td>
                    {u.department}
                    <div className="small muted">{u.designation}</div>
                  </td>
                  <td>
                    {u.location}
                    <div className="small muted">{u.region}</div>
                  </td>
                  <td>
                    <Badge kind={u.employmentType === 'Permanent' ? undefined : 'warn'}>{u.employmentType}</Badge>
                  </td>
                  <td>
                    {u.roles
                      .filter((r) => roleName(r.roleId) !== 'EMPLOYEE')
                      .map((r, i) => (
                        <span key={i} className="chip" style={isAssignmentActive(r, new Date(nowLocal())) ? undefined : { opacity: 0.5, textDecoration: 'line-through' }}>
                          {roleName(r.roleId)}
                        </span>
                      ))}
                  </td>
                  <td>{u.mfaEnabled ? <Badge kind="ok">On</Badge> : <Badge kind="warn">Off</Badge>}</td>
                  <td className="small muted">{fmtDateTime(u.lastLogin)}</td>
                  <td>
                    <StatusBadge status={u.status} />
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          {rows.length === 0 && <div className="empty">No users match the filters.</div>}
        </div>
        {paged.pager}
      </div>

      {open && <UserDrawer user={open} onClose={() => setOpen(null)} />}
      {bulkRole && (
        <AssignRoleModal
          onClose={() => setBulkRole(false)}
          onAssign={(a) => {
            const ids = selected;
            setState((s) => ({ ...s, users: s.users.map((u) => (ids.has(u.id) && !u.roles.some((r) => r.roleId === a.roleId) ? { ...u, roles: [...u.roles, a] } : u)) }));
            audit({ entity: 'User', entityId: [...ids].join(','), action: 'Assign', summary: `Bulk assigned ${roleName(a.roleId)} to ${ids.size} user(s)`, newValues: a });
            toast(`Assigned ${roleName(a.roleId)} to ${ids.size} user(s)`);
            setSelected(new Set());
            setBulkRole(false);
          }}
        />
      )}
    </>
  );
}

function AssignRoleModal({ onClose, onAssign, exclude = [] }: { onClose: () => void; onAssign: (a: RoleAssignment) => void; exclude?: string[] }) {
  const d = useTenantData();
  const options = d.roles.filter((r) => r.isActive && !exclude.includes(r.id));
  const [roleId, setRoleId] = useState(options[0]?.id ?? '');
  const [scope, setScope] = useState<Record<string, string>>({});
  const [validFrom, setValidFrom] = useState('');
  const [validTo, setValidTo] = useState('');
  const role = d.roles.find((r) => r.id === roleId);
  return (
    <Modal
      title="Assign role"
      onClose={onClose}
      footer={
        <>
          <button className="btn" onClick={onClose}>
            Cancel
          </button>
          <button
            className="btn primary"
            disabled={!roleId || (!!validFrom && !!validTo && validTo < validFrom)}
            onClick={() => onAssign({ roleId, scope, validFrom: validFrom || null, validTo: validTo || null, assignedBy: CURRENT_ADMIN, assignedOn: nowLocal().slice(0, 10) })}
          >
            Assign
          </button>
        </>
      }
    >
      <div className="stack">
        <Field label="Role">
          <select value={roleId} onChange={(e) => setRoleId(e.target.value)}>
            {options.map((r) => (
              <option key={r.id} value={r.id}>
                {r.name} ({r.code})
              </option>
            ))}
          </select>
        </Field>
        {role && <p className="small muted" style={{ margin: 0 }}>{role.description}</p>}
        <div className="grid-2">
          <Field label="Valid from (optional)" hint="Leave blank for immediate">
            <input type="date" value={validFrom} onChange={(e) => setValidFrom(e.target.value)} />
          </Field>
          <Field label="Valid to (optional)" hint="Temporary / acting access expires automatically">
            <input type="date" value={validTo} onChange={(e) => setValidTo(e.target.value)} />
          </Field>
        </div>
        <Field label="Scope (resource-based restriction)" hint="e.g. store = S045,S046 → the role only works on resources whose 'store' attribute is S045 or S046.">
          <KeyValueEditor value={scope} onChange={setScope} keyPlaceholder="store / region / dc" valuePlaceholder="S045,S046" />
        </Field>
      </div>
    </Modal>
  );
}

function UserDrawer({ user: initial, onClose }: { user: User; onClose: () => void }) {
  const { state, upsert, audit, toast } = useStore();
  const d = useTenantData();
  const nav = useNavigate();
  const [u, setU] = useState(initial);
  const [tab, setTab] = useState<'Profile' | 'Roles' | 'Attributes' | 'Effective access' | 'Activity'>('Profile');
  const [assigning, setAssigning] = useState(false);
  const isNew = !state.users.some((x) => x.id === u.id);
  const set = (p: Partial<User>) => setU({ ...u, ...p });
  const now = new Date(nowLocal());

  const save = () => {
    if (!u.fullName.trim() || !/^\S+@\S+\.\S+$/.test(u.email)) return toast('Name and a valid email are required', 'error');
    if (state.users.some((x) => x.empCode === u.empCode && x.id !== u.id)) return toast('Employee code already exists', 'error');
    const before = state.users.find((x) => x.id === u.id);
    const code = (a: RoleAssignment) => d.roles.find((r) => r.id === a.roleId)?.code ?? a.roleId;
    const added = u.roles.filter((a) => !before?.roles.some((b) => b.roleId === a.roleId)).map(code);
    const revoked = (before?.roles ?? []).filter((b) => !u.roles.some((a) => a.roleId === b.roleId)).map(code);
    const parts = [added.length && `assigned ${added.join(', ')}`, revoked.length && `revoked ${revoked.join(', ')}`].filter(Boolean);
    upsert('users', u, `${before ? 'Updated' : 'Created'} user ${u.fullName}${parts.length ? ` — ${parts.join('; ')}` : ''}`);
    toast(`Saved ${u.fullName}`);
    onClose();
  };

  const effective = useMemo(() => {
    const active = u.roles.filter((a) => isAssignmentActive(a, now)).map((a) => ({ a, role: d.roles.find((r) => r.id === a.roleId) })).filter((x) => x.role?.isActive);
    return d.applications
      .map((app) => ({
        app,
        modules: app.modules
          .map((m) => ({
            m,
            resources: m.resources
              .map((r) => ({
                r,
                actions: r.actions
                  .map((act) => {
                    const k = permissionKey(app.code, m.code, r.code, act);
                    const via = active.filter((x) => x.role!.permissions.some((p) => permissionMatches(p, k)));
                    return { act, via };
                  })
                  .filter((x) => x.via.length > 0),
              }))
              .filter((x) => x.actions.length > 0),
          }))
          .filter((x) => x.resources.length > 0),
      }))
      .filter((x) => x.modules.length > 0);
  }, [u.roles, d.roles, d.applications]);

  const activity = d.actionLogs.filter((a) => a.userId === u.id).slice(0, 30);
  const changes = d.auditLogs.filter((a) => a.entityId === u.id || a.entityId.split(',').includes(u.id)).slice(0, 30);

  return (
    <Drawer
      title={isNew ? 'New user' : `${u.fullName} · ${u.empCode}`}
      onClose={onClose}
      footer={
        <>
          {!isNew && (
            <button className="btn" onClick={() => nav(`/simulator?user=${u.id}`)}>
              ▶ Test access
            </button>
          )}
          <div className="grow" />
          <button className="btn" onClick={onClose}>
            Cancel
          </button>
          <button className="btn primary" onClick={save}>
            Save user
          </button>
        </>
      }
    >
      <Tabs tabs={['Profile', 'Roles', 'Attributes', 'Effective access', 'Activity'] as const} value={tab} onChange={setTab} />

      {tab === 'Profile' && (
        <div className="stack">
          <div className="grid-2">
            <Field label="Full name">
              <input value={u.fullName} onChange={(e) => set({ fullName: e.target.value })} />
            </Field>
            <Field label="Employee code">
              <input value={u.empCode} onChange={(e) => set({ empCode: e.target.value })} />
            </Field>
            <Field label="Email (login)">
              <input value={u.email} onChange={(e) => set({ email: e.target.value })} />
            </Field>
            <Field label="Phone">
              <input value={u.phone} onChange={(e) => set({ phone: e.target.value })} />
            </Field>
            <Field label="Department">
              <input value={u.department} onChange={(e) => set({ department: e.target.value })} list="depts" />
            </Field>
            <Field label="Designation">
              <input value={u.designation} onChange={(e) => set({ designation: e.target.value })} />
            </Field>
            <Field label="Location (store / DC / office)" hint="Used by ABAC as user.location">
              <input value={u.location} onChange={(e) => set({ location: e.target.value.toUpperCase() })} />
            </Field>
            <Field label="Region">
              <select value={u.region} onChange={(e) => set({ region: e.target.value })}>
                {REGIONS.map((r) => (
                  <option key={r}>{r}</option>
                ))}
              </select>
            </Field>
            <Field label="Employment type">
              <select value={u.employmentType} onChange={(e) => set({ employmentType: e.target.value as EmploymentType })}>
                {EMP_TYPES.map((r) => (
                  <option key={r}>{r}</option>
                ))}
              </select>
            </Field>
            <Field label="Account expiry" hint="Contract / vendor accounts are disabled automatically after this date">
              <input type="date" value={u.accountExpiry ?? ''} onChange={(e) => set({ accountExpiry: e.target.value || null })} />
            </Field>
            <Field label="Status">
              <select value={u.status} onChange={(e) => set({ status: e.target.value as UserStatus })}>
                {STATUSES.map((r) => (
                  <option key={r}>{r}</option>
                ))}
              </select>
            </Field>
            <Field label="Multi-factor authentication">
              <label className="row">
                <input type="checkbox" checked={u.mfaEnabled} onChange={(e) => set({ mfaEnabled: e.target.checked })} /> MFA enabled
              </label>
            </Field>
          </div>
          <datalist id="depts">
            {[...new Set(d.users.map((x) => x.department))].map((x) => (
              <option key={x} value={x} />
            ))}
          </datalist>
          {!isNew && (
            <div className="row">
              <button
                className="btn"
                onClick={() => {
                  audit({ entity: 'User', entityId: u.id, action: 'Reset', summary: `Password reset link sent to ${u.email}` });
                  toast('Password reset link sent');
                }}
              >
                Send password reset
              </button>
              <button
                className="btn"
                onClick={() => {
                  audit({ entity: 'User', entityId: u.id, action: 'Reset', summary: `Revoked all sessions / refresh tokens of ${u.fullName}` });
                  toast('All sessions revoked');
                }}
              >
                Revoke sessions
              </button>
              {u.status === 'Locked' && (
                <button className="btn" onClick={() => set({ status: 'Active' })}>
                  Unlock
                </button>
              )}
            </div>
          )}
        </div>
      )}

      {tab === 'Roles' && (
        <>
          <div className="row" style={{ marginBottom: 10 }}>
            <span className="grow muted small">Roles give permissions. Scope and validity narrow where and when the role works.</span>
            <button className="btn primary sm" onClick={() => setAssigning(true)}>
              + Assign role
            </button>
          </div>
          <div className="table-wrap card">
            <table>
              <thead>
                <tr>
                  <th>Role</th>
                  <th>Scope</th>
                  <th>Validity</th>
                  <th>State</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                {u.roles.map((a, i) => {
                  const role = d.roles.find((r) => r.id === a.roleId);
                  const live = isAssignmentActive(a, now);
                  return (
                    <tr key={i}>
                      <td>
                        <b>{role?.name ?? a.roleId}</b>
                        <div className="mono small muted">{role?.code}</div>
                      </td>
                      <td>
                        {Object.keys(a.scope).length === 0 ? (
                          <span className="muted small">Unscoped</span>
                        ) : (
                          Object.entries(a.scope).map(([k, v]) => (
                            <span key={k} className="chip">
                              {k}={v}
                            </span>
                          ))
                        )}
                      </td>
                      <td className="small">{a.validFrom || a.validTo ? `${a.validFrom ?? '…'} → ${a.validTo ?? '…'}` : 'Permanent'}</td>
                      <td>{live ? <Badge kind="ok">In effect</Badge> : <Badge kind="warn">{a.validFrom && a.validFrom > nowLocal().slice(0, 10) ? 'Scheduled' : 'Expired'}</Badge>}</td>
                      <td>
                        <button
                          className="btn sm danger"
                          onClick={() => set({ roles: u.roles.filter((_, j) => j !== i) })}
                        >
                          Revoke
                        </button>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
            {u.roles.length === 0 && <div className="empty">No roles assigned — the user cannot open any application.</div>}
          </div>
          {assigning && (
            <AssignRoleModal
              exclude={u.roles.map((r) => r.roleId)}
              onClose={() => setAssigning(false)}
              onAssign={(a) => {
                set({ roles: [...u.roles, a] });
                setAssigning(false);
              }}
            />
          )}
        </>
      )}

      {tab === 'Attributes' && (
        <>
          <p className="muted small" style={{ marginTop: 0 }}>
            Custom subject attributes (UserAttributes table) available to ABAC policies as <code>user.&lt;key&gt;</code>. Built-in attributes: department, designation, location,
            region, employmentType, empCode, mfaEnabled, roles.
          </p>
          <KeyValueEditor value={u.attributes} onChange={(attributes) => set({ attributes })} keyPlaceholder="e.g. costCenter, clearance, vendorCode" />
        </>
      )}

      {tab === 'Effective access' && (
        <>
          <p className="muted small" style={{ marginTop: 0 }}>
            What the user's active roles grant today (RBAC). ABAC policies such as office hours may still deny at runtime — use <b>Test access</b> to evaluate a concrete request.
          </p>
          {effective.length === 0 && <div className="empty">No effective permissions.</div>}
          <div className="stack">
            {effective.map(({ app, modules }) => (
              <details key={app.id} className="card" open={effective.length <= 3}>
                <summary className="card-head" style={{ cursor: 'pointer' }}>
                  <b>{app.name}</b>
                  <span className="muted small">{modules.length} module(s)</span>
                </summary>
                <div className="card-body">
                  {modules.map(({ m, resources }) => (
                    <div key={m.id} style={{ marginBottom: 10 }}>
                      <div className="small" style={{ fontWeight: 600 }}>
                        {m.name}
                      </div>
                      {resources.map(({ r, actions }) => (
                        <div key={r.id} className="row small" style={{ paddingLeft: 14 }}>
                          <span style={{ width: 170 }}>{r.name}</span>
                          {actions.map(({ act, via }) => (
                            <span key={act} className="chip" title={`via ${via.map((x) => x.role!.code).join(', ')}`}>
                              {act}
                            </span>
                          ))}
                        </div>
                      ))}
                    </div>
                  ))}
                </div>
              </details>
            ))}
          </div>
        </>
      )}

      {tab === 'Activity' && (
        <div className="stack">
          <h3>Recent access decisions</h3>
          <div className="table-wrap card">
            <table>
              <tbody>
                {activity.map((a) => (
                  <tr key={a.id}>
                    <td className="small muted">{fmtDateTime(a.timestamp)}</td>
                    <td className="mono small">{a.action}</td>
                    <td>
                      <StatusBadge status={a.decision} />
                    </td>
                    <td className="small">{a.reason}</td>
                  </tr>
                ))}
              </tbody>
            </table>
            {activity.length === 0 && <div className="empty">No recorded activity.</div>}
          </div>
          <h3>Admin changes to this user</h3>
          <div className="table-wrap card">
            <table>
              <tbody>
                {changes.map((a) => (
                  <tr key={a.id}>
                    <td className="small muted">{fmtDateTime(a.timestamp)}</td>
                    <td>{a.actor}</td>
                    <td>{a.summary}</td>
                  </tr>
                ))}
              </tbody>
            </table>
            {changes.length === 0 && <div className="empty">No changes recorded.</div>}
          </div>
        </div>
      )}
    </Drawer>
  );
}

import { useMemo, useState } from 'react';
import { Badge, Field, Modal, PageHead, StatusBadge, Tabs } from '../components/ui';
import { permissionCovers, permissionKey, permissionMatches } from '../engine/pdp';
import { newId, useStore, useTenantData } from '../store/store';
import { ALL_ACTIONS, type Application, type Role } from '../types';

type Coverage = 'full' | 'partial' | 'none';

export function appCoverage(role: Role, app: Application): Coverage {
  const all = app.modules.flatMap((m) => m.resources.flatMap((r) => r.actions.map((a) => permissionKey(app.code, m.code, r.code, a))));
  const granted = all.filter((k) => role.permissions.some((p) => permissionMatches(p, k))).length;
  return granted === 0 ? 'none' : granted === all.length ? 'full' : 'partial';
}

export default function Roles() {
  const { tenantId } = useStore();
  const d = useTenantData();
  const [tab, setTab] = useState<'Roles' | 'Role × Application matrix'>('Roles');
  const [q, setQ] = useState('');
  const [appFilter, setAppFilter] = useState('');
  const [editing, setEditing] = useState<Role | null>(null);

  const members = (r: Role) => d.users.filter((u) => u.roles.some((a) => a.roleId === r.id)).length;
  const rows = d.roles.filter(
    (r) => (r.name + r.code + r.description).toLowerCase().includes(q.toLowerCase()) && (!appFilter || (appFilter === 'cross' ? r.appId === null : r.appId === appFilter)),
  );

  return (
    <>
      <PageHead
        title="Roles & Permissions"
        subtitle="A role is a named bundle of permissions (APP.MODULE.RESOURCE.ACTION). Assign roles to users, optionally scoped and time-bound."
        actions={
          <button
            className="btn primary"
            onClick={() => setEditing({ id: newId('role'), tenantId, code: '', name: '', description: '', appId: null, permissions: [], isSystem: false, isActive: true })}
          >
            + New role
          </button>
        }
      />
      <Tabs tabs={['Roles', 'Role × Application matrix'] as const} value={tab} onChange={setTab} />

      {tab === 'Roles' ? (
        <div className="card">
          <div className="card-head">
            <div className="row grow">
              <input className="search" placeholder="Search roles…" value={q} onChange={(e) => setQ(e.target.value)} />
              <select value={appFilter} onChange={(e) => setAppFilter(e.target.value)}>
                <option value="">All applications</option>
                <option value="cross">Cross-application roles</option>
                {d.applications.map((a) => (
                  <option key={a.id} value={a.id}>
                    {a.name}
                  </option>
                ))}
              </select>
            </div>
          </div>
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Role</th>
                  <th>Application</th>
                  <th>Description</th>
                  <th>Permissions</th>
                  <th>Members</th>
                  <th>Status</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((r) => (
                  <tr key={r.id} className="clickable" onClick={() => setEditing(structuredClone(r))}>
                    <td>
                      <b>{r.name}</b> {r.isSystem && <Badge kind="info">system</Badge>}
                      <div className="mono muted">{r.code}</div>
                    </td>
                    <td>{r.appId ? d.applications.find((a) => a.id === r.appId)?.name ?? r.appId : <Badge>Cross-application</Badge>}</td>
                    <td className="muted">{r.description}</td>
                    <td>{r.permissions.length} keys</td>
                    <td>{members(r)}</td>
                    <td>
                      <StatusBadge status={r.isActive ? 'Active' : 'Inactive'} />
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
            {rows.length === 0 && <div className="empty">No roles match.</div>}
          </div>
        </div>
      ) : (
        <div className="card">
          <div className="card-head">
            <span className="muted small">
              <Badge kind="ok">Full</Badge> every permission · <Badge kind="info">Partial</Badge> some permissions · blank = no access. Click a role to edit.
            </span>
          </div>
          <div className="table-wrap">
            <table className="matrix">
              <thead>
                <tr>
                  <th>Role</th>
                  {d.applications.map((a) => (
                    <th key={a.id} className="act" title={a.name}>
                      {a.code}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {d.roles.map((r) => (
                  <tr key={r.id} className="clickable" onClick={() => setEditing(structuredClone(r))}>
                    <td style={{ whiteSpace: 'nowrap' }}>{r.name}</td>
                    {d.applications.map((a) => {
                      const c = appCoverage(r, a);
                      return (
                        <td key={a.id} className="act">
                          {c === 'full' ? <Badge kind="ok">Full</Badge> : c === 'partial' ? <Badge kind="info">Partial</Badge> : ''}
                        </td>
                      );
                    })}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
      {editing && <RoleEditor role={editing} onClose={() => setEditing(null)} />}
    </>
  );
}

function RoleEditor({ role: initial, onClose }: { role: Role; onClose: () => void }) {
  const { state, upsert, remove, toast } = useStore();
  const d = useTenantData();
  const [role, setRole] = useState(initial);
  const [tab, setTab] = useState<'Permissions' | 'Members' | 'Advanced'>('Permissions');
  const [appCode, setAppCode] = useState(
    () =>
      (d.applications.find((a) => a.id === initial.appId) ?? d.applications.find((a) => appCoverage(initial, a) !== 'none') ?? d.applications[0])?.code ?? '',
  );
  const [rawKey, setRawKey] = useState('');
  const isNew = !state.roles.some((r) => r.id === role.id);
  const set = (p: Partial<Role>) => setRole({ ...role, ...p });
  const app = d.applications.find((a) => a.code === appCode);

  const has = (k: string) => role.permissions.some((p) => p.toUpperCase() === k);
  const covered = (k: string) => role.permissions.some((p) => permissionCovers(p, k));
  const toggle = (k: string, on: boolean) => set({ permissions: on ? [...role.permissions, k] : role.permissions.filter((p) => p.toUpperCase() !== k) });

  const memberList = useMemo(() => d.users.filter((u) => u.roles.some((a) => a.roleId === role.id)), [d.users, role.id]);

  const save = () => {
    if (!/^[A-Z][A-Z0-9_]*$/.test(role.code)) return toast('Role code must be UPPER_CASE', 'error');
    if (d.roles.some((r) => r.code === role.code && r.id !== role.id)) return toast('Role code already exists in this tenant', 'error');
    if (!role.name.trim()) return toast('Name is required', 'error');
    const old = state.roles.find((r) => r.id === role.id);
    const added = role.permissions.filter((p) => !old?.permissions.includes(p));
    const removed = old ? old.permissions.filter((p) => !role.permissions.includes(p)) : [];
    upsert('roles', role, `${isNew ? 'Created' : 'Updated'} role ${role.code}${added.length ? ` +${added.length} permission(s)` : ''}${removed.length ? ` −${removed.length} permission(s)` : ''}`);
    toast(`Saved ${role.name}`);
    onClose();
  };

  return (
    <Modal
      wide
      title={isNew ? 'New role' : `Role: ${role.name}`}
      onClose={onClose}
      footer={
        <>
          {!isNew && !role.isSystem && (
            <button
              className="btn danger"
              disabled={memberList.length > 0}
              title={memberList.length ? 'Remove all members before deleting' : ''}
              onClick={() => {
                if (confirm(`Delete role ${role.code}?`)) {
                  remove('roles', role.id);
                  onClose();
                }
              }}
            >
              Delete
            </button>
          )}
          <div className="grow" />
          <button className="btn" onClick={onClose}>
            Cancel
          </button>
          <button className="btn primary" onClick={save}>
            Save role
          </button>
        </>
      }
    >
      <div className="grid-3" style={{ marginBottom: 12 }}>
        <Field label="Code">
          <input value={role.code} disabled={role.isSystem && !isNew} onChange={(e) => set({ code: e.target.value.toUpperCase().replace(/\s+/g, '_') })} />
        </Field>
        <Field label="Name">
          <input value={role.name} onChange={(e) => set({ name: e.target.value })} />
        </Field>
        <Field label="Belongs to">
          <select value={role.appId ?? ''} onChange={(e) => set({ appId: e.target.value || null })}>
            <option value="">Cross-application</option>
            {d.applications.map((a) => (
              <option key={a.id} value={a.id}>
                {a.name}
              </option>
            ))}
          </select>
        </Field>
      </div>
      <div className="row" style={{ marginBottom: 12 }}>
        <div className="grow">
          <Field label="Description">
            <input value={role.description} onChange={(e) => set({ description: e.target.value })} />
          </Field>
        </div>
        <label className="row small" style={{ marginTop: 18 }}>
          <input type="checkbox" checked={role.isActive} onChange={(e) => set({ isActive: e.target.checked })} /> Active
        </label>
      </div>

      <Tabs tabs={['Permissions', 'Members', 'Advanced'] as const} value={tab} onChange={setTab} />

      {tab === 'Permissions' && (
        <>
          <div className="row" style={{ marginBottom: 10 }}>
            <select value={appCode} onChange={(e) => setAppCode(e.target.value)}>
              {d.applications.map((a) => (
                <option key={a.id} value={a.code}>
                  {a.name} ({appCoverage(role, a)})
                </option>
              ))}
            </select>
            {app && (
              <label className="row small">
                <input type="checkbox" checked={has(`${app.code}.*.*.*`)} onChange={(e) => toggle(`${app.code}.*.*.*`, e.target.checked)} />
                Full access to <b>{app.name}</b> (<code>{app.code}.*.*.*</code>)
              </label>
            )}
          </div>
          {app && (
            <div className="table-wrap card">
              <table className="matrix">
                <thead>
                  <tr>
                    <th>Module / Resource</th>
                    {ALL_ACTIONS.map((a) => (
                      <th key={a} className="act">
                        {a}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {app.modules.map((m) => {
                    const modKey = permissionKey(app.code, m.code, '*', '*');
                    return [
                      <tr key={m.id} className="mod">
                        <td>
                          <label className="row">
                            <input type="checkbox" checked={covered(modKey)} disabled={covered(modKey) && !has(modKey)} onChange={(e) => toggle(modKey, e.target.checked)} />
                            {m.name} <span className="mono muted small">{m.code}.*.*</span>
                          </label>
                        </td>
                        {ALL_ACTIONS.map((a) => {
                          const k = permissionKey(app.code, m.code, '*', a);
                          const applicable = m.resources.some((r) => r.actions.includes(a));
                          return (
                            <td key={a} className={`act ${applicable ? '' : 'na'}`}>
                              {applicable && (
                                <input
                                  type="checkbox"
                                  title={`All ${m.name} resources: ${a}`}
                                  className={has(k) ? '' : 'inherited'}
                                  checked={covered(k)}
                                  disabled={covered(k) && !has(k)}
                                  onChange={(e) => toggle(k, e.target.checked)}
                                />
                              )}
                            </td>
                          );
                        })}
                      </tr>,
                      ...m.resources.map((r) => (
                        <tr key={r.id}>
                          <td style={{ paddingLeft: 32 }}>
                            {r.name} <span className="mono muted small">{r.code}</span>
                          </td>
                          {ALL_ACTIONS.map((a) => {
                            const k = permissionKey(app.code, m.code, r.code, a);
                            if (!r.actions.includes(a)) return <td key={a} className="act na" />;
                            const explicit = has(k);
                            const isCovered = covered(k);
                            return (
                              <td key={a} className="act">
                                <input
                                  type="checkbox"
                                  aria-label={k}
                                  title={isCovered && !explicit ? `Granted via ${role.permissions.find((p) => permissionCovers(p, k))}` : k}
                                  className={explicit ? '' : 'inherited'}
                                  checked={isCovered}
                                  disabled={isCovered && !explicit}
                                  onChange={(e) => toggle(k, e.target.checked)}
                                />
                              </td>
                            );
                          })}
                        </tr>
                      )),
                    ];
                  })}
                </tbody>
              </table>
            </div>
          )}
          <p className="small muted">Light checkboxes are inherited from a wildcard grant (module / app / cross-app). Untick the wildcard to edit them individually.</p>
        </>
      )}

      {tab === 'Members' && (
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>User</th>
                <th>Location</th>
                <th>Scope</th>
                <th>Validity</th>
              </tr>
            </thead>
            <tbody>
              {memberList.slice(0, 200).map((u) => {
                const a = u.roles.find((x) => x.roleId === role.id)!;
                return (
                  <tr key={u.id}>
                    <td>
                      {u.fullName} <span className="muted small">{u.empCode}</span>
                    </td>
                    <td>{u.location}</td>
                    <td>{Object.entries(a.scope).map(([k, v]) => <span key={k} className="chip">{k}={v}</span>)}</td>
                    <td className="small">{a.validFrom || a.validTo ? `${a.validFrom ?? '…'} → ${a.validTo ?? '…'}` : 'Permanent'}</td>
                  </tr>
                );
              })}
            </tbody>
          </table>
          {memberList.length === 0 && <div className="empty">No users hold this role. Assign it from the Users page.</div>}
          {memberList.length > 200 && <p className="muted small">Showing first 200 of {memberList.length}.</p>}
        </div>
      )}

      {tab === 'Advanced' && (
        <>
          <p className="muted small" style={{ marginTop: 0 }}>
            Raw permission keys stored for this role. Use <code>*</code> in any segment, e.g. <code>*.*.*.VIEW</code> for read-only on everything.
          </p>
          <div style={{ marginBottom: 10 }}>
            {role.permissions.map((p) => (
              <span key={p} className="chip mono">
                {p}
                <button onClick={() => toggle(p.toUpperCase(), false)} aria-label={`Remove ${p}`}>
                  ✕
                </button>
              </span>
            ))}
            {role.permissions.length === 0 && <span className="muted">No permissions.</span>}
          </div>
          <div className="row">
            <input className="mono grow" placeholder="APP.MODULE.RESOURCE.ACTION" value={rawKey} onChange={(e) => setRawKey(e.target.value.toUpperCase())} />
            <button
              className="btn"
              onClick={() => {
                if (!/^[A-Z0-9_*-]+\.[A-Z0-9_*-]+\.[A-Z0-9_*-]+\.[A-Z*]+$/.test(rawKey)) return toast('Key must have 4 segments: APP.MODULE.RESOURCE.ACTION', 'error');
                if (!has(rawKey)) toggle(rawKey, true);
                setRawKey('');
              }}
            >
              Add key
            </button>
          </div>
        </>
      )}
    </Modal>
  );
}

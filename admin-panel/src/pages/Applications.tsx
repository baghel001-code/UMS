import { useMemo, useState } from 'react';
import { Badge, Field, Modal, PageHead, StatusBadge } from '../components/ui';
import { permissionMatches } from '../engine/pdp';
import { newId, useStore, useTenantData } from '../store/store';
import { ALL_ACTIONS, type Action, type AppType, type Application } from '../types';

const CODE_RE = /^[A-Z][A-Z0-9_-]*$/;

function blankApp(tenantId: string): Application {
  return { id: newId('app'), code: '', name: '', type: 'In-house', vendor: 'VMM IT', ownerDept: '', description: '', isActive: true, tenantIds: [tenantId], modules: [] };
}

export default function Applications() {
  const { tenantId, state } = useStore();
  const d = useTenantData();
  const [q, setQ] = useState('');
  const [type, setType] = useState<'' | AppType>('');
  const [editing, setEditing] = useState<Application | null>(null);
  const [showAll, setShowAll] = useState(false);

  const source = showAll ? state.applications : d.applications;
  const rows = source.filter(
    (a) => (!type || a.type === type) && (a.name + a.code + a.vendor + a.ownerDept).toLowerCase().includes(q.toLowerCase()),
  );

  return (
    <>
      <PageHead
        title="Applications & Modules"
        subtitle="Register every in-house and third-party application, then describe its modules, resources and the actions allowed on each resource."
        actions={
          <button className="btn primary" onClick={() => setEditing(blankApp(tenantId))}>
            + Register application
          </button>
        }
      />
      <div className="card">
        <div className="card-head">
          <div className="row grow">
            <input className="search" placeholder="Search application, code, vendor…" value={q} onChange={(e) => setQ(e.target.value)} />
            <select value={type} onChange={(e) => setType(e.target.value as AppType | '')}>
              <option value="">All types</option>
              <option>In-house</option>
              <option>Third-party</option>
            </select>
            <label className="row small">
              <input type="checkbox" checked={showAll} onChange={(e) => setShowAll(e.target.checked)} /> Include apps not enabled for this tenant
            </label>
          </div>
        </div>
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Application</th>
                <th>Type</th>
                <th>Owner</th>
                <th>Modules</th>
                <th>Resources</th>
                <th>Permissions</th>
                <th>Roles</th>
                <th>Status</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((a) => {
                const resources = a.modules.reduce((s, m) => s + m.resources.length, 0);
                const perms = a.modules.reduce((s, m) => s + m.resources.reduce((r, x) => r + x.actions.length, 0), 0);
                const roles = d.roles.filter((r) => r.permissions.some((p) => permissionMatches(p, `${a.code}.*.*.*`))).length;
                return (
                  <tr key={a.id} className="clickable" onClick={() => setEditing(structuredClone(a))}>
                    <td>
                      <b>{a.name}</b>
                      <div className="mono muted">{a.code}</div>
                    </td>
                    <td>
                      <Badge kind={a.type === 'In-house' ? 'info' : 'warn'}>{a.type}</Badge>
                      <div className="small muted">{a.vendor}</div>
                    </td>
                    <td>{a.ownerDept}</td>
                    <td>{a.modules.length}</td>
                    <td>{resources}</td>
                    <td>{perms}</td>
                    <td>{roles}</td>
                    <td>
                      <StatusBadge status={a.isActive ? (a.tenantIds.includes(tenantId) ? 'Active' : 'Disabled') : 'Inactive'} />
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
          {rows.length === 0 && <div className="empty">No applications match.</div>}
        </div>
      </div>
      {editing && <AppEditor app={editing} onClose={() => setEditing(null)} />}
    </>
  );
}

function AppEditor({ app: initial, onClose }: { app: Application; onClose: () => void }) {
  const { state, upsert, remove, toast } = useStore();
  const [app, setApp] = useState(initial);
  const [newModule, setNewModule] = useState({ code: '', name: '' });
  const isNew = !state.applications.some((a) => a.id === app.id);
  const set = (p: Partial<Application>) => setApp({ ...app, ...p });

  const errors = useMemo(() => {
    const e: string[] = [];
    if (!CODE_RE.test(app.code)) e.push('Code must be UPPER_CASE letters, digits, _ or -');
    if (state.applications.some((a) => a.code === app.code && a.id !== app.id)) e.push(`Code ${app.code} already exists`);
    if (!app.name.trim()) e.push('Name is required');
    return e;
  }, [app, state.applications]);

  const updateModule = (mi: number, fn: (m: Application['modules'][number]) => Application['modules'][number]) =>
    set({ modules: app.modules.map((m, i) => (i === mi ? fn(m) : m)) });

  const save = async () => {
    if (errors.length) return toast(errors[0], 'error');
    if (await upsert('applications', app, `${isNew ? 'Registered' : 'Updated'} application ${app.code}`)) {
      toast(`Saved ${app.name}`);
      onClose();
    }
  };

  return (
    <Modal
      wide
      title={isNew ? 'Register application' : `${app.name} (${app.code})`}
      onClose={onClose}
      footer={
        <>
          {!isNew && (
            <button
              className="btn danger"
              onClick={async () => {
                if (confirm(`Delete ${app.name}? Roles referencing ${app.code}.* will no longer grant anything.`) && (await remove('applications', app.id))) onClose();
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
            Save
          </button>
        </>
      }
    >
      <div className="grid-3" style={{ marginBottom: 14 }}>
        <Field label="Code (used in permission keys)">
          <input value={app.code} onChange={(e) => set({ code: e.target.value.toUpperCase().replace(/\s+/g, '_') })} placeholder="POS" />
        </Field>
        <Field label="Name">
          <input value={app.name} onChange={(e) => set({ name: e.target.value })} />
        </Field>
        <Field label="Type">
          <select value={app.type} onChange={(e) => set({ type: e.target.value as AppType })}>
            <option>In-house</option>
            <option>Third-party</option>
          </select>
        </Field>
        <Field label="Vendor / developed by">
          <input value={app.vendor} onChange={(e) => set({ vendor: e.target.value })} />
        </Field>
        <Field label="Business owner (department)">
          <input value={app.ownerDept} onChange={(e) => set({ ownerDept: e.target.value })} />
        </Field>
        <Field label="Status">
          <select value={app.isActive ? 'Active' : 'Inactive'} onChange={(e) => set({ isActive: e.target.value === 'Active' })}>
            <option>Active</option>
            <option>Inactive</option>
          </select>
        </Field>
      </div>
      <Field label="Description">
        <input value={app.description} onChange={(e) => set({ description: e.target.value })} />
      </Field>
      <div style={{ margin: '14px 0' }}>
        <div className="small muted" style={{ marginBottom: 6 }}>
          Enabled for tenants
        </div>
        <div className="row">
          {state.tenants.map((t) => (
            <label key={t.id} className="row small">
              <input
                type="checkbox"
                checked={app.tenantIds.includes(t.id)}
                onChange={(e) => set({ tenantIds: e.target.checked ? [...app.tenantIds, t.id] : app.tenantIds.filter((x) => x !== t.id) })}
              />
              {t.name}
            </label>
          ))}
        </div>
      </div>

      <h3 style={{ margin: '18px 0 8px' }}>Modules → Resources → Actions</h3>
      <p className="muted small" style={{ marginTop: 0 }}>
        Each checked action becomes a grantable permission <code>{app.code || 'APP'}.MODULE.RESOURCE.ACTION</code>.
      </p>
      <div className="stack">
        {app.modules.map((m, mi) => (
          <div className="card" key={m.id}>
            <div className="card-head">
              <div className="row grow">
                <input style={{ width: 140 }} value={m.code} onChange={(e) => updateModule(mi, (x) => ({ ...x, code: e.target.value.toUpperCase() }))} />
                <input className="grow" value={m.name} onChange={(e) => updateModule(mi, (x) => ({ ...x, name: e.target.value }))} />
              </div>
              <button className="btn sm danger" onClick={() => set({ modules: app.modules.filter((_, i) => i !== mi) })}>
                Remove module
              </button>
            </div>
            <div className="table-wrap">
              <table className="matrix">
                <thead>
                  <tr>
                    <th>Resource</th>
                    {ALL_ACTIONS.map((a) => (
                      <th key={a} className="act">
                        {a}
                      </th>
                    ))}
                    <th />
                  </tr>
                </thead>
                <tbody>
                  {m.resources.map((r, ri) => (
                    <tr key={r.id}>
                      <td>
                        <div className="row" style={{ flexWrap: 'nowrap' }}>
                          <input
                            style={{ width: 150 }}
                            className="mono"
                            value={r.code}
                            onChange={(e) =>
                              updateModule(mi, (x) => ({ ...x, resources: x.resources.map((y, j) => (j === ri ? { ...y, code: e.target.value.toUpperCase() } : y)) }))
                            }
                          />
                          <input
                            style={{ width: 160 }}
                            value={r.name}
                            onChange={(e) => updateModule(mi, (x) => ({ ...x, resources: x.resources.map((y, j) => (j === ri ? { ...y, name: e.target.value } : y)) }))}
                          />
                        </div>
                      </td>
                      {ALL_ACTIONS.map((a) => (
                        <td key={a} className="act">
                          <input
                            type="checkbox"
                            aria-label={`${r.code} ${a}`}
                            checked={r.actions.includes(a)}
                            onChange={(e) =>
                              updateModule(mi, (x) => ({
                                ...x,
                                resources: x.resources.map((y, j) =>
                                  j === ri ? { ...y, actions: e.target.checked ? ALL_ACTIONS.filter((z) => y.actions.includes(z) || z === a) : y.actions.filter((z) => z !== a) } : y,
                                ),
                              }))
                            }
                          />
                        </td>
                      ))}
                      <td>
                        <button className="btn sm ghost danger" onClick={() => updateModule(mi, (x) => ({ ...x, resources: x.resources.filter((_, j) => j !== ri) }))}>
                          ✕
                        </button>
                      </td>
                    </tr>
                  ))}
                  <tr>
                    <td colSpan={ALL_ACTIONS.length + 2}>
                      <button
                        className="btn sm"
                        onClick={() =>
                          updateModule(mi, (x) => ({
                            ...x,
                            resources: [...x.resources, { id: newId('res'), code: `RESOURCE_${x.resources.length + 1}`, name: 'New resource', actions: ['View'] as Action[] }],
                          }))
                        }
                      >
                        + Add resource
                      </button>
                    </td>
                  </tr>
                </tbody>
              </table>
            </div>
          </div>
        ))}
        <div className="row">
          <input placeholder="MODULE_CODE" value={newModule.code} onChange={(e) => setNewModule({ ...newModule, code: e.target.value.toUpperCase().replace(/\s+/g, '_') })} />
          <input placeholder="Module name" value={newModule.name} onChange={(e) => setNewModule({ ...newModule, name: e.target.value })} />
          <button
            className="btn"
            onClick={() => {
              if (!CODE_RE.test(newModule.code)) return toast('Module code must be UPPER_CASE', 'error');
              if (app.modules.some((m) => m.code === newModule.code)) return toast('Module code already exists', 'error');
              set({ modules: [...app.modules, { id: newId('mod'), code: newModule.code, name: newModule.name || newModule.code, resources: [] }] });
              setNewModule({ code: '', name: '' });
            }}
          >
            + Add module
          </button>
        </div>
      </div>
    </Modal>
  );
}

import { useState } from 'react';
import { Field, Modal, PageHead, StatusBadge, fmtDate } from '../components/ui';
import { newId, useStore } from '../store/store';
import type { Tenant } from '../types';

export default function Tenants() {
  const { state, upsert, toast, setTenantId } = useStore();
  const [editing, setEditing] = useState<Tenant | null>(null);
  const count = (tenantId: string) => ({
    users: state.users.filter((u) => u.tenantId === tenantId).length,
    roles: state.roles.filter((r) => r.tenantId === tenantId).length,
    policies: state.policies.filter((p) => p.tenantId === tenantId).length,
    apps: state.applications.filter((a) => a.tenantIds.includes(tenantId)).length,
  });

  return (
    <>
      <PageHead
        title="Tenants"
        subtitle="Isolated business units / partner networks. Users, roles, policies, schedules and logs are always scoped to a tenant (TenantId)."
        actions={
          <button className="btn primary" onClick={() => setEditing({ id: newId('t'), code: '', name: '', isActive: true, createdOn: new Date().toISOString().slice(0, 10) })}>
            + New tenant
          </button>
        }
      />
      <div className="card table-wrap">
        <table>
          <thead>
            <tr>
              <th>Tenant</th>
              <th>Users</th>
              <th>Applications</th>
              <th>Roles</th>
              <th>Policies</th>
              <th>Created</th>
              <th>Status</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {state.tenants.map((t) => {
              const c = count(t.id);
              return (
                <tr key={t.id}>
                  <td>
                    <b>{t.name}</b>
                    <div className="mono muted small">{t.code}</div>
                  </td>
                  <td>{c.users}</td>
                  <td>{c.apps}</td>
                  <td>{c.roles}</td>
                  <td>{c.policies}</td>
                  <td>{fmtDate(t.createdOn)}</td>
                  <td>
                    <StatusBadge status={t.isActive ? 'Active' : 'Inactive'} />
                  </td>
                  <td className="right">
                    <div className="row" style={{ justifyContent: 'flex-end' }}>
                      <button className="btn sm" onClick={() => setTenantId(t.id)}>
                        Switch to
                      </button>
                      <button className="btn sm" onClick={() => setEditing({ ...t })}>
                        Edit
                      </button>
                    </div>
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
      {editing && (
        <Modal
          title={editing.name || 'New tenant'}
          onClose={() => setEditing(null)}
          footer={
            <>
              <button className="btn" onClick={() => setEditing(null)}>
                Cancel
              </button>
              <button
                className="btn primary"
                onClick={async () => {
                  if (!editing.code || !editing.name) return toast('Code and name are required', 'error');
                  if (await upsert('tenants', editing)) setEditing(null);
                }}
              >
                Save
              </button>
            </>
          }
        >
          <div className="stack">
            <Field label="Code">
              <input value={editing.code} onChange={(e) => setEditing({ ...editing, code: e.target.value.toUpperCase() })} />
            </Field>
            <Field label="Name">
              <input value={editing.name} onChange={(e) => setEditing({ ...editing, name: e.target.value })} />
            </Field>
            <label className="row">
              <input type="checkbox" checked={editing.isActive} onChange={(e) => setEditing({ ...editing, isActive: e.target.checked })} /> Active (inactive tenants deny every request)
            </label>
          </div>
        </Modal>
      )}
    </>
  );
}

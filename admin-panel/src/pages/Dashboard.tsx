import { Link } from 'react-router-dom';
import { Badge, PageHead, fmtDate, fmtDateTime } from '../components/ui';
import { nowLocal } from '../data/seed';
import { useTenantData } from '../store/store';

const LAYERS = [
  ['1. Tenant', 'Business unit is active'],
  ['2. Identity', 'User active, not locked/expired'],
  ['3. Application', 'Any role grants the app'],
  ['4. Module', 'Role grants the module'],
  ['5. Resource · Action', 'APP.MODULE.RESOURCE.ACTION'],
  ['6. Scope', 'Role scope e.g. store = S045'],
  ['7. ABAC policies', 'Office hours, limits, device, owner…'],
];

export default function Dashboard() {
  const d = useTenantData();
  const now = new Date(nowLocal());
  const in30 = new Date(now);
  in30.setDate(in30.getDate() + 30);
  const day = (x: Date) => x.toISOString().slice(0, 10);

  const active = d.users.filter((u) => u.status === 'Active').length;
  const locked = d.users.filter((u) => u.status === 'Locked').length;
  const noMfa = d.users.filter((u) => u.status === 'Active' && !u.mfaEnabled).length;
  const inHouse = d.applications.filter((a) => a.type === 'In-house').length;
  const permCount = d.applications.reduce((s, a) => s + a.modules.reduce((m, mod) => m + mod.resources.reduce((r, res) => r + res.actions.length, 0), 0), 0);

  const expiring = d.users.filter((u) => u.accountExpiry && u.accountExpiry >= day(now) && u.accountExpiry <= day(in30));
  const tempRoles = d.users.flatMap((u) =>
    u.roles.filter((r) => r.validTo && r.validTo >= day(now) && r.validTo <= day(in30)).map((r) => ({ u, r, role: d.roles.find((x) => x.id === r.roleId) })),
  );

  const byDept = Object.entries(
    d.users.reduce<Record<string, number>>((acc, u) => ((acc[u.department] = (acc[u.department] ?? 0) + 1), acc), {}),
  ).sort((a, b) => b[1] - a[1]);
  const maxDept = Math.max(1, ...byDept.map(([, n]) => n));

  const denies = d.actionLogs.filter((a) => a.decision === 'Deny');
  const denyReasons = Object.entries(
    denies.reduce<Record<string, number>>((acc, a) => ((acc[a.reason] = (acc[a.reason] ?? 0) + 1), acc), {}),
  )
    .sort((a, b) => b[1] - a[1])
    .slice(0, 5);

  const openExceptions = d.exceptionLogs.filter((e) => !e.resolved);

  return (
    <>
      <PageHead title={`Welcome — ${d.tenant.name}`} subtitle="Fine-grained access for every application, module, resource and action." />

      <div className="grid-4" style={{ marginBottom: 16 }}>
        <Link to="/users" className="card stat">
          <div className="label">Users</div>
          <div className="value">{d.users.length.toLocaleString('en-IN')}</div>
          <div className="sub">
            {active} active · {locked} locked
          </div>
        </Link>
        <Link to="/applications" className="card stat">
          <div className="label">Applications</div>
          <div className="value">{d.applications.length}</div>
          <div className="sub">
            {inHouse} in-house · {d.applications.length - inHouse} third-party · {permCount} permissions
          </div>
        </Link>
        <Link to="/roles" className="card stat">
          <div className="label">Roles</div>
          <div className="value">{d.roles.length}</div>
          <div className="sub">{d.roles.filter((r) => r.appId === null).length} cross-application</div>
        </Link>
        <Link to="/policies" className="card stat">
          <div className="label">Active ABAC policies</div>
          <div className="value">{d.policies.filter((p) => p.isActive).length}</div>
          <div className="sub">{d.schedules.length} office-hour schedules</div>
        </Link>
      </div>

      <div className="card" style={{ marginBottom: 16 }}>
        <div className="card-head">
          <h2>How every access request is decided</h2>
          <Link to="/simulator" className="btn sm primary">
            Try the Access Simulator
          </Link>
        </div>
        <div className="card-body">
          <div className="funnel">
            {LAYERS.map(([t, s]) => (
              <div className="lvl" key={t}>
                <b>{t}</b>
                <span>{s}</span>
              </div>
            ))}
          </div>
          <p className="muted small" style={{ marginBottom: 0 }}>
            Any failing layer denies the request (deny-overrides). Grant policies can open access without a role, e.g. the named approver of a PO.
          </p>
        </div>
      </div>

      <div className="grid-3" style={{ marginBottom: 16 }}>
        <div className="card">
          <div className="card-head">
            <h3>Users by department</h3>
          </div>
          <div className="card-body stack" style={{ gap: 8 }}>
            {byDept.map(([dept, n]) => (
              <div key={dept}>
                <div className="row small">
                  <span className="grow">{dept}</span>
                  <b>{n}</b>
                </div>
                <div className="bar">
                  <div style={{ width: `${(n / maxDept) * 100}%` }} />
                </div>
              </div>
            ))}
          </div>
        </div>

        <div className="card">
          <div className="card-head">
            <h3>Top deny reasons (last 48h)</h3>
            <Badge kind="bad">{denies.length} denied</Badge>
          </div>
          <div className="card-body stack" style={{ gap: 10 }}>
            {denyReasons.length === 0 && <span className="muted">No denials</span>}
            {denyReasons.map(([reason, n]) => (
              <div key={reason} className="row small">
                <span className="grow">{reason}</span>
                <Badge>{n}</Badge>
              </div>
            ))}
            <Link to="/actions" className="small">
              Open user action trail →
            </Link>
          </div>
        </div>

        <div className="card">
          <div className="card-head">
            <h3>Needs attention</h3>
          </div>
          <div className="card-body stack" style={{ gap: 10 }}>
            <div className="row">
              <span className="grow">Active users without MFA</span>
              <Badge kind={noMfa ? 'warn' : 'ok'}>{noMfa}</Badge>
            </div>
            <div className="row">
              <span className="grow">Locked accounts</span>
              <Badge kind={locked ? 'bad' : 'ok'}>{locked}</Badge>
            </div>
            <div className="row">
              <span className="grow">Accounts expiring in 30 days</span>
              <Badge kind={expiring.length ? 'warn' : 'ok'}>{expiring.length}</Badge>
            </div>
            <div className="row">
              <span className="grow">Temporary roles ending in 30 days</span>
              <Badge kind={tempRoles.length ? 'warn' : 'ok'}>{tempRoles.length}</Badge>
            </div>
            <div className="row">
              <span className="grow">Unresolved exceptions</span>
              <Badge kind={openExceptions.length ? 'bad' : 'ok'}>{openExceptions.length}</Badge>
            </div>
          </div>
        </div>
      </div>

      <div className="grid-2">
        <div className="card">
          <div className="card-head">
            <h3>Recent admin changes</h3>
            <Link to="/audit" className="small">
              Audit trail →
            </Link>
          </div>
          <div className="table-wrap">
            <table>
              <tbody>
                {d.auditLogs.slice(0, 7).map((a) => (
                  <tr key={a.id}>
                    <td className="small muted" style={{ whiteSpace: 'nowrap' }}>
                      {fmtDateTime(a.timestamp)}
                    </td>
                    <td>
                      <Badge>{a.action}</Badge>
                    </td>
                    <td>{a.summary}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
        <div className="card">
          <div className="card-head">
            <h3>Accounts expiring soon</h3>
          </div>
          <div className="table-wrap">
            <table>
              <tbody>
                {expiring.slice(0, 7).map((u) => (
                  <tr key={u.id}>
                    <td>{u.fullName}</td>
                    <td className="muted">{u.designation}</td>
                    <td>
                      <Badge kind="warn">{fmtDate(u.accountExpiry)}</Badge>
                    </td>
                  </tr>
                ))}
                {expiring.length === 0 && (
                  <tr>
                    <td className="muted">Nothing expiring in the next 30 days.</td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        </div>
      </div>
    </>
  );
}

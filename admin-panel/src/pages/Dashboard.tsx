import { Link, useNavigate } from 'react-router-dom';
import { Badge, fmtDate, fmtDateTime } from '../components/ui';
import { nowLocal } from '../data/seed';
import { useStore, useTenantData } from '../store/store';

const LAYERS: [string, string][] = [
  ['Tenant', 'Business unit is active'],
  ['Identity', 'User active, not locked or expired'],
  ['Application', 'A role opens the app'],
  ['Module', 'A role opens the module'],
  ['Resource · Action', 'APP.MODULE.RESOURCE.ACTION'],
  ['Scope', 'e.g. only store S045'],
  ['Policies', 'Office hours, limits, device, owner'],
];

const pct = (n: number, total: number) => (total === 0 ? 0 : Math.round((n / total) * 100));

function greeting() {
  const h = new Date().getHours();
  return h < 12 ? 'Good morning' : h < 17 ? 'Good afternoon' : 'Good evening';
}

/** One horizontal bar per row; values share one scale so rows compare honestly. */
function BarList({ rows, color = 'blue', max }: { rows: { label: string; value: number; tip?: string }[]; color?: string; max?: number }) {
  const top = Math.max(1, max ?? Math.max(...rows.map((r) => r.value)));
  return (
    <div>
      {rows.map((r) => (
        <div className="hbar" key={r.label}>
          <span title={r.label} style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
            {r.label}
          </span>
          <div className="track">
            <div className={`seg ${color}`} style={{ width: `${(r.value / top) * 100}%`, minWidth: r.value ? 3 : 0 }} data-tip={r.tip ?? `${r.label}: ${r.value}`} />
          </div>
          <b className="right">{r.value.toLocaleString('en-IN')}</b>
        </div>
      ))}
    </div>
  );
}

export default function Dashboard() {
  const { session } = useStore();
  const d = useTenantData();
  const nav = useNavigate();
  const now = new Date(nowLocal());
  const in30 = new Date(now);
  in30.setDate(in30.getDate() + 30);
  const day = (x: Date) => `${x.getFullYear()}-${String(x.getMonth() + 1).padStart(2, '0')}-${String(x.getDate()).padStart(2, '0')}`;

  const active = d.users.filter((u) => u.status === 'Active').length;
  const inactive = d.users.filter((u) => u.status === 'Inactive').length;
  const locked = d.users.filter((u) => u.status === 'Locked').length;
  const noMfa = d.users.filter((u) => u.status === 'Active' && !u.mfaEnabled).length;
  const inHouse = d.applications.filter((a) => a.type === 'In-house').length;
  const permCount = d.applications.reduce((s, a) => s + a.modules.reduce((m, mod) => m + mod.resources.reduce((r, res) => r + res.actions.length, 0), 0), 0);

  const expiring = d.users.filter((u) => u.accountExpiry && u.accountExpiry >= day(now) && u.accountExpiry <= day(in30));
  const nextExpiry = [...expiring].sort((a, b) => a.accountExpiry!.localeCompare(b.accountExpiry!))[0];
  const tempRoles = d.users.flatMap((u) => u.roles.filter((r) => r.validTo && r.validTo >= day(now) && r.validTo <= day(in30)));
  const openExceptions = d.exceptionLogs.filter((e) => !e.resolved);

  const byDept = Object.entries(d.users.reduce<Record<string, number>>((acc, u) => ((acc[u.department] = (acc[u.department] ?? 0) + 1), acc), {}))
    .sort((a, b) => b[1] - a[1])
    .slice(0, 8)
    .map(([label, value]) => ({ label, value }));

  const allowed = d.actionLogs.filter((a) => a.decision === 'Allow').length;
  const denied = d.actionLogs.length - allowed;
  const byApp = Object.entries(
    d.actionLogs.reduce<Record<string, { allow: number; deny: number }>>((acc, a) => {
      const x = (acc[a.app] ??= { allow: 0, deny: 0 });
      if (a.decision === 'Allow') x.allow++;
      else x.deny++;
      return acc;
    }, {}),
  )
    .map(([app, v]) => ({ app, ...v, total: v.allow + v.deny }))
    .sort((a, b) => b.total - a.total)
    .slice(0, 6);
  const maxApp = Math.max(1, ...byApp.map((a) => a.total));

  const denyReasons = Object.entries(
    d.actionLogs.filter((a) => a.decision === 'Deny').reduce<Record<string, number>>((acc, a) => ((acc[a.reason] = (acc[a.reason] ?? 0) + 1), acc), {}),
  )
    .sort((a, b) => b[1] - a[1])
    .slice(0, 4);

  const attention: { label: string; value: number; kind: 'ok' | 'warn' | 'bad'; to: string }[] = [
    { label: 'Active users without MFA', value: noMfa, kind: noMfa ? 'warn' : 'ok', to: '/users' },
    { label: 'Locked accounts', value: locked, kind: locked ? 'bad' : 'ok', to: '/users' },
    { label: 'Accounts expiring in 30 days', value: expiring.length, kind: expiring.length ? 'warn' : 'ok', to: '/users' },
    { label: 'Temporary roles ending in 30 days', value: tempRoles.length, kind: tempRoles.length ? 'warn' : 'ok', to: '/users' },
    { label: 'Unresolved exceptions', value: openExceptions.length, kind: openExceptions.length ? 'bad' : 'ok', to: '/exceptions' },
  ];

  return (
    <>
      <section className="hero">
        <div className="row" style={{ alignItems: 'flex-start' }}>
          <div className="grow">
            <h1>
              {greeting()}, {session?.fullName.split(' ')[0] ?? 'Administrator'}
            </h1>
            <p>
              {d.tenant.name} · {now.toLocaleDateString('en-IN', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' })}
            </p>
          </div>
          <div className="row">
            <span className="hero-badge">{d.applications.length} applications</span>
            <span className="hero-badge">{d.users.length.toLocaleString('en-IN')} users</span>
            <span className="hero-badge">{d.policies.filter((p) => p.isActive).length} active policies</span>
          </div>
        </div>
        <div className="row hero-actions">
          <button className="btn glass" onClick={() => nav('/users?new=1')}>
            ＋ Add user
          </button>
          <button className="btn glass" onClick={() => nav('/roles?new=1')}>
            ＋ New role
          </button>
          <button className="btn glass" onClick={() => nav('/policies?new=1')}>
            ＋ New policy
          </button>
          <button className="btn glass" onClick={() => nav('/simulator')}>
            ▶ Test someone's access
          </button>
        </div>
      </section>

      <div className="grid-4" style={{ marginBottom: 20 }}>
        <Link to="/users" className="card stat">
          <div className="stat-ico">👥</div>
          <div>
            <div className="label">Users</div>
            <div className="value">{d.users.length.toLocaleString('en-IN')}</div>
            <div className="sub">
              {active.toLocaleString('en-IN')} active · {locked} locked
            </div>
          </div>
        </Link>
        <Link to="/applications" className="card stat">
          <div className="stat-ico">▦</div>
          <div>
            <div className="label">Applications</div>
            <div className="value">{d.applications.length}</div>
            <div className="sub">
              {inHouse} in-house · {d.applications.length - inHouse} third-party
            </div>
          </div>
        </Link>
        <Link to="/roles" className="card stat">
          <div className="stat-ico">🛡</div>
          <div>
            <div className="label">Roles</div>
            <div className="value">{d.roles.length}</div>
            <div className="sub">{permCount} grantable permissions</div>
          </div>
        </Link>
        <Link to="/policies" className="card stat">
          <div className="stat-ico">⚖</div>
          <div>
            <div className="label">ABAC policies</div>
            <div className="value">{d.policies.filter((p) => p.isActive).length}</div>
            <div className="sub">{d.schedules.length} office-hour schedules</div>
          </div>
        </Link>
      </div>

      <div className="card" style={{ marginBottom: 20 }}>
        <div className="card-head">
          <div>
            <h2>How every access request is checked</h2>
            <div className="small muted">Any failing step denies the request. Grant policies can open access without a role (e.g. the approver named on a PO).</div>
          </div>
          <Link to="/simulator" className="btn sm primary">
            Try it
          </Link>
        </div>
        <div className="card-body" style={{ paddingTop: 24 }}>
          <div className="steps">
            {LAYERS.map(([t, s]) => (
              <div className="step" key={t}>
                <b>{t}</b>
                <span>{s}</span>
              </div>
            ))}
          </div>
        </div>
      </div>

      <div className="grid-3" style={{ marginBottom: 20 }}>
        <div className="card">
          <div className="card-head">
            <h3>Access decisions</h3>
            <Link to="/actions" className="small">
              View trail →
            </Link>
          </div>
          <div className="card-body">
            <div className="row" style={{ gap: 24 }}>
              <div className="kv">
                <span className="num">{allowed}</span>
                <span className="small muted">✓ allowed</span>
              </div>
              <div className="kv">
                <span className="num">{denied}</span>
                <span className="small muted">✕ denied</span>
              </div>
              <div className="kv">
                <span className="num">{pct(allowed, allowed + denied)}%</span>
                <span className="small muted">allow rate</span>
              </div>
            </div>
            <div className="legend" style={{ margin: '14px 0 10px' }}>
              <span>
                <i style={{ background: 'var(--ok-mark)' }} />✓ Allow
              </span>
              <span>
                <i style={{ background: 'var(--bad-mark)' }} />✕ Deny
              </span>
            </div>
            {byApp.map((a) => (
              <div className="hbar" key={a.app}>
                <span>{a.app}</span>
                <div className="track">
                  <div className="seg ok" style={{ width: `${(a.allow / maxApp) * 100}%` }} data-tip={`${a.app}: ${a.allow} allowed`} />
                  <div className="seg bad" style={{ width: `${(a.deny / maxApp) * 100}%` }} data-tip={`${a.app}: ${a.deny} denied`} />
                </div>
                <b className="right">{a.total}</b>
              </div>
            ))}
            {byApp.length === 0 && <div className="muted small">No decisions recorded yet.</div>}
          </div>
        </div>

        <div className="card">
          <div className="card-head">
            <h3>Users by status</h3>
            <Link to="/users" className="small">
              Manage →
            </Link>
          </div>
          <div className="card-body">
            <div className="big-split" role="img" aria-label={`${active} active, ${inactive} inactive, ${locked} locked`}>
              <div className="seg ok" style={{ flex: active }} data-tip={`Active: ${active}`} />
              <div className="seg warn" style={{ flex: inactive }} data-tip={`Inactive: ${inactive}`} />
              <div className="seg bad" style={{ flex: locked }} data-tip={`Locked: ${locked}`} />
            </div>
            <div className="legend">
              <span>
                <i style={{ background: 'var(--ok-mark)' }} />✓ Active {active} ({pct(active, d.users.length)}%)
              </span>
              <span>
                <i style={{ background: 'var(--warn-mark)' }} />⏸ Inactive {inactive}
              </span>
              <span>
                <i style={{ background: 'var(--bad-mark)' }} />🔒 Locked {locked}
              </span>
            </div>
            <h3 style={{ margin: '20px 0 10px' }}>Top deny reasons</h3>
            <div className="stack" style={{ gap: 8 }}>
              {denyReasons.map(([reason, n]) => (
                <div key={reason} className="row small" style={{ flexWrap: 'nowrap' }}>
                  <span className="grow">{reason}</span>
                  <Badge>{n}</Badge>
                </div>
              ))}
              {denyReasons.length === 0 && <span className="muted small">No denials.</span>}
            </div>
          </div>
        </div>

        <div className="card">
          <div className="card-head">
            <h3>Users by department</h3>
          </div>
          <div className="card-body">
            <BarList rows={byDept} />
          </div>
        </div>
      </div>

      <div className="grid-2">
        <div className="card">
          <div className="card-head">
            <h3>Needs attention</h3>
          </div>
          <div className="card-body stack" style={{ gap: 4 }}>
            {attention.map((a) => (
              <Link key={a.label} to={a.to} className="row" style={{ color: 'inherit', padding: '8px 0', borderBottom: '1px solid var(--border)', flexWrap: 'nowrap' }}>
                <span className="grow">{a.label}</span>
                <Badge kind={a.kind}>
                  {a.kind === 'ok' ? '✓' : a.kind === 'warn' ? '!' : '✕'} {a.value}
                </Badge>
              </Link>
            ))}
            {expiring.length > 0 && (
              <div className="small muted" style={{ marginTop: 8 }}>
                Next expiry: {nextExpiry!.fullName} on {fmtDate(nextExpiry!.accountExpiry)}
              </div>
            )}
          </div>
        </div>
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
                {[...d.auditLogs]
                  .sort((a, b) => b.timestamp.localeCompare(a.timestamp))
                  .slice(0, 7)
                  .map((a) => (
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
            {d.auditLogs.length === 0 && <div className="empty">No changes yet.</div>}
          </div>
        </div>
      </div>
    </>
  );
}

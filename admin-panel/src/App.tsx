import { useState, type ReactNode } from 'react';
import { NavLink, Navigate, Route, Routes, useLocation } from 'react-router-dom';
import { useStore } from './store/store';
import Dashboard from './pages/Dashboard';
import Applications from './pages/Applications';
import Users from './pages/Users';
import Roles from './pages/Roles';
import Policies from './pages/Policies';
import Schedules from './pages/Schedules';
import AccessSimulator from './pages/AccessSimulator';
import { ActionTrail, AuditTrail, ExceptionLogs } from './pages/Logs';
import Tenants from './pages/Tenants';
import Settings from './pages/Settings';

const NAV: { group: string; items: { to: string; label: string; ico: string }[] }[] = [
  { group: 'Overview', items: [{ to: '/dashboard', label: 'Dashboard', ico: '◧' }] },
  {
    group: 'Access control',
    items: [
      { to: '/applications', label: 'Applications & Modules', ico: '▦' },
      { to: '/users', label: 'Users', ico: '👤' },
      { to: '/roles', label: 'Roles & Permissions', ico: '🛡' },
      { to: '/policies', label: 'ABAC Policies', ico: '⚖' },
      { to: '/schedules', label: 'Office Hours & Holidays', ico: '🕘' },
      { to: '/simulator', label: 'Access Simulator', ico: '▶' },
    ],
  },
  {
    group: 'Monitoring',
    items: [
      { to: '/audit', label: 'Audit Trail', ico: '📜' },
      { to: '/actions', label: 'User Action Trail', ico: '🧭' },
      { to: '/exceptions', label: 'Exception Logs', ico: '⚠' },
    ],
  },
  {
    group: 'Administration',
    items: [
      { to: '/tenants', label: 'Tenants', ico: '🏢' },
      { to: '/settings', label: 'Security Settings', ico: '⚙' },
    ],
  },
];

function Splash({ children }: { children: ReactNode }) {
  return <div className="splash">{children}</div>;
}

function Login() {
  const { login } = useStore();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [busy, setBusy] = useState(false);
  return (
    <Splash>
      <form
        className="login-card stack"
        onSubmit={async (e) => {
          e.preventDefault();
          setBusy(true);
          await login(email, password);
          setBusy(false);
        }}
      >
        <div className="brand">
          <div className="brand-logo">V</div>
          <div>
            <b>VMM UMS</b>
            <small>Identity &amp; Access Admin</small>
          </div>
        </div>
        <h2>Sign in</h2>
        <label className="field">
          <span>Email</span>
          <input type="email" autoComplete="username" value={email} onChange={(e) => setEmail(e.target.value)} required autoFocus />
        </label>
        <label className="field">
          <span>Password</span>
          <input type="password" autoComplete="current-password" value={password} onChange={(e) => setPassword(e.target.value)} required />
        </label>
        <button className="btn primary" disabled={busy} style={{ justifyContent: 'center', padding: '10px' }}>
          {busy ? 'Signing in…' : 'Sign in'}
        </button>
        <span className="small muted">Access to this console is itself checked by UMS (UMS.* permissions).</span>
      </form>
    </Splash>
  );
}

export default function App() {
  const { state, tenantId, setTenantId, toasts, reset, mode, status, loadError, reload, logout, session } = useStore();
  const [open, setOpen] = useState(false);
  const loc = useLocation();
  const title = NAV.flatMap((g) => g.items).find((i) => loc.pathname.startsWith(i.to))?.label ?? '';

  const toastLayer = (
    <div className="toasts">
      {toasts.map((t) => (
        <div key={t.id} className={`toast ${t.kind}`} role="status">
          {t.text}
        </div>
      ))}
    </div>
  );

  if (status === 'login')
    return (
      <>
        <Login />
        {toastLayer}
      </>
    );
  if (status === 'loading' || (status === 'ready' && !tenantId))
    return (
      <Splash>
        <div className="splash-msg">Loading access-control data…</div>
      </Splash>
    );
  if (status === 'error')
    return (
      <Splash>
        <div className="login-card stack">
          <h2>Cannot load data</h2>
          <p className="muted" style={{ margin: 0 }}>
            {loadError}
          </p>
          <button className="btn primary" onClick={reload} style={{ justifyContent: 'center' }}>
            Try again
          </button>
        </div>
      </Splash>
    );

  return (
    <div className="layout">
      <aside className={`sidebar ${open ? 'open' : ''}`} onClick={() => setOpen(false)}>
        <div className="brand">
          <div className="brand-logo">V</div>
          <div>
            <b>VMM UMS</b>
            <small>Identity &amp; Access Admin</small>
          </div>
        </div>
        {NAV.map((g) => (
          <div key={g.group}>
            <div className="nav-group">{g.group}</div>
            <nav className="nav">
              {g.items.map((i) => (
                <NavLink key={i.to} to={i.to}>
                  <span className="ico">{i.ico}</span>
                  {i.label}
                </NavLink>
              ))}
            </nav>
          </div>
        ))}
        <div className="sidebar-foot">{mode === 'api' ? '● Connected to UMS API' : '● Demo mode (data kept in this browser)'}</div>
      </aside>
      <div className="main">
        <header className="topbar">
          <button className="btn menu-btn" onClick={() => setOpen(!open)} aria-label="Menu">
            ☰
          </button>
          <span className="crumb hide-sm">{title}</span>
          <div className="spacer" />
          <label className="row small">
            <span className="muted">Tenant</span>
            <select value={tenantId} onChange={(e) => setTenantId(e.target.value)}>
              {state.tenants.map((t) => (
                <option key={t.id} value={t.id}>
                  {t.name}
                </option>
              ))}
            </select>
          </label>
          {mode === 'demo' ? (
            <button className="btn sm ghost hide-sm" title="Restore demo data" onClick={() => confirm('Reset all demo data?') && reset()}>
              ↺ Reset demo
            </button>
          ) : (
            <button className="btn sm ghost hide-sm" title="Reload from the API" onClick={reload}>
              ↻ Refresh
            </button>
          )}
          <span className="badge info hide-sm">{session?.fullName ?? 'ums.admin'}</span>
          {mode === 'api' && session && (
            <button className="btn sm" onClick={logout}>
              Sign out
            </button>
          )}
        </header>
        <main className="content">
          <Routes>
            <Route path="/" element={<Navigate to="/dashboard" replace />} />
            <Route path="/dashboard" element={<Dashboard />} />
            <Route path="/applications" element={<Applications />} />
            <Route path="/users" element={<Users />} />
            <Route path="/roles" element={<Roles />} />
            <Route path="/policies" element={<Policies />} />
            <Route path="/schedules" element={<Schedules />} />
            <Route path="/simulator" element={<AccessSimulator />} />
            <Route path="/audit" element={<AuditTrail />} />
            <Route path="/actions" element={<ActionTrail />} />
            <Route path="/exceptions" element={<ExceptionLogs />} />
            <Route path="/tenants" element={<Tenants />} />
            <Route path="/settings" element={<Settings />} />
            <Route path="*" element={<Navigate to="/dashboard" replace />} />
          </Routes>
        </main>
      </div>
      {toastLayer}
    </div>
  );
}

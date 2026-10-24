import { useMemo, useState } from 'react';
import { Badge, Modal, PageHead, StatusBadge, downloadCsv, fmtDateTime, usePaged } from '../components/ui';
import { useStore, useTenantData } from '../store/store';
import type { AuditLog, ExceptionLog } from '../types';

function diffKeys(a: unknown, b: unknown): string[] {
  if (!a || !b || typeof a !== 'object' || typeof b !== 'object') return [];
  const ao = a as Record<string, unknown>;
  const bo = b as Record<string, unknown>;
  return [...new Set([...Object.keys(ao), ...Object.keys(bo)])].filter((k) => JSON.stringify(ao[k]) !== JSON.stringify(bo[k]));
}

export function AuditTrail() {
  const d = useTenantData();
  const [q, setQ] = useState('');
  const [entity, setEntity] = useState('');
  const [open, setOpen] = useState<AuditLog | null>(null);
  const entities = [...new Set(d.auditLogs.map((a) => a.entity))];
  const rows = useMemo(
    () =>
      d.auditLogs
        .filter((a) => (!entity || a.entity === entity) && (a.summary + a.actor + a.entityId).toLowerCase().includes(q.toLowerCase()))
        .sort((a, b) => b.timestamp.localeCompare(a.timestamp)),
    [d.auditLogs, q, entity],
  );
  const paged = usePaged(rows, 20);

  return (
    <>
      <PageHead
        title="Audit Trail"
        subtitle="Who changed what, when and from where — with before/after values for every user, role, policy and application change."
        actions={
          <button className="btn" onClick={() => downloadCsv('audit-trail.csv', rows.map(({ oldValues, newValues, ...r }) => ({ ...r, oldValues: JSON.stringify(oldValues), newValues: JSON.stringify(newValues) })))}>
            ⇩ Export
          </button>
        }
      />
      <div className="card">
        <div className="card-head">
          <div className="row grow">
            <input className="search" placeholder="Search summary, actor…" value={q} onChange={(e) => setQ(e.target.value)} />
            <select value={entity} onChange={(e) => setEntity(e.target.value)}>
              <option value="">All entities</option>
              {entities.map((x) => (
                <option key={x}>{x}</option>
              ))}
            </select>
          </div>
        </div>
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Time</th>
                <th>Actor</th>
                <th>Entity</th>
                <th>Action</th>
                <th>Summary</th>
                <th>IP / Device</th>
              </tr>
            </thead>
            <tbody>
              {paged.rows.map((a) => (
                <tr key={a.id} className="clickable" onClick={() => setOpen(a)}>
                  <td className="small" style={{ whiteSpace: 'nowrap' }}>
                    {fmtDateTime(a.timestamp)}
                  </td>
                  <td>{a.actor}</td>
                  <td>{a.entity}</td>
                  <td>
                    <Badge kind={a.action === 'Delete' || a.action === 'Revoke' ? 'bad' : a.action === 'Create' || a.action === 'Assign' ? 'ok' : 'info'}>{a.action}</Badge>
                  </td>
                  <td>{a.summary}</td>
                  <td className="small muted">
                    {a.ip}
                    <div>{a.device}</div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          {rows.length === 0 && <div className="empty">No audit entries.</div>}
        </div>
        {paged.pager}
      </div>
      {open && (
        <Modal wide title={`${open.entity} ${open.action} · ${fmtDateTime(open.timestamp)}`} onClose={() => setOpen(null)}>
          <p style={{ marginTop: 0 }}>
            <b>{open.actor}</b> — {open.summary}
          </p>
          {diffKeys(open.oldValues, open.newValues).length > 0 && (
            <p className="small">
              Changed fields:{' '}
              {diffKeys(open.oldValues, open.newValues).map((k) => (
                <span key={k} className="chip">
                  {k}
                </span>
              ))}
            </p>
          )}
          <div className="grid-2">
            <div>
              <h3 style={{ marginBottom: 6 }}>Before</h3>
              <pre className="json">{JSON.stringify(open.oldValues, null, 2) ?? 'null'}</pre>
            </div>
            <div>
              <h3 style={{ marginBottom: 6 }}>After</h3>
              <pre className="json">{JSON.stringify(open.newValues, null, 2) ?? 'null'}</pre>
            </div>
          </div>
        </Modal>
      )}
    </>
  );
}

export function ActionTrail() {
  const d = useTenantData();
  const [q, setQ] = useState('');
  const [decision, setDecision] = useState('');
  const [app, setApp] = useState('');
  const rows = useMemo(
    () =>
      d.actionLogs
        .filter((a) => (!decision || a.decision === decision) && (!app || a.app === app) && (a.userName + a.action + a.route + a.reason).toLowerCase().includes(q.toLowerCase()))
        .sort((a, b) => b.timestamp.localeCompare(a.timestamp)),
    [d.actionLogs, q, decision, app],
  );
  const paged = usePaged(rows, 25);
  return (
    <>
      <PageHead
        title="User Action Trail"
        subtitle="Every authorization decision made for end users across all applications: route, permission checked, decision and reason."
        actions={
          <button className="btn" onClick={() => downloadCsv('user-actions.csv', rows as never)}>
            ⇩ Export
          </button>
        }
      />
      <div className="card">
        <div className="card-head">
          <div className="row grow">
            <input className="search" placeholder="Search user, permission, route…" value={q} onChange={(e) => setQ(e.target.value)} />
            <select value={app} onChange={(e) => setApp(e.target.value)}>
              <option value="">All applications</option>
              {d.applications.map((a) => (
                <option key={a.id} value={a.code}>
                  {a.name}
                </option>
              ))}
            </select>
            <select value={decision} onChange={(e) => setDecision(e.target.value)}>
              <option value="">Allow &amp; Deny</option>
              <option>Allow</option>
              <option>Deny</option>
            </select>
          </div>
        </div>
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Time</th>
                <th>User</th>
                <th>App</th>
                <th>Route</th>
                <th>Permission</th>
                <th>Decision</th>
                <th>Reason</th>
                <th>IP</th>
              </tr>
            </thead>
            <tbody>
              {paged.rows.map((a) => (
                <tr key={a.id}>
                  <td className="small" style={{ whiteSpace: 'nowrap' }}>
                    {fmtDateTime(a.timestamp)}
                  </td>
                  <td>{a.userName}</td>
                  <td>{a.app}</td>
                  <td className="mono small">{a.route}</td>
                  <td className="mono small">{a.action}</td>
                  <td>
                    <StatusBadge status={a.decision} />
                  </td>
                  <td className="small">{a.reason}</td>
                  <td className="small muted">{a.ip}</td>
                </tr>
              ))}
            </tbody>
          </table>
          {rows.length === 0 && <div className="empty">No actions recorded.</div>}
        </div>
        {paged.pager}
      </div>
    </>
  );
}

export function ExceptionLogs() {
  const { resolveException } = useStore();
  const d = useTenantData();
  const [sev, setSev] = useState('');
  const [onlyOpen, setOnlyOpen] = useState(true);
  const [open, setOpen] = useState<ExceptionLog | null>(null);
  const rows = d.exceptionLogs.filter((e) => (!sev || e.severity === sev) && (!onlyOpen || !e.resolved)).sort((a, b) => b.timestamp.localeCompare(a.timestamp));
  const paged = usePaged(rows, 20);

  const resolve = async (e: ExceptionLog) => {
    if (await resolveException(e.id)) setOpen(null);
  };

  return (
    <>
      <PageHead title="Exception Logs" subtitle="Unhandled exceptions captured by the exception-logging middleware in every connected application, with request context and stack trace." />
      <div className="card">
        <div className="card-head">
          <div className="row grow">
            <select value={sev} onChange={(e) => setSev(e.target.value)}>
              <option value="">All severities</option>
              <option>Critical</option>
              <option>Error</option>
              <option>Warning</option>
            </select>
            <label className="row small">
              <input type="checkbox" checked={onlyOpen} onChange={(e) => setOnlyOpen(e.target.checked)} /> Unresolved only
            </label>
          </div>
        </div>
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Time</th>
                <th>App</th>
                <th>Severity</th>
                <th>Message</th>
                <th>Request</th>
                <th>User</th>
                <th>Status</th>
              </tr>
            </thead>
            <tbody>
              {paged.rows.map((e) => (
                <tr key={e.id} className="clickable" onClick={() => setOpen(e)}>
                  <td className="small" style={{ whiteSpace: 'nowrap' }}>
                    {fmtDateTime(e.timestamp)}
                  </td>
                  <td>{e.app}</td>
                  <td>
                    <Badge kind={e.severity === 'Warning' ? 'warn' : 'bad'}>{e.severity}</Badge>
                  </td>
                  <td>{e.message}</td>
                  <td className="mono small">{e.requestPath}</td>
                  <td className="small">{e.userName ?? '—'}</td>
                  <td>{e.resolved ? <Badge kind="ok">Resolved</Badge> : <Badge kind="warn">Open</Badge>}</td>
                </tr>
              ))}
            </tbody>
          </table>
          {rows.length === 0 && <div className="empty">No exceptions 🎉</div>}
        </div>
        {paged.pager}
      </div>
      {open && (
        <Modal
          wide
          title={`${open.app} · ${open.severity}`}
          onClose={() => setOpen(null)}
          footer={
            !open.resolved && (
              <button className="btn primary" onClick={() => resolve(open)}>
                Mark resolved
              </button>
            )
          }
        >
          <p style={{ marginTop: 0 }}>
            <b>{open.message}</b>
          </p>
          <p className="small muted">
            {fmtDateTime(open.timestamp)} · {open.requestPath} · {open.ip} · user: {open.userName ?? 'anonymous'}
          </p>
          <pre className="json">{open.stackTrace}</pre>
        </Modal>
      )}
    </>
  );
}

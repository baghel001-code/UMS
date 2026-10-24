import { useEffect, useState, type ReactNode } from 'react';

export function PageHead({ title, subtitle, actions }: { title: string; subtitle?: ReactNode; actions?: ReactNode }) {
  return (
    <div className="page-head">
      <div>
        <h1>{title}</h1>
        {subtitle && <p>{subtitle}</p>}
      </div>
      {actions && <div className="row">{actions}</div>}
    </div>
  );
}

export function Badge({ kind, children }: { kind?: 'ok' | 'bad' | 'warn' | 'info'; children: ReactNode }) {
  return <span className={`badge ${kind ?? ''}`}>{children}</span>;
}

export function StatusBadge({ status }: { status: string }) {
  const kind = ({ Active: 'ok', Allow: 'ok', Inactive: 'warn', Locked: 'bad', Deny: 'bad', Disabled: 'warn' } as const)[status as 'Active'];
  return <Badge kind={kind}>{status}</Badge>;
}

function useEscape(onClose: () => void) {
  useEffect(() => {
    const h = (e: KeyboardEvent) => e.key === 'Escape' && onClose();
    window.addEventListener('keydown', h);
    return () => window.removeEventListener('keydown', h);
  }, [onClose]);
}

export function Modal({
  title,
  onClose,
  children,
  footer,
  wide,
}: {
  title: ReactNode;
  onClose: () => void;
  children: ReactNode;
  footer?: ReactNode;
  wide?: boolean;
}) {
  useEscape(onClose);
  return (
    <div className="overlay" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <div className={`modal ${wide ? 'wide' : ''}`} role="dialog" aria-modal="true">
        <div className="modal-head">
          <h2>{title}</h2>
          <button className="btn ghost" onClick={onClose} aria-label="Close">
            ✕
          </button>
        </div>
        <div className="modal-body">{children}</div>
        {footer && <div className="modal-foot">{footer}</div>}
      </div>
    </div>
  );
}

export function Drawer({ title, onClose, children, footer }: { title: ReactNode; onClose: () => void; children: ReactNode; footer?: ReactNode }) {
  useEscape(onClose);
  return (
    <div className="overlay drawer-overlay" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <div className="drawer" role="dialog" aria-modal="true">
        <div className="modal-head">
          <h2>{title}</h2>
          <button className="btn ghost" onClick={onClose} aria-label="Close">
            ✕
          </button>
        </div>
        <div className="modal-body">{children}</div>
        {footer && <div className="modal-foot">{footer}</div>}
      </div>
    </div>
  );
}

export function Tabs<T extends string>({ tabs, value, onChange }: { tabs: readonly T[] | { id: T; label: string }[]; value: T; onChange: (t: T) => void }) {
  const items = (tabs as (T | { id: T; label: string })[]).map((t) => (typeof t === 'string' ? { id: t, label: t } : t));
  return (
    <div className="tabs" role="tablist">
      {items.map((t) => (
        <button key={t.id} role="tab" className={t.id === value ? 'active' : ''} onClick={() => onChange(t.id)}>
          {t.label}
        </button>
      ))}
    </div>
  );
}

export function Field({ label, children, hint }: { label: string; children: ReactNode; hint?: ReactNode }) {
  return (
    <label className="field">
      <span>{label}</span>
      {children}
      {hint && <small className="muted">{hint}</small>}
    </label>
  );
}

export function usePaged<T>(rows: T[], pageSize = 15) {
  const [page, setPage] = useState(0);
  const pages = Math.max(1, Math.ceil(rows.length / pageSize));
  const p = Math.min(page, pages - 1);
  return {
    rows: rows.slice(p * pageSize, (p + 1) * pageSize),
    pager: (
      <div className="pager">
        <span>
          {rows.length === 0 ? 0 : p * pageSize + 1}–{Math.min(rows.length, (p + 1) * pageSize)} of {rows.length.toLocaleString('en-IN')}
        </span>
        <div className="row">
          <button className="btn sm" disabled={p === 0} onClick={() => setPage(p - 1)}>
            ‹ Prev
          </button>
          <span>
            Page {p + 1} / {pages}
          </span>
          <button className="btn sm" disabled={p >= pages - 1} onClick={() => setPage(p + 1)}>
            Next ›
          </button>
        </div>
      </div>
    ),
    reset: () => setPage(0),
  };
}

export function KeyValueEditor({ value, onChange, keyPlaceholder = 'key', valuePlaceholder = 'value' }: {
  value: Record<string, string>;
  onChange: (v: Record<string, string>) => void;
  keyPlaceholder?: string;
  valuePlaceholder?: string;
}) {
  const entries = Object.entries(value);
  const [k, setK] = useState('');
  const [v, setV] = useState('');
  const add = () => {
    if (!k.trim()) return;
    onChange({ ...value, [k.trim()]: v.trim() });
    setK('');
    setV('');
  };
  return (
    <div className="stack" style={{ gap: 6 }}>
      {entries.map(([ek, ev]) => (
        <div className="kv-row" key={ek}>
          <input value={ek} disabled />
          <input value={ev} onChange={(e) => onChange({ ...value, [ek]: e.target.value })} />
          <button
            className="btn sm danger"
            onClick={() => {
              const n = { ...value };
              delete n[ek];
              onChange(n);
            }}
          >
            Remove
          </button>
        </div>
      ))}
      <div className="kv-row">
        <input placeholder={keyPlaceholder} value={k} onChange={(e) => setK(e.target.value)} onKeyDown={(e) => e.key === 'Enter' && add()} />
        <input placeholder={valuePlaceholder} value={v} onChange={(e) => setV(e.target.value)} onKeyDown={(e) => e.key === 'Enter' && add()} />
        <button className="btn sm" onClick={add}>
          Add
        </button>
      </div>
    </div>
  );
}

export const fmtDateTime = (s: string | null) =>
  s ? new Date(s).toLocaleString('en-IN', { day: '2-digit', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' }) : '—';

export const fmtDate = (s: string | null) => (s ? new Date(s).toLocaleDateString('en-IN', { day: '2-digit', month: 'short', year: 'numeric' }) : '—');

export function downloadCsv(filename: string, rows: Record<string, unknown>[]) {
  if (rows.length === 0) return;
  const cols = Object.keys(rows[0]);
  const esc = (v: unknown) => `"${String(v ?? '').replace(/"/g, '""')}"`;
  const csv = [cols.join(','), ...rows.map((r) => cols.map((c) => esc(r[c])).join(','))].join('\n');
  const a = document.createElement('a');
  a.href = URL.createObjectURL(new Blob([csv], { type: 'text/csv' }));
  a.download = filename;
  a.click();
  URL.revokeObjectURL(a.href);
}

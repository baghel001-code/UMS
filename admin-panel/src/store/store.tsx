// Client-side store standing in for the UMS REST API.
// Every mutation writes an AuditLog entry with before/after values, like IAuditService on the server.
import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { createSeed, localIso, T_VMM, type UmsState } from '../data/seed';
import type { AuditLog } from '../types';

const STORAGE_KEY = 'vmm-ums-admin-v1';
export const CURRENT_ADMIN = 'ums.admin';

type CollectionKey = 'tenants' | 'applications' | 'roles' | 'users' | 'policies' | 'schedules' | 'holidays';
type Item<K extends CollectionKey> = UmsState[K][number];

const ENTITY: Record<CollectionKey, string> = {
  tenants: 'Tenant',
  applications: 'Application',
  roles: 'Role',
  users: 'User',
  policies: 'Policy',
  schedules: 'Schedule',
  holidays: 'Holiday',
};

function load(): UmsState {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (raw) return JSON.parse(raw) as UmsState;
  } catch {
    /* storage unavailable: fall back to seed */
  }
  return createSeed();
}

export interface Toast {
  id: number;
  text: string;
  kind: 'ok' | 'error';
}

interface StoreApi {
  state: UmsState;
  tenantId: string;
  setTenantId: (id: string) => void;
  upsert: <K extends CollectionKey>(key: K, item: Item<K>, summary?: string) => void;
  remove: <K extends CollectionKey>(key: K, id: string, summary?: string) => void;
  audit: (entry: Pick<AuditLog, 'entity' | 'entityId' | 'action' | 'summary'> & Partial<AuditLog>) => void;
  setState: (fn: (s: UmsState) => UmsState) => void;
  reset: () => void;
  toast: (text: string, kind?: Toast['kind']) => void;
  toasts: Toast[];
}

const Ctx = createContext<StoreApi | null>(null);

const nowIso = () => localIso(new Date());
export const newId = (prefix: string) => `${prefix}-${Date.now().toString(36)}${Math.floor(Math.random() * 1e4).toString(36)}`;

function itemName(item: { id: string } & Record<string, unknown>): string {
  return String(item.name ?? item.fullName ?? item.code ?? item.id);
}

export function StoreProvider({ children }: { children: ReactNode }) {
  const [state, setStateRaw] = useState<UmsState>(load);
  const [tenantId, setTenantId] = useState<string>(T_VMM);
  const [toasts, setToasts] = useState<Toast[]>([]);

  useEffect(() => {
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(state));
    } catch {
      /* quota / private mode: keep working in memory */
    }
  }, [state]);

  const toast = useCallback((text: string, kind: Toast['kind'] = 'ok') => {
    const id = Date.now() + Math.random();
    setToasts((t) => [...t, { id, text, kind }]);
    setTimeout(() => setToasts((t) => t.filter((x) => x.id !== id)), 3500);
  }, []);

  const makeAudit = useCallback(
    (e: Pick<AuditLog, 'entity' | 'entityId' | 'action' | 'summary'> & Partial<AuditLog>): AuditLog => ({
      id: newId('al'),
      tenantId,
      timestamp: nowIso(),
      actor: CURRENT_ADMIN,
      oldValues: null,
      newValues: null,
      ip: '10.12.4.21',
      device: navigator.userAgent.includes('Edg') ? 'Edge' : 'Browser',
      ...e,
    }),
    [tenantId],
  );

  const api = useMemo<StoreApi>(
    () => ({
      state,
      tenantId,
      setTenantId,
      toasts,
      toast,
      setState: (fn) => setStateRaw(fn),
      audit: (e) => setStateRaw((s) => ({ ...s, auditLogs: [makeAudit(e), ...s.auditLogs] })),
      upsert: (key, item, summary) =>
        setStateRaw((s) => {
          const list = s[key] as Item<typeof key>[];
          const old = list.find((x) => x.id === item.id);
          const next = old ? list.map((x) => (x.id === item.id ? item : x)) : [...list, item];
          const log = makeAudit({
            entity: ENTITY[key],
            entityId: item.id,
            action: old ? 'Update' : 'Create',
            summary: summary ?? `${old ? 'Updated' : 'Created'} ${ENTITY[key].toLowerCase()} ${itemName(item as never)}`,
            oldValues: old ?? null,
            newValues: item,
          });
          return { ...s, [key]: next, auditLogs: [log, ...s.auditLogs] };
        }),
      remove: (key, id, summary) =>
        setStateRaw((s) => {
          const list = s[key] as Item<typeof key>[];
          const old = list.find((x) => x.id === id);
          if (!old) return s;
          const log = makeAudit({
            entity: ENTITY[key],
            entityId: id,
            action: 'Delete',
            summary: summary ?? `Deleted ${ENTITY[key].toLowerCase()} ${itemName(old as never)}`,
            oldValues: old,
          });
          return { ...s, [key]: list.filter((x) => x.id !== id), auditLogs: [log, ...s.auditLogs] };
        }),
      reset: () => {
        setStateRaw(createSeed());
        setTenantId(T_VMM);
        toast('Demo data reset');
      },
    }),
    [state, tenantId, toasts, toast, makeAudit],
  );

  return <Ctx.Provider value={api}>{children}</Ctx.Provider>;
}

export function useStore(): StoreApi {
  const v = useContext(Ctx);
  if (!v) throw new Error('useStore outside StoreProvider');
  return v;
}

/** Collections filtered to the tenant selected in the top bar. */
export function useTenantData() {
  const { state, tenantId } = useStore();
  return useMemo(
    () => ({
      tenant: state.tenants.find((t) => t.id === tenantId)!,
      users: state.users.filter((u) => u.tenantId === tenantId),
      roles: state.roles.filter((r) => r.tenantId === tenantId),
      policies: state.policies.filter((p) => p.tenantId === tenantId),
      schedules: state.schedules.filter((s) => s.tenantId === tenantId),
      holidays: state.holidays.filter((h) => h.tenantId === tenantId),
      applications: state.applications.filter((a) => a.tenantIds.includes(tenantId)),
      auditLogs: state.auditLogs.filter((a) => a.tenantId === tenantId),
      actionLogs: state.actionLogs.filter((a) => a.tenantId === tenantId),
      exceptionLogs: state.exceptionLogs.filter((a) => a.tenantId === tenantId),
      settings: state.settings[tenantId],
    }),
    [state, tenantId],
  );
}

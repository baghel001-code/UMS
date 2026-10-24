// State for the admin panel. Two modes:
//  - API mode (VITE_API_URL set): data is loaded from and saved to the UMS Web API, which writes the audit trail.
//  - Demo mode: data lives in the browser (localStorage) and this store writes the audit entries itself.
// Pages call the same actions in both modes; every action resolves to true when it succeeded.
import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { API_MODE, ApiError, api, getToken, setApiTenant, setToken } from '../api/client';
import { createSeed, localIso, T_VMM, type UmsState } from '../data/seed';
import type { AccessRequest, AuditLog, Decision, RoleAssignment, SecuritySettings, User, UserStatus } from '../types';

const STORAGE_KEY = 'vmm-ums-admin-v2';
const SESSION_KEY = 'vmm-ums-session';
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

// REST resource behind each collection (see WebApi/Controllers/V1)
const ROUTE: Record<CollectionKey, string> = {
  tenants: '/tenants',
  applications: '/applications',
  roles: '/roles',
  users: '/users',
  policies: '/policies',
  schedules: '/schedules',
  holidays: '/holidays',
};

const EMPTY: UmsState = {
  tenants: [], applications: [], roles: [], users: [], policies: [], schedules: [], holidays: [],
  auditLogs: [], actionLogs: [], exceptionLogs: [], settings: {},
};

function loadDemo(): UmsState {
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

export type LoadStatus = 'loading' | 'ready' | 'login' | 'error';

export interface Session {
  fullName: string;
  email: string;
}

interface StoreApi {
  mode: 'api' | 'demo';
  status: LoadStatus;
  loadError: string;
  session: Session | null;
  state: UmsState;
  tenantId: string;
  setTenantId: (id: string) => void;
  upsert: <K extends CollectionKey>(key: K, item: Item<K>, summary?: string) => Promise<boolean>;
  remove: <K extends CollectionKey>(key: K, id: string, summary?: string) => Promise<boolean>;
  saveSettings: (settings: SecuritySettings) => Promise<boolean>;
  bulkUsers: (ids: string[], change: { status?: UserStatus; assignRole?: RoleAssignment }) => Promise<boolean>;
  importUsers: (users: User[]) => Promise<{ created: number; skipped: number } | null>;
  setPassword: (userId: string, password: string) => Promise<boolean>;
  resolveException: (id: string) => Promise<boolean>;
  recordDecision: (req: AccessRequest, decision: Decision) => Promise<Decision | null>;
  login: (email: string, password: string) => Promise<boolean>;
  logout: () => void;
  reload: () => void;
  reset: () => void;
  toast: (text: string, kind?: Toast['kind']) => void;
  toasts: Toast[];
}

const Ctx = createContext<StoreApi | null>(null);

const nowIso = () => localIso(new Date());

/** New ids are GUIDs so the same records can be sent to the API. */
export const newId = (_prefix?: string): string => {
  if (typeof crypto !== 'undefined' && 'randomUUID' in crypto) return crypto.randomUUID();
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
    const r = (Math.random() * 16) | 0;
    return (c === 'x' ? r : (r & 0x3) | 0x8).toString(16);
  });
};

function itemName(item: { id: string } & Record<string, unknown>): string {
  return String(item.name ?? item.fullName ?? item.code ?? item.id);
}

export function StoreProvider({ children }: { children: ReactNode }) {
  const mode = API_MODE ? 'api' : 'demo';
  const [state, setState] = useState<UmsState>(() => (API_MODE ? EMPTY : loadDemo()));
  const [tenantId, setTenantIdRaw] = useState<string>(API_MODE ? '' : T_VMM);
  const [status, setStatus] = useState<LoadStatus>(API_MODE ? 'loading' : 'ready');
  const [loadError, setLoadError] = useState('');
  const [session, setSessionRaw] = useState<Session | null>(() => {
    try {
      return getToken() ? (JSON.parse(sessionStorage.getItem(SESSION_KEY) ?? 'null') as Session | null) : null;
    } catch {
      return null;
    }
  });
  const setSession = (s: Session | null) => {
    setSessionRaw(s);
    try {
      if (s) sessionStorage.setItem(SESSION_KEY, JSON.stringify(s));
      else sessionStorage.removeItem(SESSION_KEY);
    } catch {
      /* storage blocked */
    }
  };
  const [toasts, setToasts] = useState<Toast[]>([]);
  const loadSeq = useRef(0);

  useEffect(() => {
    if (API_MODE) return;
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(state));
    } catch {
      /* quota / private mode: keep working in memory */
    }
  }, [state]);

  const toast = useCallback((text: string, kind: Toast['kind'] = 'ok') => {
    const id = Date.now() + Math.random();
    setToasts((t) => [...t, { id, text, kind }]);
    setTimeout(() => setToasts((t) => t.filter((x) => x.id !== id)), kind === 'error' ? 6000 : 3500);
  }, []);

  // ------------------------------------------------------------ API loading

  const loadTenantData = useCallback(async (wanted: string | null) => {
    const seq = ++loadSeq.current;
    setStatus((s) => (s === 'ready' ? 'ready' : 'loading'));
    try {
      setApiTenant('');
      const tenants = await api<UmsState['tenants']>('GET', '/tenants');
      const tenant = tenants.find((t) => t.id === wanted) ?? tenants.find((t) => t.code === 'VMM') ?? tenants[0];
      if (!tenant) throw new ApiError(500, 'No tenants exist yet');
      setApiTenant(tenant.id);
      const [applications, roles, users, policies, schedules, holidays, auditLogs, actionLogs, exceptionLogs, settings] = await Promise.all([
        api<UmsState['applications']>('GET', '/applications?all=true'),
        api<UmsState['roles']>('GET', '/roles'),
        api<UmsState['users']>('GET', '/users'),
        api<UmsState['policies']>('GET', '/policies'),
        api<UmsState['schedules']>('GET', '/schedules'),
        api<UmsState['holidays']>('GET', '/holidays'),
        api<UmsState['auditLogs']>('GET', '/audit-logs'),
        api<UmsState['actionLogs']>('GET', '/user-actions'),
        api<UmsState['exceptionLogs']>('GET', '/exceptions'),
        api<SecuritySettings>('GET', `/tenants/${tenant.id}/settings`),
      ]);
      if (seq !== loadSeq.current) return; // a newer load started (fast tenant switching)
      setState({ tenants, applications, roles, users, policies, schedules, holidays, auditLogs, actionLogs, exceptionLogs, settings: { [tenant.id]: settings } });
      setTenantIdRaw(tenant.id);
      setStatus('ready');
    } catch (e) {
      if (seq !== loadSeq.current) return;
      if (e instanceof ApiError && e.status === 401) {
        setToken(null);
        setStatus('login');
      } else {
        setLoadError(e instanceof Error ? e.message : String(e));
        setStatus('error');
      }
    }
  }, []);

  useEffect(() => {
    if (API_MODE) void loadTenantData(null);
  }, [loadTenantData]);

  // after a change the server has written new audit / action entries
  const refreshLogs = useCallback(async () => {
    try {
      const [auditLogs, actionLogs] = await Promise.all([
        api<UmsState['auditLogs']>('GET', '/audit-logs'),
        api<UmsState['actionLogs']>('GET', '/user-actions'),
      ]);
      setState((s) => ({ ...s, auditLogs, actionLogs }));
    } catch {
      /* logs are informational; the change itself succeeded */
    }
  }, []);

  /** Runs an API call; shows the server's message and returns null when it fails. */
  const call = useCallback(
    async <T,>(fn: () => Promise<T>): Promise<T | null> => {
      try {
        return await fn();
      } catch (e) {
        if (e instanceof ApiError && e.status === 401) {
          setToken(null);
          setStatus('login');
        }
        toast(e instanceof Error ? e.message : String(e), 'error');
        return null;
      }
    },
    [toast],
  );

  // ------------------------------------------------------------ demo-mode audit

  const makeAudit = useCallback(
    (e: Pick<AuditLog, 'entity' | 'entityId' | 'action' | 'summary'> & Partial<AuditLog>): AuditLog => ({
      id: newId(),
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
  const audit = useCallback((e: Parameters<typeof makeAudit>[0]) => setState((s) => ({ ...s, auditLogs: [makeAudit(e), ...s.auditLogs] })), [makeAudit]);

  // ------------------------------------------------------------ actions

  const replaceItem = <K extends CollectionKey>(key: K, item: Item<K>) =>
    setState((s) => {
      const list = s[key] as Item<K>[];
      const exists = list.some((x) => x.id === item.id);
      return { ...s, [key]: exists ? list.map((x) => (x.id === item.id ? item : x)) : [...list, item] };
    });

  const value = useMemo<StoreApi>(
    () => ({
      mode,
      status,
      loadError,
      session,
      state,
      tenantId,
      toasts,
      toast,
      setTenantId: (id) => {
        if (API_MODE) void loadTenantData(id);
        else setTenantIdRaw(id);
      },

      upsert: async (key, item, summary) => {
        if (API_MODE) {
          const saved = await call(() =>
            key === 'holidays' ? api<Item<typeof key>>('POST', ROUTE[key], item) : api<Item<typeof key>>('PUT', `${ROUTE[key]}/${item.id}`, item),
          );
          if (!saved) return false;
          replaceItem(key, saved);
          void refreshLogs();
          return true;
        }
        setState((s) => {
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
          // a new tenant starts with a copy of the first tenant's security settings
          const settings = key === 'tenants' && !s.settings[item.id] ? { ...s.settings, [item.id]: { ...Object.values(s.settings)[0] } } : s.settings;
          return { ...s, [key]: next, settings, auditLogs: [log, ...s.auditLogs] };
        });
        return true;
      },

      remove: async (key, id, summary) => {
        if (API_MODE) {
          const ok = await call(() => api('DELETE', `${ROUTE[key]}/${id}`));
          if (ok === null) return false;
          setState((s) => ({ ...s, [key]: (s[key] as Item<typeof key>[]).filter((x) => x.id !== id) }));
          void refreshLogs();
          return true;
        }
        setState((s) => {
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
        });
        return true;
      },

      saveSettings: async (settings) => {
        if (API_MODE) {
          const saved = await call(() => api<SecuritySettings>('PUT', `/tenants/${tenantId}/settings`, settings));
          if (!saved) return false;
          setState((s) => ({ ...s, settings: { ...s.settings, [tenantId]: saved } }));
          void refreshLogs();
          return true;
        }
        const old = state.settings[tenantId];
        setState((s) => ({ ...s, settings: { ...s.settings, [tenantId]: settings } }));
        audit({ entity: 'SecuritySettings', entityId: tenantId, action: 'Update', summary: 'Updated security settings', oldValues: old, newValues: settings });
        return true;
      },

      bulkUsers: async (ids, change) => {
        const apply = (u: User): User => ({
          ...u,
          status: change.status ?? u.status,
          roles: change.assignRole && !u.roles.some((r) => r.roleId === change.assignRole!.roleId) ? [...u.roles, change.assignRole] : u.roles,
        });
        if (API_MODE) {
          const ok = await call(() => api<number>('POST', '/users/bulk', { userIds: ids, status: change.status ?? null, assignRole: change.assignRole ?? null }));
          if (ok === null) return false;
        } else {
          const roleCode = state.roles.find((r) => r.id === change.assignRole?.roleId)?.code;
          audit({
            entity: 'User',
            entityId: ids.join(','),
            action: change.assignRole ? 'Assign' : 'Update',
            summary: `Bulk ${[change.status && `set status ${change.status}`, roleCode && `assigned ${roleCode}`].filter(Boolean).join(' and ')} on ${ids.length} user(s)`,
            newValues: change,
          });
        }
        const set = new Set(ids);
        setState((s) => ({ ...s, users: s.users.map((u) => (set.has(u.id) ? apply(u) : u)) }));
        if (API_MODE) void refreshLogs();
        return true;
      },

      importUsers: async (users) => {
        if (API_MODE) {
          const res = await call(() => api<{ created: number; skipped: number }>('POST', '/users/import', users));
          if (!res) return null;
          const fresh = await call(() => api<User[]>('GET', '/users'));
          if (fresh) setState((s) => ({ ...s, users: fresh }));
          void refreshLogs();
          return res;
        }
        const existing = new Set(state.users.map((u) => u.empCode));
        const created = users.filter((u) => !existing.has(u.empCode));
        setState((s) => ({ ...s, users: [...s.users, ...created] }));
        audit({ entity: 'User', entityId: 'bulk-import', action: 'Create', summary: `Bulk imported ${created.length} user(s)` });
        return { created: created.length, skipped: users.length - created.length };
      },

      setPassword: async (userId, password) => {
        if (API_MODE) {
          const ok = await call(() => api('POST', `/users/${userId}/password`, { password }));
          if (ok === null) return false;
          void refreshLogs();
          return true;
        }
        audit({ entity: 'User', entityId: userId, action: 'Reset', summary: 'Password set (demo mode: not stored)' });
        return true;
      },

      resolveException: async (id) => {
        if (API_MODE) {
          const saved = await call(() => api<UmsState['exceptionLogs'][number]>('PUT', `/exceptions/${id}/resolve`));
          if (!saved) return false;
          setState((s) => ({ ...s, exceptionLogs: s.exceptionLogs.map((x) => (x.id === id ? saved : x)) }));
          void refreshLogs();
          return true;
        }
        const e = state.exceptionLogs.find((x) => x.id === id);
        setState((s) => ({ ...s, exceptionLogs: s.exceptionLogs.map((x) => (x.id === id ? { ...x, resolved: true } : x)) }));
        audit({ entity: 'ExceptionLog', entityId: id, action: 'Update', summary: `Marked exception resolved: ${e?.message.slice(0, 60)}` });
        return true;
      },

      // API mode asks the server's decision engine (and logs it); demo mode records the local decision
      recordDecision: async (req, decision) => {
        const user = state.users.find((u) => u.id === req.userId);
        if (!user) return null;
        if (API_MODE) {
          const res = await call(() =>
            api<Decision>('POST', '/authorize', {
              userId: req.userId, app: req.app, module: req.module, resource: req.resource, action: req.action,
              resourceAttributes: req.resourceAttributes, env: { ...req.env, dateTime: `${req.env.dateTime}:00` }, log: true,
              route: `/simulator/${req.module}/${req.resource}`.toLowerCase(),
            }),
          );
          if (res) void refreshLogs();
          return res;
        }
        setState((s) => ({
          ...s,
          actionLogs: [
            {
              id: newId(), tenantId, timestamp: nowIso(), userId: user.id, userName: user.fullName, app: req.app,
              route: `/simulator/${req.module}/${req.resource}`.toLowerCase(), action: `${req.app}.${req.module}.${req.resource}.${req.action}`.toUpperCase(),
              decision: decision.allowed ? 'Allow' : 'Deny', reason: decision.reason, ip: req.env.ip,
            },
            ...s.actionLogs,
          ],
        }));
        return decision;
      },

      login: async (email, password) => {
        try {
          const res = await api<{ token: string; tenantId: string; fullName: string; email: string }>('POST', '/auth/login', { email, password });
          setToken(res.token);
          setSession({ fullName: res.fullName, email: res.email });
          await loadTenantData(res.tenantId);
          return true;
        } catch (e) {
          toast(e instanceof Error ? e.message : String(e), 'error');
          return false;
        }
      },

      logout: () => {
        setToken(null);
        setSession(null);
        if (API_MODE) void loadTenantData(null);
      },

      reload: () => {
        if (API_MODE) void loadTenantData(tenantId || null);
      },

      reset: () => {
        if (API_MODE) return;
        setState(createSeed());
        setTenantIdRaw(T_VMM);
        toast('Demo data reset');
      },
    }),
    [mode, status, loadError, session, state, tenantId, toasts, toast, makeAudit, audit, call, refreshLogs, loadTenantData],
  );

  return <Ctx.Provider value={value}>{children}</Ctx.Provider>;
}

export const hasStoredToken = () => getToken() !== null;

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
      tenant: state.tenants.find((t) => t.id === tenantId) ?? state.tenants[0],
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

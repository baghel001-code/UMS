# VMM User Management System – Access Control Architecture

This document explains the access model behind the admin portal (`UMS.AdminPortal`, Blazor Server + MudBlazor). It also defines the backend
contract (database tables + REST API) that the ASP.NET Core UMS API must implement. It follows the two reference
documents: *Architecture document for user management and authentication with RBAC, ABAC, policy, multi-tenant,
audit trail and exception logging*, and *User Management System for Vishal Mega Mart*.

## 1. The question every request must answer

> Can **user U** perform **action A** on **resource R** (a specific record) inside **module M** of **application P**,
> **now**, from **this network / device / channel**?

The Policy Decision Point (PDP) answers in layers. The first layer that fails denies the request
(*deny-overrides*):

| # | Layer | Source of truth | Example deny |
|---|-------|-----------------|--------------|
| 1 | Tenant | `Tenants.IsActive` | Vendor network suspended |
| 2 | Identity | `Users.Status`, `Users.AccountExpiry` | Locked after 5 failed logins; contract ended |
| 3 | Application | any active role grants `APP.*.*.*` | Cashier opening the Finance ERP |
| 4 | Module | any active role grants `APP.MODULE.*.*` | Cashier opening POS → Terminal Config |
| 5 | Resource · Action | role grants `APP.MODULE.RESOURCE.ACTION` | Cashier doing `POS.BILLING.INVOICE.REFUND` |
| 6 | Scope | `UserRoles.Scope` vs resource attributes | Cashier of S045 billing for store S101 |
| 7 | ABAC policies | `Policies` (Restrict / Deny), `Schedules`, `Holidays` | Finance executive at 20:15 (outside office hours) |

**Grant** policies can stand in for a missing RBAC permission in layers 3–6. Example: the approver named on a PO can
approve it without a procurement role. Restrict and Deny policies still apply after a grant.

The implementation is `DomainLayer/Services/AccessDecisionService.cs` (tests in
`Tests/UMS.Tests/AccessDecisionServiceTests.cs`). It is the only engine. `POST /api/v1/authorize`, the admin API's
`RequirePermission` filter and the admin portal's Access Simulator (through the API) all use it, so they always agree.

### Permission keys

`APP.MODULE.RESOURCE.ACTION`, upper-case. Any segment may be `*`:

* `POS.BILLING.INVOICE.REFUND` – one action on one resource
* `POS.BILLING.*.*` – everything in the Billing module
* `HRMS.*.*.*` – the whole application
* `*.*.*.VIEW` – read-only across every application (auditor)

Actions: `View, Create, Edit, Delete, Approve, Export, Print, Void, Refund`. Each resource declares which actions
make sense for it. The admin panel only offers those.

### Policy effects

| Effect | Semantics | Typical use |
|--------|-----------|-------------|
| `Restrict` | Conditions **must** be true, otherwise deny | Office hours only; own store only; MFA + office network for approvals |
| `Deny` | If conditions are true, deny | Refund > ₹10,000 by supervisor; export from unmanaged device; contract staff delete |
| `Grant` | If conditions are true, allow without a role (still subject to Restrict/Deny) | Owner of the record; named approver |

A policy *targets* `app / module / resource / actions` (wildcards allowed), optionally only users holding some
roles. Policies are evaluated in `Priority` order.

### Attributes available to conditions

* `user.*`: department, designation, location, region, employmentType, empCode, mfaEnabled, roles, plus any
  custom `UserAttributes` (e.g. `costCenter`, `vendorCode`).
* `resource.*`: the record's attributes sent by the calling application (e.g. `store`, `amount`, `ownerEmpCode`,
  `approverEmpCode`, `region`, `status`), plus `resource.app/module/type`.
* `env.*`: `now` (used with *within / outside schedule*), `time`, `day`, `date`, `isHoliday`, `network`, `device`,
  `channel`, `ip`.

Operators: `== != in "not in" > >= < <= contains "starts with" "within schedule" "outside schedule"`. The right-hand
side can be a literal or another attribute (e.g. `resource.store == user.location`). A missing attribute never
satisfies a condition, so the check fails closed.

### Office hours

`Schedules` hold named weekly windows (overnight windows such as 22:00–06:00 are supported) and a time zone.
`Holidays` (all-India or per region) close schedules flagged `ExcludeHolidays`.
Example: `Restrict PAYROLL.*.*.* WHEN InSchedule(env.now, 'GENERAL')`.

## 2. Components

```
 50+ apps (in-house & third-party)            UMS.AdminPortal (Blazor Server)
 ┌──────────────────────────────┐             ┌───────────────────────────┐
 │ PEP: auth middleware / SDK   │             │ Users · Roles · Policies  │
 │  - validates JWT (tenant,sub)│             │ Apps · Schedules · Logs   │
 │  - POST /api/v1/authorize    │             │ Access Simulator          │
 └──────────────┬───────────────┘             └─────────────┬─────────────┘
                │                                           │ REST (admin scope)
        ┌───────▼───────────────────────────────────────────▼──────┐
        │ UMS API (ASP.NET Core)                                    │
        │  ExceptionLoggingMiddleware → JWT auth → RequirePermission │
        │  → VMM.Mediator handlers (ApplicationLayer) → repositories │
        │  AccessDecisionService · IAuditService · ILogRepository    │
        │  ICurrentTenantService · ITokenIssuer · IPasswordHasher    │
        └───────┬─────────────────────────────┬────────────────────┘
                │                             │
        SQL Server (Dapper)             In-memory store (development/demo)
        (tables below, TenantId on all) Redis cache: recommended next step
```

* **Third-party apps** that cannot call `/api/v1/authorize` per request receive coarse permissions as JWT/SAML claims
  (`permissions: ["HRMS.ESS.*.VIEW", …]`) via SSO (OIDC/SAML), or are provisioned via SCIM.
* **In-house apps** call `/api/v1/authorize` (or use the shared .NET SDK that caches the user's effective
  permissions and policies for a short TTL and evaluates locally).

## 3. Database tables

The schema is `database/001_create_access_control_schema.sql`, implemented by
`Infrastructure/Persistence/SqlServer/SqlServerRepositories.cs`. Every tenant-owned table carries `TenantId`.

| Table | Key columns |
|-------|-------------|
| `Tenants`, `TenantSettings` | tenant; security settings stored as JSON |
| `Applications`, `ApplicationTenants`, `Modules`, `Resources` | catalogue; `Resources.Actions` is a comma list |
| `Roles`, `RolePermissions` | `PermissionKey` = `APP.MODULE.RESOURCE.ACTION` (wildcards allowed) |
| `UserAccounts`, `UserAttributes`, `UserRoleAssignments` | `ScopeJson` (e.g. `{"store":"S045,S046"}`), `ValidFrom` / `ValidTo` |
| `Policies` | effect, target columns, `ConditionsJson` (structured, never executed as code), priority |
| `Schedules`, `Holidays` | `WindowsJson` (`[{"days":["Mon"],"start":"09:30","end":"18:30"}]`), regions |
| `AuditLogs`, `UserActions`, `ExceptionLogs` | clustered on `(TenantId, Timestamp DESC)` |

UMS accounts live in `UserAccounts`, separate from the `[User]` table used by the generic `DapperRepository<T>`.

## 4. REST API

All routes are under `/api/v1` (see `WebApi/Controllers/V1/AccessControlControllers.cs` and Swagger). The
tenant comes from the `X-Tenant-Id` header, or else the `tenant_id` claim. Only an active `SUPER_ADMIN` may work in
another tenant. Each endpoint requires a `UMS.*` permission, checked by the `RequirePermission` filter with the
same engine. Every change writes an `AuditLogs` row with before/after JSON.

| Area | Endpoints | Permission |
|------|-----------|------------|
| Auth | `POST /auth/login` (`email, password, appCode, clientIp, userAgent` → access token, refresh token, session id, idle timeout); `POST /auth/refresh` (rotates the refresh token); `GET /auth/session?touch=` (is the session still active; `touch` records activity); `POST /auth/logout` (`SignedOut` or `Idle`) | anonymous (login, refresh); signed in |
| Sessions | `GET /sessions?activeOnly=`, `DELETE /sessions/{id}` (sign a user out) | `UMS.SETUP.SESSION.View/Delete` |
| Tenants | `GET /tenants`, `PUT /tenants/{id}`, `GET/PUT /tenants/{id}/settings` | `UMS.SETUP.TENANT.*`, `UMS.SETUP.SETTINGS.*` |
| Applications | `GET /applications[?all=true]`, `PUT/DELETE /applications/{id}` | `UMS.SETUP.APPLICATION.*` |
| Roles | `GET /roles`, `PUT/DELETE /roles/{id}` | `UMS.ROLES.ROLE.*` |
| Users | `GET /users?search=&department=&roleId=&region=&status=`, `GET/PUT /users/{id}`, `POST /users/bulk`, `POST /users/import`, `GET /users/{id}/effective-permissions`, `POST /users/{id}/password` | `UMS.USERS.USER.*` |
| Policies | `GET /policies`, `PUT/DELETE /policies/{id}` | `UMS.POLICIES.POLICY.*` |
| Schedules | `GET /schedules`, `PUT/DELETE /schedules/{id}`, `GET/POST /holidays`, `DELETE /holidays/{id}` | `UMS.POLICIES.SCHEDULE.*` |
| Decision | `POST /authorize` (body `AccessRequestDto`, returns `{allowed, reason, steps[]}`); `POST /authorize/week` (same request for every hour of the week, used by the simulator heat-map) | `UMS.POLICIES.DECISION.VIEW` |
| Logs | `GET /audit-logs`, `GET /user-actions`, `GET /exceptions`, `PUT /exceptions/{id}/resolve` | `UMS.LOGS.*` |
| Me (for protected apps) | `POST /me/authorize`, `POST /me/authorize/batch` (up to 2000 checks), `GET /me/directory?search=` — the user and tenant come from the caller's own token, so an app can only ask about its signed-in user | signed in |

### Sign-in sessions

Every sign-in creates a `UserSessions` row (`database/002_sessions_and_lockout.sql`) for one application:
- The access token carries the session as `sid` (with `app`, `tenant_id`, `emp_code`) and lives `JwtExpiryMinutes`.
- The refresh token is `{sid}.{random}`; only its SHA-256 hash is stored. Each refresh rotates it, and presenting an
  old one ends the session (`TokenReuse`).
- JWT validation checks the session on every request (cached 10 seconds; ending a session clears the cache). A
  session that ended or ran out answers 401 with `X-Session-End: Replaced | Idle | Expired | RevokedByAdmin |
  TokenReuse | SignedOut | AccountDisabled`, so clients can tell the user why.
- With `SingleDeviceLogin`, a new sign-in ends the user's open sessions in the same application (`Replaced`).
- Idle: no activity for `SessionTimeoutMinutes` (clients report activity through `GET /auth/session?touch=true`).
  Absolute lifetime: `RefreshTokenDays`.
- Lockout: `LoginAttempts` counts failures per login; `MaxFailedLogins` locks for `LockoutMinutes`.

Errors are RFC 7807 problem responses:
- 400 for validation and domain-rule failures
- 401 when the caller is not signed in, with `code` (`auth.invalid`, `auth.locked`, `session.replaced`, …) and the
  `X-Session-End` header when a session ended
- 403 with the deny reason when a permission check fails
- 404 for unknown records
- 500 for unexpected errors, which are also written to `ExceptionLogs`

## 5. Scale notes (12,000+ users, 50+ applications)

* Server-side paging and filtering for users and logs. The UI already pages client-side, so switching to the API
  only changes the data source.
* `AccessContextProvider` currently loads a tenant's data on every decision. That is fine in memory, but for SQL
  Server, cache it (in memory or Redis) and invalidate on role, assignment or policy change.
* `GET /users` returns the whole tenant, because the panel filters on the client. For 12,000+ users, add server-side
  paging (the query already accepts filters).
* Cache each user's effective permission set and the tenant's compiled policies in Redis. Invalidate on role,
  assignment or policy change (publish an event from `IAuditService`).
* Logs grow fast: partition `UserActions` / `AuditLogs` by month, or ship them to Elasticsearch/Seq.
* Bulk onboarding through CSV import or HRMS → UMS SCIM sync. Leavers are deactivated automatically by the HRMS
  feed or by `AccountExpiry`.
* Delegated administration: give app owners a role like `UMS_ADMIN` with a scope `{"app":"POS"}` so they can
  manage only their own application's roles.

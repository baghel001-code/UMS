# VMM User Management System – Access Control Architecture

This document explains the access model behind the admin panel (`admin-panel/`). It also defines the backend
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

The reference implementation is `admin-panel/src/engine/pdp.ts`, covered by unit tests in `pdp.test.ts`.
The backend `IAuthorizationService` must port this algorithm unchanged, so the simulator in the admin panel and
production give the same answer.

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
 50+ apps (in-house & third-party)            Admin panel (this repo, React)
 ┌──────────────────────────────┐             ┌───────────────────────────┐
 │ PEP: auth middleware / SDK   │             │ Users · Roles · Policies  │
 │  - validates JWT (tenant,sub)│             │ Apps · Schedules · Logs   │
 │  - POST /api/authorize       │             │ Access Simulator          │
 └──────────────┬───────────────┘             └─────────────┬─────────────┘
                │                                           │ REST (admin scope)
        ┌───────▼───────────────────────────────────────────▼──────┐
        │ UMS API (ASP.NET Core)                                    │
        │  TenantResolver → ExceptionLogger → Authentication (JWT)  │
        │  → Authorization (PDP) → AuditLogger                      │
        │  IAuthService · IAuthorizationService · IAuditService      │
        │  IUserActionService · IExceptionLogger                     │
        └───────┬─────────────────────────────┬────────────────────┘
                │                             │
        SQL Server / PostgreSQL         Redis (permission & policy cache,
        (tables below, TenantId on all)  refresh tokens) · Serilog → Seq/ELK
```

* **Third-party apps** that cannot call `/api/authorize` per request receive coarse permissions as JWT/SAML claims
  (`permissions: ["HRMS.ESS.*.VIEW", …]`) via SSO (OIDC/SAML), or are provisioned via SCIM.
* **In-house apps** call `/api/authorize` (or use the shared .NET SDK that caches the user's effective
  permissions and policies for a short TTL and evaluates locally).

## 3. Database tables

All tables carry `TenantId`. EF Core global query filters enforce tenant isolation.

| Table | Key columns |
|-------|-------------|
| `Tenants` | Id, Code, Name, IsActive, CreatedOn |
| `TenantSettings` | TenantId, Key, Value (password policy, MFA, token lifetimes, trusted IP ranges) |
| `Applications` | Id, Code (unique), Name, Type (InHouse/ThirdParty), Vendor, OwnerDept, IsActive |
| `ApplicationTenants` | ApplicationId, TenantId |
| `Modules` | Id, ApplicationId, Code, Name |
| `Resources` | Id, ModuleId, Code, Name |
| `ResourceActions` | ResourceId, Action |
| `Roles` | Id, TenantId, Code, Name, Description, ApplicationId (nullable = cross-app), IsSystem, IsActive |
| `RolePermissions` | RoleId, PermissionKey (`APP.MODULE.RESOURCE.ACTION`, wildcards allowed) |
| `Users` | Id, TenantId, EmpCode, FullName, Email, Phone, Department, Designation, Location, Region, EmploymentType, Status, MfaEnabled, PasswordHash, LastLogin, AccountExpiry |
| `UserAttributes` | Id, UserId, TenantId, AttributeKey, AttributeValue, LastUpdated |
| `UserRoles` | UserId, RoleId, ScopeJson (e.g. `{"store":"S045,S046"}`), ValidFrom, ValidTo, AssignedBy, AssignedOn |
| `Policies` | Id, TenantId, Name, Description, Effect, TargetApp, TargetModule, TargetResource, TargetActions, TargetRoles, Match (all/any), ConditionsJson, RuleExpression, Priority, IsActive, CreatedBy, CreatedAt |
| `ResourceAttributes` | Id, TenantId, ResourceType, ResourceId, AttributeKey, AttributeValue (for resources whose attributes UMS must look up itself) |
| `Schedules` | Id, TenantId, Code, Name, TimeZone, ExcludeHolidays, WindowsJson |
| `Holidays` | Id, TenantId, Date, Name, Regions |
| `AuditLogs` | Id, TenantId, UserId/Actor, EntityName, EntityId, Action, OldValues, NewValues, Timestamp, IP, Device |
| `UserActions` | Id, TenantId, UserId, App, Route, Action (permission key), Decision, Reason, Timestamp, IP |
| `ExceptionLogs` | Id, TenantId, App, Severity, Message, StackTrace, UserId, RequestPath, QueryString, IP, Timestamp, Resolved |
| `LoginAttempts` | Id, TenantId, UserName, Success, IP, Device, Timestamp |

`ConditionsJson` stores the structured conditions the admin builds in the UI. `RuleExpression` stores the
rendered text (e.g. `InSchedule(env.now,'GENERAL') && env.network in ['Office LAN','VPN']`) for display and
audit only. The backend evaluates the structured conditions, never a free-text expression, so the rules
cannot be used for expression injection.

## 4. REST API used by the admin panel

All routes are under `/api/v1`, need an admin JWT with the `UMS.*` permission for the area, and are tenant-scoped by
the `X-Tenant-Id` header (or subdomain). Every mutating call writes an `AuditLogs` row with old/new values.

| Area | Endpoints |
|------|-----------|
| Tenants | `GET/POST /tenants`, `PUT /tenants/{id}`, `GET/PUT /tenants/{id}/settings` |
| Applications | `GET/POST /applications`, `GET/PUT/DELETE /applications/{id}` (modules, resources and actions are nested) |
| Roles | `GET/POST /roles`, `GET/PUT/DELETE /roles/{id}`, `GET /roles/{id}/members` |
| Users | `GET /users?q=&dept=&role=&region=&status=&page=&size=`, `POST /users`, `GET/PUT /users/{id}`, `POST /users/import` (CSV), `GET /users/export` |
| User access | `POST /users/{id}/roles` (roleId, scope, validFrom, validTo), `DELETE /users/{id}/roles/{roleId}`, `PUT /users/{id}/attributes`, `GET /users/{id}/effective-permissions`, `POST /users/{id}/reset-password`, `POST /users/{id}/revoke-sessions`, `POST /users/bulk` (status / role) |
| Policies | `GET/POST /policies`, `GET/PUT/DELETE /policies/{id}` |
| Schedules | `GET/POST /schedules`, `PUT/DELETE /schedules/{id}`, `GET/POST/DELETE /holidays` |
| Decision | `POST /authorize` – body = `AccessRequest`, response = `Decision` (`allowed`, `reason`, `steps[]`). Used by applications **and** by the Access Simulator. |
| Logs | `GET /audit-logs`, `GET /user-actions`, `GET /exceptions`, `PUT /exceptions/{id}/resolve` |

`AccessRequest` / `Decision` JSON shapes are the TypeScript interfaces in `admin-panel/src/types.ts`.

## 5. Scale notes (12,000+ users, 50+ applications)

* Server-side paging and filtering for users and logs. The UI already pages client-side, so switching to the API
  only changes the data source.
* Cache each user's effective permission set and the tenant's compiled policies in Redis. Invalidate on role,
  assignment or policy change (publish an event from `IAuditService`).
* Logs grow fast: partition `UserActions` / `AuditLogs` by month, or ship them to Elasticsearch/Seq.
* Bulk onboarding through CSV import or HRMS → UMS SCIM sync. Leavers are deactivated automatically by the HRMS
  feed or by `AccountExpiry`.
* Delegated administration: give app owners a role like `UMS_ADMIN` with a scope `{"app":"POS"}` so they can
  manage only their own application's roles.

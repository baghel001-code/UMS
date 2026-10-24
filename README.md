# Vishal Mega Mart – User Management System (UMS)

Central identity and fine-grained access control for 50+ in-house and third-party applications and 12,000+
employees, vendors and contractors.

Every request is checked at every level:

**Application → Module → Resource → Action → record scope → time / network / device / attributes**

The checks combine:
- **RBAC**: roles hold permissions such as `POS.BILLING.INVOICE.REFUND`.
- **Resource scopes**: a role can be limited to specific records, e.g. stores S045 and S046 only.
- **ABAC policies**: office hours, holidays, amount limits, MFA, own record only.

The system is **multi-tenant** and writes an **audit trail**, a **user action trail** and **exception logs**.

## Solution layout

```
UMS_Sol.sln
├── DomainLayer/          Entities (User, Tenant, Application, Role, Policy, Schedule, …), PermissionKey,
│                         AccessDecisionService (the decision engine), ScheduleEvaluator, ConditionEvaluator
├── ApplicationLayer/     MediatR commands/queries under Features/AccessControl/*, DTOs, validators,
│                         repository & service ports (Interfaces/), AccessContextProvider
├── Infrastructure/       Persistence/InMemory (demo data), Persistence/SqlServer (Dapper), DemoSeed,
│                         audit/tenant/password/token services, DI (AddAccessControl)
├── WebApi/               REST API /api/v1 (Controllers/V1), ExceptionLoggingMiddleware,
│                         RequirePermission filter (UMS protects itself with its own engine), JWT auth
├── Tests/UMS.Tests/      xUnit tests: decision engine, schedules, storage formats
├── database/             001_create_access_control_schema.sql (SQL Server)
├── admin-panel/          React + TypeScript admin console (prismatic blue / white theme)
└── docs/ARCHITECTURE.md  Access model, API contract, schema, scaling notes
```

## Run it

### 1. API (in-memory demo data, no database needed)

```bash
cd WebApi
dotnet run --launch-profile http          # http://localhost:5125, Swagger UI at /
```

`appsettings.Development.json` turns on:
- `Persistence:Provider = InMemory`: about 300 demo users, 13 applications, 21 roles and 11 policies in the main tenant
- `Security:RequireAuthentication = false`: so the panel works without signing in
- CORS for the admin panel on ports 5173 and 4173

### 2. Admin panel

```bash
cd admin-panel
npm install
cp .env.example .env.local                # VITE_API_URL=http://localhost:5125
npm run dev                               # http://localhost:5173
```

Without `VITE_API_URL` the panel runs on its own demo data kept in the browser.

### 3. Tests

```bash
dotnet test                               # 28 xUnit tests (engine, schedules, storage formats)
cd admin-panel && npm test                # 22 Vitest tests (same scenarios as the C# engine)
```

### Signing in (secured mode)

Set `Security:RequireAuthentication = true`. The admin panel then shows a sign-in page, and every admin API
call is checked against the caller's own `UMS.*` permissions: roles plus ABAC policies, like any other application.

In development, the seeded UMS administrator signs in as `ankit.saxena@vmm.example`. The password is
`Persistence:DemoAdminPassword` from `appsettings.Development.json`. Change it, and never use it outside
development. Administrators can set passwords for other users from the Users page.

### SQL Server

1. Create a database and run `database/001_create_access_control_schema.sql`.
2. In configuration set:
   - `Persistence:Provider = SqlServer`
   - `ConnectionStrings:Default`
   - `Jwt:Secret` (at least 32 characters; use user-secrets or an environment variable, not appsettings)
3. Optionally set `Persistence:SeedDemoData = true` and `Persistence:DemoAdminPassword` to load the demo data
   into an empty database on first start.

## Admin panel

| Page | What you can do |
|------|-----------------|
| Dashboard | Welcome banner with quick actions, key figures, the 7 decision steps, access decisions per app, users by status/department, items needing attention, recent changes |
| Applications & Modules | Register in-house and third-party apps, enable them per tenant, define modules → resources → allowed actions |
| Users | Search and filter; bulk activate, lock or assign a role; CSV import/export; per-user roles (with scope and validity dates), ABAC attributes, effective access, activity; set password |
| Roles & Permissions | Permission matrix per application (with wildcards) and a role × application coverage matrix |
| ABAC Policies | Visual builder: Restrict / Deny / Grant, target, conditions on `user.*`, `resource.*`, `env.*`, templates, live rule text |
| Office Hours & Holidays | Weekly schedules (overnight shifts supported) and a regional holiday calendar |
| Access Simulator | Pick user, app/module/resource/action, record attributes and time/network/device. See PERMIT/DENY step by step and a weekly heat-map |
| Audit Trail · User Action Trail · Exception Logs | Before/after diffs, every allow/deny decision, captured exceptions |
| Tenants · Security Settings | Tenant isolation; password, lockout, MFA and token policy |

## Integrating an application

Applications ask UMS before acting:

```http
POST /api/v1/authorize
X-Tenant-Id: <tenant id>
Authorization: Bearer <service token>

{ "empCode": "VMM10003", "app": "POS", "module": "BILLING", "resource": "INVOICE", "action": "Refund",
  "resourceAttributes": { "store": "S045", "amount": "15000" },
  "env": { "channel": "POS Terminal" }, "route": "/billing/invoice/8812/refund" }
```

The response is `{ "allowed": false, "reason": "...", "steps": [ ... ] }`, and the decision is written to the
user action trail.

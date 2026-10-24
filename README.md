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

The solution also contains the **VMM Asset Management System (AMS)**, a Snipe-IT style application for every kind
of company asset. AMS has no users, roles or permission rules of its own: people sign in with their UMS account,
and UMS authorises every action (see [docs/ASSET_MANAGEMENT.md](docs/ASSET_MANAGEMENT.md)).

## Solution layout (`UMS_Sol.sln`)

```
src
├── DomainLayer/          Entities (User, Tenant, Application, Role, Policy, Schedule, …), PermissionKey,
│                         AccessDecisionService (the decision engine), ScheduleEvaluator, ConditionEvaluator
├── ApplicationLayer/     MediatR commands/queries under Features/AccessControl/*, DTOs, validators,
│                         repository & service ports (Interfaces/), AccessContextProvider
├── Infrastructure/       Persistence/InMemory (demo data), Persistence/SqlServer (Dapper), DemoSeed,
│                         audit/tenant/password/token services, DI (AddAccessControl)
├── WebApi/               REST API /api/v1 (Controllers/V1), ExceptionLoggingMiddleware,
│                         RequirePermission filter (UMS protects itself with its own engine), JWT auth
└── UMS.AdminPortal/      Blazor Server + MudBlazor admin console (prismatic blue / white theme).
                          Calls WebApi over HTTP; reuses the ApplicationLayer DTOs.
AssetManagement           Asset Management System – a client application protected by UMS
├── AMS.Core/             Entities (assets, licences, accessories, consumables, components, requests, …) and
│                         services; every operation asks IAccessGuard (UMS) with the record's attributes
├── AMS.Infrastructure/   EF Core (SQLite for development, SQL Server), tenant filter, demo data,
│                         UmsClient + UmsAccessGuard (calls /api/v1/me/authorize with the user's token)
└── AMS.Web/              Blazor Server + MudBlazor UI, same prismatic blue / white theme
tests
├── UMS.Tests/            xUnit tests: decision engine, schedules, storage formats, AMS policies
└── AMS.Tests             (AssetManagement/AMS.Tests) services always ask UMS, list filtering, guard client
database/                 001_create_access_control_schema.sql (SQL Server)
docs/ARCHITECTURE.md      Access model, API contract, schema, scaling notes
```

## Run it in Visual Studio

1. Open `UMS_Sol.sln`.
2. In the startup-project dropdown on the toolbar, choose **API + Admin Portal**. This profile comes from
   `UMS_Sol.slnLaunch` and needs Visual Studio 2022 17.11 or newer.
   - On older versions, right-click the solution → **Configure Startup Projects…** → **Multiple startup
     projects**, and set **WebApi** and **UMS.AdminPortal** to *Start*, both using the **http** profile.
3. Press **F5**.
   - The API runs at `http://localhost:5125` (Swagger UI at `/`).
   - The admin portal opens at **`http://localhost:5200`**.
4. To run Asset Management as well, choose **API + Admin Portal + Asset Management** instead. AMS opens at
   **`http://localhost:5300`**.

In Development the API uses in-memory demo data (about 300 users, 13 applications, 21 roles and 11 policies in
the main tenant) and does not require sign-in. Changes are kept until the API stops.

From the command line, run each in its own terminal:

```bash
dotnet run --project WebApi --launch-profile http
dotnet run --project UMS.AdminPortal --launch-profile http
dotnet run --project AssetManagement/AMS.Web --launch-profile http
dotnet test UMS_Sol.sln                    # 56 xUnit tests (UMS 36 + AMS 20)
```

The portal finds the API through `UmsApi:BaseUrl` in `UMS.AdminPortal/appsettings.json`.

### Signing in (secured mode)

Set `Security:RequireAuthentication = true` in the WebApi settings. The portal then shows a sign-in page, and
every admin call is checked against the caller's own `UMS.*` permissions (roles plus ABAC policies), like any
other application.

In development, the seeded UMS administrator signs in as `ankit.saxena@vmm.example`. The password is
`Persistence:DemoAdminPassword` from `WebApi/appsettings.Development.json`. Change it, and never use it outside
development. Administrators can set passwords for other users in the user dialog.

### SQL Server

1. Create a database and run `database/001_create_access_control_schema.sql`.
2. In the WebApi configuration set:
   - `Persistence:Provider = SqlServer`
   - `ConnectionStrings:Default`
   - `Jwt:Secret` (at least 32 characters; use user-secrets or an environment variable, not appsettings)
3. Optionally set `Persistence:SeedDemoData = true` and `Persistence:DemoAdminPassword` to load the demo data
   into an empty database on first start.

## Admin portal pages

| Page | What you can do |
|------|-----------------|
| Dashboard | Welcome banner with quick actions, key figures, the 7 decision steps, access decisions per app, users by status/department, items needing attention, recent changes |
| Applications & Modules | Register in-house and third-party apps, enable them per tenant, define modules → resources → allowed actions |
| Users | Search and filter; bulk activate, lock or assign a role; CSV import/export; per-user roles (with scope and validity dates), ABAC attributes, effective access, activity; set password |
| Roles & Permissions | Permission matrix per application (with wildcards), role × application coverage matrix, members, raw keys |
| ABAC Policies | Visual builder: Restrict / Deny / Grant, target, conditions on `user.*`, `resource.*`, `env.*`, templates, live rule preview |
| Office Hours & Holidays | Weekly schedules (overnight shifts supported) and a regional holiday calendar |
| Access Simulator | Pick user, app/module/resource/action, record attributes and time/network/device. The API returns PERMIT/DENY step by step, plus a weekly heat-map |
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

## Asset Management System (AMS)

AMS covers what Snipe-IT does:
- **Assets** with tags, models, serials, status labels, locations, departments, suppliers, purchase and warranty
  data. Assets can be checked out and in (with an expected return date), audited, and printed as tag labels.
- **Licences** with seats given to people or installed on assets. Product keys are hidden unless the user may edit.
- **Accessories** (lent and returned), **consumables** (issued and used up, with re-order levels) and
  **components** (installed into assets).
- **Asset requests** with approval, **maintenance** (repairs, upgrades, warranty work), **My Assets** for every
  employee, **reports** (depreciation, warranty, audits due, overdue check-ins), the **activity log**, CSV exports,
  and setup lists.

Sign in with a UMS account. Demo accounts (development, `Persistence:DemoUserPassword` / `DemoAdminPassword` in
`WebApi/appsettings.Development.json`):

| Who | Email | What UMS gives them in AMS |
|-----|-------|----------------------------|
| Ankit Saxena (UMS super admin) | `ankit.saxena@vmm.example` | Everything |
| Rahul Agarwal | `rahul.agarwal@vmm.example` | `ASSET_ADMIN`: everything, but cannot approve his own requests |
| Sneha Iyer | `sneha.iyer@vmm.example` | `ASSET_MANAGER` scoped to location `HO-GGN`; cannot dispose assets over ₹1 lakh |
| Priya Verma (cashier) | `priya.verma@vmm.example` | `EMPLOYEE`: My Assets, request an asset, see and cancel own requests |

To change who can do what in AMS, use the UMS admin portal: roles, role scopes, office hours and ABAC
policies for application `AMS`. No AMS code or configuration changes are needed.

In Development, AMS uses a SQLite file (`ams-dev.db`). It fills it with demo data for the main tenant on the first
sign-in. For production, set `Ams:Database:Provider = SqlServer` and `ConnectionStrings:Ams`; the schema is created
on start-up.

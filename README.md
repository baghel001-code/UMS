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
AssetManagement           Asset Management System – a client application protected by UMS (clean architecture)
├── AMS.Domain/           Request/response models (BaseResponse: responseCode, responseMessage, recordId),
│                         read models, enums, exceptions, the AMS permission catalogue (identifiers only)
├── AMS.Application/      Use cases (assets, licences, stock, requests, maintenance, setup, reports); every one
│                         asks IAccessGuard (UMS) first. Ports: repositories, IAPICall, e-mail (INotifier<T>)
├── AMS.Infrastructure/   Dapper + stored procedures (DapperHelper), UmsClient / UmsAccessGuard, SMTP e-mail
│                         queue and daily digest, ApiCallService (IAPICall) used by the portal
├── AMS.Api/              REST API POST /api/v1/{action}; validates the UMS token (app AMS, session id);
│                         the only part of AMS that touches the database
├── AMS.Portal/           Blazor Server + MudBlazor web portal; signs in through UMS, calls AMS.Api only.
│                         Single-device login, idle timeout, token refresh
└── database/             001_ams_schema.sql, 002_ams_procedures.sql (SQL Server)
tests
├── UMS.Tests/            xUnit tests: decision engine, schedules, storage formats, sessions, AMS policies
└── AMS.Tests             (AssetManagement/AMS.Tests) use cases, API client, UMS guard, e-mail, SQL procedures
database/                 001 schema, 002 sign-in sessions and lockout (SQL Server)
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
4. To run Asset Management as well, choose **API + Admin Portal + Asset Management** instead. This starts
   `AMS.Api` (`http://localhost:5310`, Swagger at `/swagger`) and `AMS.Portal` at **`http://localhost:5300`**.
   AMS needs SQL Server (see [Asset Management database](#asset-management-database)).

In Development the API uses in-memory demo data (about 300 users, 13 applications, 21 roles and 11 policies in
the main tenant) and does not require sign-in. Changes are kept until the API stops.

From the command line, run each in its own terminal:

```bash
dotnet run --project WebApi --launch-profile http
dotnet run --project UMS.AdminPortal --launch-profile http
dotnet run --project AssetManagement/AMS.Api --launch-profile http
dotnet run --project AssetManagement/AMS.Portal --launch-profile http
dotnet test UMS_Sol.sln                    # 74 xUnit tests (UMS 45 + AMS 29; 4 need AMS_TEST_SQL)
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

1. Create a database and run `database/001_create_access_control_schema.sql`, then
   `database/002_sessions_and_lockout.sql`.
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

### Asset Management database

AMS keeps its data in its own SQL Server database, read and written only by `AMS.Api` through Dapper and the stored
procedures in `AssetManagement/database/`. For development, any SQL Server 2019+ works; with Docker:

```bash
docker run -d --name sql -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD='Dev_Passw0rd!' -p 1433:1433 mcr.microsoft.com/mssql/server:2022-latest
```

`AMS.Api/appsettings.Development.json` points at that server (`ConnectionStrings:Ams`), has `Ams:Database:AutoMigrate`
on and sets `DemoTenantId`: on start-up the API creates database `VMM_AMS`, runs the schema once, refreshes the
procedures and fills an empty database with demo data. In production, DBAs run the two scripts and `AutoMigrate`
stays off.

E-mails (check-out, check-in, asset requests, decisions, daily digest) go to `AMS.Api/MailDrop/*.eml` in development.
Set `EmailServerSettings` (SMTP) and `Notifications` for real mail.

### Sessions and security

- **Single-device login.** Signing in again ends the user's other session in the same application. The other
  browser is signed out with *"You signed in on another device"*. The switch is in UMS **Security Settings**.
- **Idle timeout.** `SessionTimeoutMinutes` (Security Settings) signs inactive users out after a one-minute warning.
  The browser and the server both watch, and the UMS session is closed as *Idle*.
- **Tokens.** UMS issues a short-lived access token (`JwtExpiryMinutes`) bound to the session (`sid` claim) and a
  rotating refresh token (`RefreshTokenDays`). A reused refresh token ends the session. Every UMS-protected call
  checks the session, so a revoked or replaced session stops working at once.
- **Lockout.** `MaxFailedLogins` failed attempts lock the account for `LockoutMinutes`.
- **Active Sessions** in the UMS admin portal lists who is signed in to which application, from where. An
  administrator can end any session; AMS signs that user out within a minute.

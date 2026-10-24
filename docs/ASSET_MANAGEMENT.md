# VMM Asset Management System (AMS) – How Access Works

AMS manages company assets the way Snipe-IT does. It holds **no access rules**. It has no users, no roles and no
`if (role == …)` checks. UMS is the only place access is decided:

```
 Browser ──► AMS.Portal (Blazor Server) ──IAPICall──► AMS.Api ──► use case ──► IAccessGuard ──► UMS  POST /api/v1/me/authorize
   cookie       │ sign-in, refresh, logout,      POST          │ (UMS token,  │                    (user's own token,
                │ session checks                 api/v1/{x}     │  app = AMS)  └─► repository ─► Dapper ─► SQL Server
                └──────────────────────────────► UMS /api/v1/auth/*                                (stored procedures)
```

## Projects (clean architecture)

| Project | Depends on | Responsibility |
|---------|-----------|----------------|
| `AMS.Domain` | – | Request/response contracts (`BaseResponse`: `responseCode`, `responseMessage`, `recordId`), read models, enums, exceptions, the permission catalogue (`Perm`, identifiers only) |
| `AMS.Application` | Domain | Use cases (`Services/*UseCases.cs`), ports (`IAssetRepository`…, `IAccessGuard`, `ICurrentActor`, `IAPICall`, `INotifier<T>`), e-mail templates and recipient resolvers |
| `AMS.Infrastructure` | Application | Dapper repositories over stored procedures (`DapperHelper`), `UmsClient` / `UmsAccessGuard` / `UmsPeopleDirectory`, SMTP sender + notification queue + daily digest, `ApiCallService` |
| `AMS.Api` | Infrastructure | Controllers `POST api/v1/{action}`, UMS JWT validation, exception → response mapping, Serilog, Swagger |
| `AMS.Portal` | Infrastructure (API client only) | Blazor Server UI. Never touches the database: every page calls `IAPICall.CallingAPI<TRes,TReq>(AmsApiNames.X, req)` |

Each use case depends only on interfaces; the API and the portal are composition roots (`AddAmsApiInfrastructure`,
`AddAmsPortalInfrastructure`). The API names live in `AmsApiNames`; `AmsApi:Routes` can map a name to another route.

## Authentication and sessions

- **Sign-in.** The portal's sign-in page posts to UMS `POST /api/v1/auth/login` with application code `AMS` and the
  browser's IP address and user agent. UMS checks the password and lockout, that AMS is enabled for the tenant, and
  (with single-device login) ends the user's other AMS session. The portal keeps the UMS access and refresh
  tokens on the server, never in the browser. A single-use ticket hands the sign-in to `POST /account/complete-login`,
  which writes an HTTP-only cookie (`__Host-AMS.Auth` outside development).
- **Calls to the API** carry the UMS access token. `AMS.Api` validates it locally (UMS issuer, audience and key,
  `app = AMS`, a session id); UMS checks the session again on every authorisation call. Tokens are refreshed
  shortly before they expire, one refresh at a time per session.
- **Session end.** When UMS reports a session as ended (`X-Session-End`), the API answers 401 with the same header
  and the portal signs the user out with the reason: *signed in on another device*, *idle*, *ended by an
  administrator*, *expired*. Open pages are re-checked every `Auth:RevalidationSeconds`, and every HTTP request
  re-checks the cookie.
- **Idle timeout.** `SessionTimeoutMinutes` from UMS Security Settings. The browser tracks activity (shared across
  tabs) and calls `/account/keepalive`, which renews the cookie and tells UMS the user is active. A warning appears
  a minute before the timeout; then the portal signs out and closes the UMS session as *Idle*. The server also
  signs out circuits that have been idle that long.
- **Sign-out** is an antiforgery-protected POST; it ends the UMS session and clears the cookie.

## Authorisation

- Every use case calls the guard **before** it reads or changes data, with the permission and the attributes of
  the specific record. A denial throws `AccessDeniedException` carrying UMS's reason, for example *"Policy: Deny
  'High-value assets not issued to contract staff' — resource.cost (171500) > '150000'"*. The API returns 403 with
  that reason, and the portal shows it.
- **Lists** are filtered record by record with one batch call (`POST /api/v1/me/authorize/batch`). The same batch
  tells the UI which buttons to show on each row. Menu items use type-level checks (`GetCapabilities`). These only
  shape the UI; the use case always checks again when an action runs.
- **Edits** are checked twice: on the record as it is, and on the record as it will be. Moving an asset to a
  location outside your scope is therefore denied.
- **Audit**: every single-action decision (allow and deny) is written to the UMS user action trail with app
  `AMS`. AMS also keeps its own history of what was done.

## API responses

| Outcome | HTTP | `responseCode` |
|---------|------|----------------|
| Success | 200 | 0 |
| Session ended | 401 + `X-Session-End` | 401 |
| Denied by UMS | 403 | 403 (message = UMS reason) |
| Not found | 404 | 1001 |
| Business rule (stored procedure or use case) | 422 | 1002 |
| Duplicate | 409 | 1003 |
| UMS unreachable (fail closed) | 503 | 503 |
| Unexpected | 500 | 1004 (logged) |

Stored procedures return `@Error_Code` / `@Error_Message` (and `@Record_Id` on inserts) with the same codes.

## E-mail

Sending is decoupled from the use cases: a use case publishes a model (`AssetCheckedOutModel`,
`RequestSubmittedModel`, …) to an in-memory queue. A background worker resolves it with `EmailNotifier<T>`:
- `IRecipientResolver<T>` decides who gets it;
- `IEmailTemplate<T>` builds the subject and body;
- `IRecipientPolicy` drops invalid addresses and addresses outside `Notifications:AllowedDomains`.

A slow mail server therefore never blocks a user.

| E-mail | To |
|--------|----|
| Asset checked out / checked in | the employee (asset desk in copy) |
| Asset request submitted | `Notifications:AssetDeskEmail` (requester in copy) |
| Request approved / rejected | the requester |
| Daily digest (`DailyDigestTime`, IST) | asset desk: audits due, overdue check-ins, warranties ending, low stock, pending requests |

`EmailServerSettings` configures SMTP; `PickupDirectory` writes `.eml` files instead (development).

## Permission catalogue (application `AMS` in UMS)

| Module | Resource | Actions |
|--------|----------|---------|
| DASHBOARD | OVERVIEW | View |
| ASSETS | ASSET | View, Create, Edit, Delete, Checkout, Checkin, Audit, Export, Print |
| ASSETS | MAINTENANCE | View, Create, Edit, Delete |
| ASSETS | REQUEST | View, Request, Approve, Void |
| LICENSES | LICENSE | View, Create, Edit, Delete, Checkout, Checkin, Export |
| ACCESSORIES | ACCESSORY | View, Create, Edit, Delete, Checkout, Checkin |
| CONSUMABLES | CONSUMABLE | View, Create, Edit, Delete, Checkout |
| COMPONENTS | COMPONENT | View, Create, Edit, Delete, Checkout, Checkin |
| REPORTS | REPORT, ACTIVITY | View, Export |
| SETUP | CATEGORY, MANUFACTURER, MODEL, SUPPLIER, LOCATION, DEPARTMENT, STATUS | View, Create, Edit, Delete |
| SELF | MY_ASSETS | View |

`AMS.Domain/Security/Security.cs` (`Perm`) lists these identifiers. They are names only; no rule is attached
to them in AMS code.

## Record attributes sent to UMS

Policies and role scopes can use these as `resource.<name>`:

| Record | Attributes |
|--------|------------|
| Asset | `assetTag`, `location`, `region`, `cost`, `category`, `status`, `statusType`, `department`, `requestable`, `assignedToEmpCode` |
| Check-out (any item) | the item's attributes plus `assigneeEmpCode`, `assigneeEmploymentType`, `assigneeDepartment`, `assigneeLocation`, `assigneeRegion` (from the UMS directory) |
| Request | the asset's attributes plus `requestedByEmpCode`, `requestStatus` |
| Maintenance | the asset's attributes; `cost` is the job cost and `assetCost` the asset's cost |
| Licence | `cost`, `seats`, `category`, `manufacturer` (plus `location` of the target asset on check-out) |
| Accessory / consumable / component | `location`, `region`, `category`, `cost` (unit cost × quantity), `quantity` |
| Setup record | `name`; locations also `location`, `region` |
| Activity entry | `location` of the item |

Location codes match UMS user locations (`S045`, `DC-NR1`, `HO-GGN`, …). A role assigned with scope
`{"location": "HO-GGN"}` therefore limits that person to head-office assets, stock and setup data.

The guard also sends the environment (`env.*`):
- IP address of the browser.
- Network: *Office LAN* for private addresses, else *Internet*.
- Device: `Ums:Device`.
- Channel: *Web*.

UMS adds the time in the tenant's time zone, so office-hour schedules apply.

## Seeded examples (development)

All of these are data in UMS. Edit them in the admin portal; nothing in AMS needs to change.

**Roles**

| Role | Permissions |
|------|-------------|
| `ASSET_ADMIN` | `AMS.*.*.*` |
| `ASSET_MANAGER` | Everything except setup changes. Setup is view-only. |
| `EMPLOYEE` | My Assets, plus request, view and cancel own asset requests |
| `IT_SUPPORT` | Dashboard and asset view |

**Policies**

| Policy | Rule |
|--------|------|
| Asset check-out only during operating hours | *Restrict* Checkout to the store schedule |
| Managers cannot dispose high-value assets | *Deny* Delete for `ASSET_MANAGER` when `resource.cost > 100000` |
| Employees see assets issued to them | *Grant* View when `resource.assignedToEmpCode == user.empCode` |
| No self-approval of asset requests | *Deny* Approve when `resource.requestedByEmpCode == user.empCode` |
| Asset request approval needs MFA | *Restrict* Approve to users with MFA |
| High-value assets not issued to contract staff | *Deny* Checkout when `cost > 150000` and the assignee is Contract / Intern / Vendor |
| Asset requests: own or asset staff | *Restrict* View and Void to the requester or asset staff |
| No export from unmanaged devices | Tenant-wide; also applies to AMS exports |

## Design notes

- **Fail closed.** If UMS cannot be reached, the API answers 503 and nothing is shown or changed. If UMS answers
  401, the user is signed out.
- **Tenant isolation.** Every table has `TenantId`, and every procedure takes `@TenantId` from the signed-in user's
  UMS token, never from the request body.
- **Data rules in SQL.** The stored procedures enforce them for any caller, for example one holder per asset, seat
  and stock limits, and unique asset tags.
- **Database changes.** `database/001_ams_schema.sql` (tables) and `002_ams_procedures.sql` (view and procedures,
  safe to re-run). `Ams:Database:AutoMigrate` runs them on start-up in development.
- **Scale.** A list costs one batch call per page load (at most 2000 checks per call; the client splits larger
  ones). For large estates, add server-side paging to the procedures, and cache UMS's compiled context per tenant
  (see ARCHITECTURE.md §5).
- **Logs.** Serilog writes `Logs/` per level in both the API and the portal. The API logs every UMS denial with
  the permission and the reason.

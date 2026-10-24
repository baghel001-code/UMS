# VMM Asset Management System (AMS) – How Access Works

AMS manages company assets the way Snipe-IT does. It holds **no access rules**. It has no users, no roles and no
`if (role == …)` checks. UMS is the only place access is decided:

```
 Browser ──► AMS.Web (Blazor) ──► AMS service ──► IAccessGuard ──► UMS  POST /api/v1/me/authorize
                                       │                            (user's own token, record attributes)
                                       └─ acts only on "allowed": true; otherwise shows UMS's reason
```

- **Authentication**: the AMS sign-in page posts to UMS `POST /api/v1/auth/login`. AMS keeps the returned UMS
  token for the browser session and sends it with every UMS call. The user and tenant always come from that
  token, so AMS can only ask about the person who is signed in.
- **Authorisation**: every service method (`AMS.Core/Services/*`) calls the guard **before** it reads or changes
  data, with the permission and the attributes of the specific record. A denial throws `AccessDeniedException`
  carrying UMS's reason, for example *"Policy: Deny 'High-value assets not issued to contract staff' — resource.cost
  (171500) > '150000'"*. That reason appears in the UI.
- **Lists** are filtered record by record with one batch call (`POST /api/v1/me/authorize/batch`). The same batch
  tells the UI which buttons to show on each row. Menu items use type-level checks. These checks only shape the
  UI; the service always checks again when an action runs.
- **Edits** are checked twice: on the record as it is, and on the record as it will be. Moving an asset to a
  location outside your scope is therefore denied.
- **Audit**: every single-action decision (allow and deny) is written to the UMS user action trail with app
  `AMS`. AMS also keeps its own activity log of what was done.

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

`AMS.Core/Security/AccessGuard.cs` (`Perm`) lists these identifiers. They are names only; no rule is attached
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

- **Fail closed.** If UMS cannot be reached or answers 401, nothing is shown and nothing is changed. The user is
  asked to sign in again or retry.
- **Tenant isolation.** `AmsDbContext` filters every query by the signed-in user's UMS tenant. It refuses writes
  to rows of another tenant.
- **Scale.** A list costs one batch call per page load (at most 2000 checks per call; the client splits larger
  ones). For large estates, add server-side paging to the services, and cache UMS's compiled context per tenant
  (see ARCHITECTURE.md §5).
- **Schema.** `EnsureCreated` builds the schema on start-up. Switch to EF Core migrations once the model has to
  evolve in production.

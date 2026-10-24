# Vishal Mega Mart – User Management System (UMS)

Admin control panel for central identity and fine-grained access control across 50+ in-house and third-party
applications and 12,000+ employees, vendors and contractors.

Access is controlled at every level:

**Application → Module → Resource → Action → (record scope) → (time, network, device, attributes)**

It combines **RBAC** (roles holding permissions such as `POS.BILLING.INVOICE.REFUND`), **resource-based scopes**
(a role valid only for stores S045 and S046), and **ABAC policies** (office hours, holidays, amount limits, MFA,
own-record-only). It is **multi-tenant** and records an **audit trail**, a **user action trail** and
**exception logs**.

## Admin panel features

| Page | What you can do |
|------|-----------------|
| Dashboard | Users, applications, roles, policies at a glance; top deny reasons; MFA gaps; expiring accounts; recent changes |
| Applications & Modules | Register in-house / third-party apps, enable them per tenant, define modules → resources → allowed actions |
| Users | Search and filter thousands of users; bulk activate / lock / assign role; CSV import & export; per-user drawer with profile, role assignments (scope + validity dates for temporary access), ABAC attributes, effective permissions, activity |
| Roles & Permissions | Permission matrix per application (module / resource × action, wildcards), role × application coverage matrix, members, raw permission keys |
| ABAC Policies | Visual policy builder: effect (Restrict / Deny / Grant), target (app/module/resource/actions/roles), conditions on `user.*`, `resource.*`, `env.*`, templates, live rule expression |
| Office Hours & Holidays | Named weekly schedules (incl. overnight shifts) with a visual week grid; regional holiday calendar |
| Access Simulator | Pick a user, app/module/resource/action, record attributes and time/network/device. See PERMIT/DENY with a layer-by-layer explanation and a weekly heat-map of when access is allowed |
| Audit Trail | Every admin change with before/after JSON diff, actor, IP, device |
| User Action Trail | Allow/deny decisions for end users across apps |
| Exception Logs | Errors from all apps with request context and stack trace; mark resolved |
| Tenants · Security Settings | Tenant isolation; password, lockout, MFA, token lifetimes, trusted IP ranges |

## Run it

```bash
cd admin-panel
npm install
npm run dev        # http://localhost:5173
npm test           # policy engine unit tests
npm run build      # production build in admin-panel/dist
```

The panel currently runs on realistic demo data (13 VMM applications, 21 roles, ~300 users, 11 policies,
4 schedules). The data is kept in the browser's localStorage. **↺ Reset demo** in the top bar restores it.
Replacing the store in `src/store/store.tsx` with calls to the REST API in
[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md#4-rest-api-used-by-the-admin-panel) connects it to the ASP.NET
Core backend.

### Try these in the Access Simulator

* *Cashier bills on POS terminal* → PERMIT. *Cashier bills at 23:30* → DENY (outside store hours).
* *Cashier bills for another store* → DENY at the **Scope** layer.
* *Supervisor refund ₹15,000* → DENY by the refund-limit policy; change amount to 2500 → PERMIT.
* *Finance exec at 8:15 pm* → DENY (General Office Hours policy).
* *Named approver approves PO* → PERMIT through a **Grant** policy, without any procurement role.

## Repository layout

```
admin-panel/
  src/engine/pdp.ts        Policy Decision Point (RBAC + scope + ABAC + schedules) – port to the backend
  src/engine/pdp.test.ts   Unit tests for the decision layers
  src/data/seed.ts         Demo tenants, apps, roles, users, policies, schedules, logs
  src/store/store.tsx      State + audit logging (stand-in for the REST API)
  src/pages/*              Admin screens
docs/ARCHITECTURE.md       Access model, DB schema, API contract, scaling notes
```

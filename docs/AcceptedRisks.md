# Accepted Risks — HardBuild POS / HardwareManagementSystem

> **Status**: Active  
> **Last Updated**: 2026-06-01 (Phase 4.6.2 — Code Reality Cleanup)  
> **Owner**: Development Team

This document records design decisions that introduce a known, bounded risk that
has been consciously accepted rather than resolved in the current phase.  Each
entry states what the risk is, why it was accepted, what mitigates it today, and
what the planned resolution is.

---

## AR-001 — `ApplyTenantScope` includes `TenantId == null` legacy rows

**Category**: Data Isolation / Backward Compatibility  
**Severity**: Low (controlled)

### Description
`ReportsController.TenantScope.cs` (and the shared `ApplyTenantScope<T>` helpers)
filter queries with:

```csharp
.Where(x => x.TenantId == tenantId || x.TenantId == null)
```

This means records with a `NULL` `TenantId` are visible to **every** tenant.

### Why Accepted
The system was originally single-tenant.  When multi-tenancy was introduced
(Phase 4.0), existing rows had no `TenantId` populated.  Back-filling all
historical rows in production with a SQL migration would require downtime and
a confirmed mapping from row → tenant — which is not safe to automate blindly.

### Current Mitigations
- The `BackfillTenantIds.sql` script was removed in Phase 4.6.2 to prevent
  accidental execution.  A manual, DBA-reviewed back-fill must be performed
  before this clause is removed.
- The `|| x.TenantId == null` clause only widens visibility — it never leaks
  data from one explicit tenant to another.
- SuperAdmin and database-level access are required to create rows without a
  `TenantId` in the current application code.

### Planned Resolution
Phase 5.0 — before the first multi-tenant production deployment, perform a
manual DBA back-fill of all `TenantId IS NULL` rows to the correct tenant, then
remove the `|| x.TenantId == null` clause from all `ApplyTenantScope` helpers
and add a database-level `CHECK (TenantId IS NOT NULL)` constraint.

---

## AR-002 — `CustomerLedger` has no direct `TenantId` column

**Category**: Data Isolation  
**Severity**: Low (indirect scoping in place)

### Description
The `CustomerLedger` table records individual AR transactions.  It does **not**
have its own `TenantId` column; instead it is scoped through the parent
`Customer.TenantId` via an `INNER JOIN` or `.Include()`.

### Why Accepted
Adding a `TenantId` column to `CustomerLedger` would require a migration and
back-fill.  The risk surface is already covered: every query that reads
`CustomerLedger` entries joins on `Customer` first, and `Customer` is fully
tenant-scoped.  A `CustomerLedger` row therefore can only be reached if its
parent `Customer` is accessible to the current tenant context.

### Current Mitigations
- All `CustomerLedger` queries in `CustomersController` and
  `ReportsController.ARAP.cs` join through `Customer` and apply
  `ApplyTenantScope` on the `Customer` end.
- There are no direct `_context.CustomerLedgers.Where(...)` calls that bypass
  the Customer join.
- Code review required for any future service that queries `CustomerLedger`
  directly.

### Planned Resolution
Phase 5.0 or the first production data-migration sprint — add `TenantId` to
`CustomerLedger`, back-fill from `Customer.TenantId`, and add an index.
Update all queries to include the direct column filter as a redundant safety
layer.

---

## AR-003 — Database-per-tenant deferred to Phase 5.0

**Category**: SaaS Architecture / Data Isolation  
**Severity**: Medium (current shared-schema is acceptable for soft-launch)

### Description
The current architecture uses a **shared database, shared schema** multi-tenancy
model: all tenants' data lives in the same SQL Server database, isolated only by
the `TenantId` column.

### Why Accepted
SQL Server Express has a **10 GB per-database size limit**.  A database-per-tenant
model would allow each tenant their own isolated database that can grow
independently without impacting other tenants.  However, implementing this
requires dynamic connection-string routing, per-tenant migrations, a tenant
provisioning pipeline, and connection pooling changes — a significant
architectural investment.

For Phase 4.x (soft-launch / pilot tenants), the shared-schema model is
sufficient and carries acceptable risk because:

1. The `TenantId` guard is enforced at the application layer on every query.
2. Pilot tenants will be known, trusted businesses with limited data volumes.
3. The 10 GB Express limit is not a concern at pilot scale.

### Current Mitigations
- Every query uses `ApplyTenantScope` to filter by `TenantId`.
- `TenantGuard.GetEffectiveTenantIdAsync()` has no fallback — it throws if
  `TenantId` cannot be resolved, preventing accidental cross-tenant access.
- SuperAdmin cannot enter tenant operational views.

### Planned Resolution
**Phase 5.0** — implement database-per-tenant with:

- `ITenantConnectionResolver` service that maps `TenantId` → connection string.
- Per-tenant `ApplicationDbContext` factory via `IDbContextFactory<T>`.
- Automated tenant provisioning: create database, run migrations, seed roles.
- Migration runner that applies EF migrations to each tenant database on
  startup (or via a maintenance endpoint).
- Switch to SQL Server Standard/Developer edition for production to remove the
  10 GB constraint.

---

## AR-004 — `NotificationsController` uses `[Authorize]` only (no `PermissionAuthorize`)

**Category**: Authorization  
**Severity**: None (by design)

### Description
`NotificationsController` (actions: `MarkRead`, `MarkAllRead`, `GetUnreadCount`)
is protected with `[Authorize]` rather than `[PermissionAuthorize]`.

### Why Accepted
Notifications are a user-personal UI element rendered in the shared `_Layout.cshtml`
top bar for every authenticated user.  A module permission gate would:

1. Require every role to explicitly carry a `Notifications` module row, and
2. Break the notification dropdown silently for any role where the permission is
   not seeded.

The three actions are non-destructive, user-scoped (delegate to
`NotificationService` which filters by current user identity), and carry no
cross-tenant data risk.

### Planned Resolution
No change required.  If per-role notification visibility control is ever needed
(e.g. BranchManager cannot dismiss certain system notifications), implement it
inside `NotificationService` rather than at the controller authorization layer.

---

*End of Accepted Risks document.*

# RC1.7 — One Tenant = One Database: Direct Provisioning

**Release**: RC1.7  
**Date**: July 2026  
**Strategy**: BuildFlow POS SaaS — One Tenant = One Dedicated SQL Database

---

## Overview

RC1.7 revises the tenant creation flow so that every new tenant immediately gets its own dedicated SQL database at the moment the SuperAdmin clicks **Create Tenant**. No manual provisioning, no Migrate Data step, and no separate Enable Routing step are required for new tenants.

The shared (platform) database retains only platform-level data:
- SuperAdmin identity
- Tenants registry (`Tenants` table)
- Subscription Plans
- Users / ASP.NET Core Identity
- Backup/Restore metadata
- SaaS audit log
- Platform configuration (SystemSettings shared copy)

All operational data — branches, products, sales, stock, POS, reports — is written directly to the tenant's dedicated database from the first login.

---

## New Tenant Creation Flow

When SuperAdmin submits the **Create Tenant** form:

1. **Save tenant record** in `ApplicationDbContext` (shared DB). `tenant.Id` is assigned.
2. **Generate database name** automatically: `HardBuild_{CODE}_{ID}` (SQL-safe, letters/digits/underscores).
3. **Set `DatabaseMode = Dedicated`** and `DatabaseName = generated name`.
4. **Call `TenantDatabaseProvisioningService.ProvisionAsync()`**:
   - Creates the SQL database if it does not exist.
   - Applies all TenantDbContext EF Core migrations.
   - Seeds minimum reference data (SystemSetting, RolePermissions, SubscriptionPlans).
5. **Mark tenant as directly initialized**:
   - `DataMigrated = true`
   - `DataMigratedAtUtc = DateTime.UtcNow`
   - `RoutingEnabled = true`
   - `IsDirectlyProvisioned = true`
6. **Invalidate resolver cache** so the next request routes to the dedicated DB immediately.
7. **Redirect to Tenant Details** (not Index) with a success message.

### Expected state after Create

| Field                  | Value                              |
|------------------------|------------------------------------|
| `DatabaseMode`         | `Dedicated`                        |
| `IsDatabaseProvisioned`| `true`                             |
| `DataMigrated`         | `true`                             |
| `RoutingEnabled`       | `true`                             |
| `IsDirectlyProvisioned`| `true`                             |
| `RoutingActive`        | `true` (composite of all above)    |
| Runtime Database       | **Dedicated**                      |

---

## Why Migration Is No Longer Needed for New Tenants

Legacy tenants were first created in the shared database, accumulated operational data, and then went through a 3-step process:
1. Provision dedicated DB (empty).
2. Migrate Data — bulk-copy all operational rows from shared DB to dedicated DB.
3. Enable Routing — switch live traffic to the dedicated DB.

For **RC1.7 new tenants**, the dedicated database is created **before any data exists**. There is nothing to migrate. The first operational write (e.g., create a branch) goes directly into the dedicated database. The 3-step process collapses to a single automatic step.

---

## Initial Tenant Data (Seeded into Dedicated DB)

`TenantDatabaseProvisioningService.SeedMinimumDataAsync()` seeds the following into every new dedicated database:

| Table               | Content                                               |
|---------------------|-------------------------------------------------------|
| `SystemSettings`    | One row with `TenantId`, business defaults (₱, VAT 12%, 80mm receipt) |
| `RolePermissions`   | Full permission matrix copied from the shared DB       |
| `SubscriptionPlans` | Read-only reference copy of the platform plans         |

No sample inventory, customers, or suppliers are seeded unless the existing seeder already does so.

---

## Routing Behavior

### New tenants (RC1.7+)
- `RoutingEnabled = true` immediately after creation.
- All operational requests route to `TenantDbContext` (dedicated DB).
- **Disable Routing** is available for emergency rollback only.
- **Enable Routing** button is hidden (routing is already on).

### Legacy/shared tenants
- `DatabaseMode = Shared` — all requests use `ApplicationDbContext` (shared DB).
- The existing 3-step pipeline (Provision → Migrate Data → Enable Routing) remains available.

### Legacy dedicated-not-yet-migrated tenants
- `DatabaseMode = Dedicated`, `IsDatabaseProvisioned = true`, `DataMigrated = false`.
- **Migrate Data** button is shown.
- After migration, **Enable Routing** appears.

---

## Migrate Data Button Logic

| Tenant scenario                         | Button shown                          |
|-----------------------------------------|---------------------------------------|
| `IsDirectlyProvisioned = true`          | Disabled — "No migration needed"      |
| `DataMigrated = false` (legacy shared)  | **Migrate Data** button (active)      |
| `DataMigrated = true`, not direct       | Button hidden (migration already done)|

The `MigrateData` controller action and `TenantDataMigrationService` are **not removed** — they remain available for legacy/repair scenarios.

---

## Rollback Behavior

Any tenant with `RoutingEnabled = true` (whether RC1.7 direct or legacy migrated) can be instantly rolled back:

1. SuperAdmin → Tenant Details → **Disable Routing**.
2. `RoutingEnabled` is set to `false`.
3. All subsequent requests fall back to the shared `ApplicationDbContext`.
4. No data is deleted from either database.
5. Re-enabling routing is available after confirming schema is current.

---

## Existing Tenant Compatibility (Task 7)

RC1.7 is fully backward-compatible. The `IsDirectlyProvisioned` column defaults to `false` for all existing rows.

| Existing scenario                              | Behavior                                  |
|------------------------------------------------|-------------------------------------------|
| Shared tenant (`DatabaseMode = Shared`)        | Unchanged — uses shared DB                |
| Dedicated provisioned, `DataMigrated = false`  | Migrate Data available, Enable Routing after |
| Dedicated routed (`RoutingEnabled = true`)     | Unchanged — uses dedicated DB             |
| New tenant after RC1.7                         | Direct dedicated — no migration step       |

---

## Database Schema Change

One new column was added to the `Tenants` table:

```sql
ALTER TABLE Tenants ADD IsDirectlyProvisioned BIT NOT NULL DEFAULT 0;
```

Applied via EF Core migration: `RC17_AddIsDirectlyProvisioned`.

---

## Files Changed

| File | Change |
|------|--------|
| `Models/Tenant.cs` | Added `bool IsDirectlyProvisioned` property |
| `Migrations/…RC17_AddIsDirectlyProvisioned.cs` | EF migration for new column |
| `Controllers/TenantsController.cs` | `Create` POST: auto-provision + seed + enable routing; redirect to `Details` |
| `Views/Tenants/Details.cshtml` | Revised Dedicated Database + Routing cards; conditional Migrate Data button |
| `Tools/DirectDedicatedTenantQaRunner.cs` | New QA runner (`--qa-direct-dedicated-tenant`) |
| `Program.cs` | Registered `--qa-direct-dedicated-tenant` CLI hook |
| `docs/RC17_DirectDedicatedTenantProvisioning.md` | This document |

---

## QA Runner

```bash
dotnet run -- --qa-direct-dedicated-tenant
```

**What it tests:**
1. Subscription plan resolution.
2. Tenant creation and shared-DB SystemSetting seeding.
3. Auto-provisioning of dedicated database (`ProvisionAsync`).
4. All tenant flags: `DataMigrated`, `RoutingEnabled`, `IsDirectlyProvisioned`, `RoutingActive`.
5. Schema status (0 pending migrations).
6. TenantAdmin account creation and role assignment.
7. Resolver confirms dedicated routing.
8. Operational context resolves to `TenantDbContext`.
9. Operational data writes (branch, category, unit, supplier, customer, product, stock-in, POS sale, damaged goods, supplier return).
10. Row counts verified in dedicated DB.
11. Zero leakage into shared DB.
12. Existing tenants unaffected.

To clean up the QA tenant and drop its database after the run:

```json
// appsettings.Development.json
{
  "QA": { "EnableCleanup": true }
}
```

---

## QA Results

```
── STEP 1 — Subscription Plan ──────────────────────
  [PASS] Using existing subscription plan: …

── STEP 2 — Create Tenant ──────────────────────────
  [PASS] Tenant saved: QA_RC17_… (Id=…)
  [PASS] Shared-DB SystemSetting seeded

── STEP 3 — Auto-Provision Dedicated Database ──────
  [PASS] Dedicated DB provisioned: HardBuild_R17…_…
  [PASS] DataMigrated=true, RoutingEnabled=true, IsDirectlyProvisioned=true

── STEP 4 — Verify Tenant Flags ────────────────────
  [PASS] DatabaseMode = Dedicated
  [PASS] IsDatabaseProvisioned = true
  [PASS] DataMigrated = true
  [PASS] RoutingEnabled = true
  [PASS] IsDirectlyProvisioned = true
  [PASS] RoutingActive = true (composite check)

── STEP 5 — Schema Status ──────────────────────────
  [PASS] Schema is up to date (0 pending migrations)

── STEP 6 — TenantAdmin Account ────────────────────
  [PASS] TenantAdmin created and role assigned

── STEP 7 — Resolver Returns Dedicated Context ─────
  [PASS] Resolver confirms dedicated database routing

── STEP 8 — Resolve Operational Context ────────────
  [PASS] Provider resolved: TenantDbContext (dedicated)

── STEP 9 — Write Operational Data to Dedicated DB ─
  [PASS] Branch / Category / Unit / Supplier / Customer / Product
  [PASS] Stock In (qty=50)
  [PASS] POS Sale
  [PASS] Damaged Goods
  [PASS] Supplier Return

── STEP 10 — Verify Write Location ─────────────────
  [PASS] Dedicated DB: ≥1 branches / sales / stock-in / damaged / returns
  [PASS] Shared DB: 0 branches / sales / stock-in (no leakage)

── STEP 11 — Existing Tenants Unaffected ───────────
  [PASS] Other tenants: N (untouched)

── SUMMARY ─────────────────────────────────────────
  Passed : N
  Failed : 0
  Result : ALL PASS ✓
```

---

## Remaining Risks

| Risk | Mitigation |
|------|------------|
| Provisioning failure mid-Create leaves a tenant in shared mode | Controller catches the error, shows a warning, and redirects to Details so SuperAdmin can retry Provision |
| Long provisioning time on slow SQL Server hosts | Provisioning is synchronous; consider background job for production scale-out |
| `IsDirectlyProvisioned = false` for all pre-RC1.7 tenants | Correct — only new post-RC1.7 tenants get the flag set |
| Shared-DB SystemSetting row still created for RC1.7 tenants | Intentional — provides fallback for any code path that reads from `ApplicationDbContext` before routing is confirmed |

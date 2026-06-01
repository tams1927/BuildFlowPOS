# Pilot Tenant Operations Guide

Step-by-step procedure to onboard a pilot tenant and cut it over to its own dedicated database,
then roll back / re-enable if needed. All steps are performed by **SuperAdmin** through the
**Tenants** area, unless noted. Each privileged action writes an audit event.

> **Prerequisites:** App deployed and running, shared DB migrated, SuperAdmin can log in, and the
> SQL login can `CREATE DATABASE` + run migrations.

---

## A. Create Tenant

1. Log in as **SuperAdmin**.
2. Go to **Tenants → Create**.
3. Enter **Name**, **Code**, limits (Max Branches / Users / Products) and set **Status = Active**.
4. Save. Leave **Database Mode = Shared** for now (it will be switched in step C).

## B. Create TenantAdmin

1. Open the tenant's **Details** page (or the Users area scoped to that tenant).
2. Create the first user with role **TenantAdmin**, assign it to the tenant.
3. Set a strong initial password; the user changes it on first login.
4. Confirm the TenantAdmin can log in and sees the tenant's (currently shared) data.

## C. Set Dedicated Mode

1. On the tenant **Details** page, in the **Dedicated Database** card, switch
   **Database Mode → Dedicated**.
2. Set a valid **Database Name** (letters/digits/`_`/`-` only, starts with a letter or `_`,
   ≤ 128 chars), e.g. `HardBuild_Tenant_<Code>`.
3. Save. Routing is still inactive — the tenant keeps using the shared database.

## D. Provision Database

1. On the **Dedicated Database** card click **Provision Database**.
2. Confirm the SweetAlert prompt ("Provision Dedicated Database?").
3. The service will: create the database (if missing), apply `TenantDbContext` migrations, and seed
   minimal reference data (one `SystemSetting` row, the `RolePermissions` matrix, a read-only
   `SubscriptionPlans` copy). **No business data is moved yet.**
4. Expect success: *"Dedicated database '<name>' provisioned successfully. The tenant continues to
   use the shared database (routing inactive)."*
5. Audit event: `TENANT_DATABASE_PROVISIONED`. Re-provisioning is blocked ("Database already provisioned").

## E. Test Connection

1. Click **Test Connection** on the card.
2. Expected results:
   - Shared tenant → "Using shared database."
   - Dedicated + provisioned → "Connection successful."
   - Dedicated + not provisioned → "Database not provisioned."
3. Audit event: `TENANT_DATABASE_CONNECTION_TESTED`.

## F. Migrate Data

1. Click **Migrate Data**; confirm the SweetAlert ("Copy tenant data to dedicated database? No
   shared data will be removed.").
2. The migration copies this tenant's operational rows to the dedicated DB and **validates counts**
   table-by-table. It fails if any count differs. Shared data is **not** deleted.
3. On success, `DataMigrated = true` and `DataMigratedAtUtc` are set.
4. Audit events: `TENANT_DATA_MIGRATION_STARTED`, `TENANT_DATA_MIGRATION_COMPLETED`
   (or `TENANT_DATA_MIGRATION_FAILED`).

## G. Enable Routing

1. Click **Enable Routing**; confirm the SweetAlert ("Enable dedicated database routing? Only this
   tenant will use the dedicated database. Rollback remains available.").
2. This sets `RoutingEnabled = true` and invalidates the resolver cache.
3. From now on, this tenant's operational requests use `TenantDbContext`.
4. Audit event: `TENANT_ROUTING_ENABLED`.

## H. Verify Diagnostics

1. Open the tenant **Diagnostics / Database Info** page.
2. Confirm:
   - Provisioned = **Yes**
   - Data Migrated = **Yes**
   - Routing Enabled = **Yes**
   - **Current Runtime Database = Dedicated**
3. (Dev/staging) `/Diagnostics/RuntimeDatabase` (SuperAdmin only) returns Tenant Name, Tenant Id,
   Current Context (`TenantDbContext`), Database Name, Connection Type.
4. Log in as the **TenantAdmin** and confirm operational pages (Inventory, POS, Sales, Reports,
   Branches, Imports, Audit) behave normally and the branch selector shows correct branches.
5. For every **other** tenant, diagnostics must still show **Runtime Database = Shared**.

## I. Rollback Procedure (when to use)

Use rollback if the dedicated database shows problems (connectivity, unexpected data, migration
issues) after go-live. Rollback is **non-destructive** and reversible:

1. Before rollback, note the time — operational writes after this point go to the **shared** DB.
2. Perform **Disable Routing** (section J).
3. Verify the tenant operates on shared and the operator can keep working.
4. Investigate the dedicated DB out-of-band; re-migrate/reconcile if data was written to both DBs
   during the switching window before re-enabling.

## J. Disable Routing

1. On the tenant **Details** page click **Disable Routing**.
2. This sets `RoutingEnabled = false` and invalidates the resolver cache.
3. The tenant **immediately** falls back to the shared database. No data is deleted; the dedicated
   database is retained.
4. Verify Diagnostics → **Runtime Database = Shared**, and the TenantAdmin can still log in.
5. Audit event: `TENANT_ROUTING_DISABLED`.

## K. Re-enable Routing

1. Click **Enable Routing** again (the dedicated DB is already provisioned + migrated).
2. Resolver cache is invalidated; Diagnostics → **Runtime Database = Dedicated**.
3. Confirm branches, assignments, and previously imported/created records are visible again.
4. Audit event: `TENANT_ROUTING_ENABLED`.

---

### Quick reference — routing preconditions

Routing is active only when: `DatabaseMode = Dedicated` **and** Provisioned **and** DataMigrated
**and** RoutingEnabled **and** a non-blank ConnectionString. Missing any one → tenant stays Shared.

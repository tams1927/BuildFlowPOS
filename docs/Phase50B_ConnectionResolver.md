# Phase 5.0B — Tenant Database Connection Resolver Layer

**System**: HardBuild POS — HardwareManagementSystem SaaS
**Date**: 2026-06-01
**Status**: Complete. Resolver infrastructure ready; routing NOT active.

---

## Objective

Add tenant database routing **metadata** and **resolver infrastructure** so the
system is ready for database-per-tenant routing in Phase 5.0C — **without moving
any data or changing runtime behaviour**.

All tenants continue to use the existing shared database via `ApplicationDbContext`.

---

## 1. What Was Added

### Tenant model — database routing fields (`Models/Tenant.cs`)

| Field | Type | Default | Purpose |
|---|---|---|---|
| `DatabaseMode` | `TenantDatabaseMode` enum | `Shared` | Storage mode for the tenant |
| `DatabaseName` | `string?` (128) | `null` | Logical dedicated DB name |
| `ConnectionString` | `string?` (1000) | `null` | Backend-only dedicated connection string |
| `DatabaseServer` | `string?` (256) | `null` | Informational SQL host/instance |
| `LastDatabaseMigration` | `string?` (256) | `null` | Last migration applied to dedicated DB |
| `DatabaseProvisionedAtUtc` | `DateTime?` | `null` | When the dedicated DB was provisioned |

Helper: `Tenant.UsesDedicatedDatabase` → true only when `DatabaseMode == Dedicated`
**and** a `ConnectionString` is present.

### Enum (`Models/TenantDatabaseMode.cs`)

```
TenantDatabaseMode.Shared    = 0   (default — only active mode in 5.0B)
TenantDatabaseMode.Dedicated = 1   (reserved for 5.0C+)
```

No magic strings — the enum is used consistently across model, resolver, controller, and views.

### Resolver layer (`Services/TenantDatabases/`)

| File | Role |
|---|---|
| `ITenantDatabaseResolver.cs` | Contract: `GetConnectionStringAsync`, `GetDatabaseNameAsync`, `UsesDedicatedDatabaseAsync` |
| `TenantDatabaseResolver.cs` | Reads tenant metadata from `ApplicationDbContext`, caches 5 min in `IMemoryCache`, masks connection strings, falls back to `DefaultConnection` for Shared tenants |
| `ITenantDbContextFactory.cs` | Contract: `CreateAsync(int tenantId)` → `TenantDbContext` |
| `TenantDbContextFactory.cs` | Builds `DbContextOptions<TenantDbContext>` from the resolved connection string |

### DI registration (`Program.cs`)

```csharp
builder.Services.AddScoped<ITenantDatabaseResolver, TenantDatabaseResolver>();
builder.Services.AddScoped<ITenantDbContextFactory, TenantDbContextFactory>();
```

`ApplicationDbContext` remains the only DbContext registered with `AddDbContext`
and powering Identity. `TenantDbContext` is **not** registered as a runtime context.

### SuperAdmin UI

- **Tenant Details** — new "Database" card: mode, name, provisioned date, last migration + a "Diagnostics" link.
- **Tenant Edit** — new "Database Routing (Advanced)" section: edit `DatabaseMode` and `DatabaseName` only. The connection string is explicitly **not** editable.
- **Diagnostic action** — `/Tenants/DatabaseInfo/{id}` shows tenant name, mode, DB name, dedicated yes/no, masked connection string, resolver status, and phase status. No connectivity test is performed.

### Migration

`Migrations/20260531190647_AddTenantDatabaseRouting.cs` adds the 6 columns to the
`Tenants` table. `DatabaseMode` has `defaultValue: 0` so **every existing tenant
becomes Shared automatically**.

---

## 2. Why Runtime Is Unchanged

- No controller or service consumes `ITenantDatabaseResolver` or `ITenantDbContextFactory` on a request path. They are registered for Phase 5.0C use only.
- `ApplicationDbContext` is still the sole runtime context (Identity + all data access).
- The resolver, for any Shared-mode tenant (i.e. all of them), returns the **same `DefaultConnection`** the app already uses.
- Tenant isolation, branch isolation, SuperAdmin SaaS management, subscription management, and Identity login are all untouched.
- The only schema change is six **nullable / defaulted** columns appended to `Tenants` — no data backfill, no breaking change.

---

## 3. How Shared Mode Works (today)

```
Request → ApplicationDbContext (DefaultConnection)   ← unchanged

ITenantDatabaseResolver.GetConnectionStringAsync(tenantId)
    → tenant.DatabaseMode == Shared
    → returns configuration["ConnectionStrings:DefaultConnection"]
```

Every tenant resolves to the shared database. The factory, if ever called, would
produce a `TenantDbContext` pointed at that same shared database.

---

## 4. How Dedicated Mode Will Work (Phase 5.0C+)

```
ITenantDatabaseResolver.GetConnectionStringAsync(tenantId)
    → tenant.DatabaseMode == Dedicated && tenant.ConnectionString != null
    → returns tenant.ConnectionString   (the tenant's own database)

ITenantDbContextFactory.CreateAsync(tenantId)
    → new TenantDbContext(UseSqlServer(dedicated connection string))
```

Controllers will be migrated (in later phases) to obtain a `TenantDbContext` from
the factory instead of injecting `ApplicationDbContext`.

---

## 5. Security Considerations

- **Connection strings are never logged in full.** `TenantDatabaseResolver.Mask()`
  emits only `Server` and `Database` tokens; credentials are redacted.
- The diagnostic view and factory logs use the masked form exclusively.
- `ConnectionString` is **backend-only**: it is not bound in the Edit POST (only
  `DatabaseMode` and `DatabaseName` are copied to the tracked entity), and it is not
  rendered in any view.
- Unknown tenants resolve to the shared database with a warning log (tenant id only)
  rather than throwing during metadata lookup, avoiding accidental disclosure.

### Connection String Masking

```
Input : Server=SQL01;Database=HardBuild_Tenant_42;User Id=app;Password=secret
Output: Server=SQL01; Database=HardBuild_Tenant_42; (credentials hidden)
```

---

## 6. Phase 5.0C Prerequisites

Before activating dedicated routing in Phase 5.0C, the following must be in place:

1. A `TenantDatabaseProvisioningService` that creates the physical database, applies
   `TenantDbContext` migrations, seeds defaults, and sets `Tenant.ConnectionString`
   + `DatabaseProvisionedAtUtc` + `LastDatabaseMigration`.
2. A per-tenant data migration script (shared DB → dedicated DB) for all tenant tables.
3. A secure store / encryption strategy for `Tenant.ConnectionString`.
4. A scoped tenant-context middleware that selects the `TenantDbContext` per request
   (only for tenants flagged `Dedicated`), with safe fallback to shared.
5. A pilot tenant chosen for the first dedicated database, with a rollback path
   (set `DatabaseMode = Shared` / clear `ConnectionString`).

---

## 7. Build Result

```
dotnet build → Build succeeded. 0 Warning(s) 0 Error(s)
```

Migration `AddTenantDatabaseRouting` created via
`dotnet ef migrations add AddTenantDatabaseRouting --context ApplicationDbContext`.

---

*Phase 5.0B complete. Resolver ready, routing not active. Do not start Phase 5.0C.*

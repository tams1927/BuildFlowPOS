# RC1.2 — Pilot Readiness Hotfixes

Pre-pilot corrections for unit-cost clarity, Phase 5.1 migration readiness, and SuperAdmin credential hardening. No new modules or architectural changes.

---

## 1. Unit cost audit findings (Task 1)

| Location | Pre-RC1.2 behavior |
|----------|-------------------|
| `StockInController.Create` | `UnitCost` was ambiguous; `totalCost = baseQty × unitCost` treated input as **cost per base unit** |
| `PurchaseOrdersController` Create / Edit / Receive | Same: `totalCost = baseQty × unitCost` |
| `StockInDetail.UnitCost` | Single field with no received vs base distinction |
| `PurchaseOrderItem.UnitCost` | Same |
| `item.CostPrice` | Set directly from user input without conversion |

**Risk:** User enters ₱1,000 per sack (25 kg). System interpreted ₱1,000 per kg → inventory value inflated 25× (e.g. ₱250,000 instead of ₱10,000).

---

## 2. Final cost model (Task 2)

**Rule:** User enters **cost per received/ordered unit**. System derives base-unit cost for valuation.

```
CostPerBaseUnit = CostPerReceivedUnit ÷ ConversionQuantity
TotalCost       = ReceivedQuantity × CostPerReceivedUnit
Inventory value = BaseQuantity × CostPerBaseUnit
```

**Example (QA Nail):**

| Field | Value |
|-------|-------|
| Conversion | 1 sack = 25 kg |
| Stock-in | 10 sacks @ ₱1,000/sack |
| Base qty | 250 kg |
| Cost/kg | ₱40 |
| Total / inventory value | ₱10,000 |

**Stored fields:**

- `StockInDetail`: `CostPerReceivedUnit`, `CostPerBaseUnit`, `UnitCost` (= base, legacy mirror)
- `PurchaseOrderItem`: `CostPerOrderedUnit`, `CostPerBaseUnit`, `UnitCost` (= base, legacy mirror)

**Service:** `ItemUnitConversionService.ComputePurchaseCost()`

**Legacy backfill (migration):** Existing rows assumed `UnitCost` was per base unit; `CostPerReceivedUnit` / `CostPerOrderedUnit` derived as `UnitCost × ConversionQuantity`.

---

## 3. UI changes (Task 3)

| Screen | Change |
|--------|--------|
| Stock-In (`Views/StockIn/Index.cshtml`) | Label **Cost Per Received Unit**; live cost preview (conversion, ₱/base, line total) |
| PO Create / Edit | Header **Cost / Ordered Unit** + helper text |
| PO Receive / Details / Print | Display `CostPerOrderedUnit` (fallback for legacy rows) |

---

## 4. Migration verification — shared DB (Task 5)

**Context:** `ApplicationDbContext`

| Migration | Status |
|-----------|--------|
| `20260604091656_Phase51_UnitConversionAndCurrency` | Required baseline |
| `20260605045843_RC12_CostPerUnitClarity` | Adds cost clarity columns + backfill |

**Verify:**

```powershell
dotnet ef migrations list --context ApplicationDbContext
dotnet ef database update --context ApplicationDbContext
```

**Phase 5.1 objects (shared):** `Items.BaseUnitId`, `ItemUnitConversions`, `SystemSettings.CurrencyCode` / `CurrencyName`, Stock-In and PO conversion columns.

**RC1.2 objects (shared):** `StockInDetails.CostPerReceivedUnit`, `CostPerBaseUnit`; `PurchaseOrderItems.CostPerOrderedUnit`, `CostPerBaseUnit`.

---

## 5. Tenant DB migration (Tasks 6–7)

**Context:** `TenantDbContext`

| Migration | Purpose |
|-----------|---------|
| `20260604091736_Phase51_UnitConversionAndCurrency` | Unit conversion + currency |
| `20260605045854_RC12_CostPerUnitClarity` | Cost clarity columns + backfill |

**Provisioning path:** `TenantDatabaseProvisioningService.ProvisionAsync()` calls `tenantContext.Database.MigrateAsync()` — new dedicated databases receive all tenant migrations automatically.

**Existing dedicated DB upgrade:**

1. Deploy application with new migrations.
2. Re-run **Provision** (idempotent) or call `MigrateAsync` on the tenant connection string.
3. Confirm `Tenants.LastDatabaseMigration` includes `RC12_CostPerUnitClarity`.

**Routing note:** Tenants on shared DB only need `ApplicationDbContext` migrations. Dedicated+routed tenants need both contexts kept in sync.

---

## 6. Security hardening (Tasks 8–9)

**`DbSeeder.SeedAdminAsync`:**

- Reads `SeedSettings:InitialSuperAdminPassword` from configuration.
- Falls back to `SuperAdmin123!` with **warning** if unset.
- New SuperAdmin accounts get `ForcePasswordChange = true`.

**`AccountController`:** Existing `ForcePasswordChange` redirect to Change Password on login (unchanged).

**Configuration:**

```json
"SeedSettings": {
  "InitialSuperAdminPassword": "<strong-password-before-go-live>"
}
```

Set in environment-specific config or secrets — never commit production passwords.

---

## 7. QA

**Runner:** `dotnet run -- --qa-rc12` (Development only)

**Covers:**

- Shared DB pending/applied migrations
- Schema column accessibility
- Unit cost math (10 sacks × ₱1,000 → 250 kg, ₱40/kg, ₱10,000 value)
- SuperAdmin `ForcePasswordChange` flag
- Dedicated tenant DB migration/schema check (when a routed tenant exists)

**Related:** `dotnet run -- --qa-phase51` updated for new cost field semantics.

**Last run (`--qa-rc12`):** 14 passed, 0 failed

| Check | Result |
|-------|--------|
| Shared DB pending migrations | None |
| Phase51 on shared DB | Applied |
| Schema columns (ItemUnitConversions, cost, currency, BaseUnitId) | OK |
| Unit cost math (QA Nail scenario) | ₱40/kg, ₱10,000 total, no inflation |
| SuperAdmin ForcePasswordChange (new installs) | DbSeeder enforces; dev DB has legacy account |
| Dedicated tenant live schema | Skipped (no routed dedicated tenant in dev) |

---

## 8. Remaining risks

| Risk | Mitigation |
|------|------------|
| Historical stock-in/PO rows entered as “per sack” before RC1.2 may have inflated `TotalCost` | Migration backfill assumes old `UnitCost` was per **base** unit; manually review pre-pilot conversion transactions |
| Existing SuperAdmin on dev DB may have `ForcePasswordChange = false` | Rotate password; set config password before production |
| PDF export (`DocumentPdfService`) still uses legacy “Unit Cost” column label | Display values follow stored PO line data |
| Dedicated tenant without re-provision after deploy | Run provision/migrate on each dedicated DB before pilot |

---

## 9. Files changed (summary)

- Models: `StockInDetail.cs`, `PurchaseOrderItem.cs`
- Service: `ItemUnitConversionService.cs`
- Controllers: `StockInController.cs`, `PurchaseOrdersController.cs`
- Views: Stock-In, PO Create/Edit/Receive/Details/Print
- Migrations: `RC12_CostPerUnitClarity` (shared + tenant)
- Seeder: `DbSeeder.cs`
- Config: `appsettings.json`, `appsettings.Development.json`
- QA: `Tools/Rc12QaRunner.cs`, `Tools/Phase51QaRunner.cs`, `Program.cs`

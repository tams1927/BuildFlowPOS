# Phase 5.3 — Damaged Goods & Supplier Return Management

## Business explanation

Hardware stores must separate **sellable** inventory from **damaged / unsellable** stock before pilot deployment. Damaged items (broken, rusted, expired, lost, theft) are not sold at POS. They may be recovered, disposed, or returned to suppliers for credit or replacement.

| Bucket | Field (global) | Field (branch) | POS / sale |
|--------|----------------|----------------|------------|
| Sellable | `Item.CurrentStock` | `BranchProductStock.Quantity` | Yes |
| Damaged | `Item.DamagedStock` | `BranchProductStock.DamagedStock` | No |
| Physical | Sellable + Damaged | Sellable + Damaged | — |

When a branch context is active, branch quantities drive POS availability (same pattern as Stock Adjustment). Global `Item` damaged totals are still updated for reporting and non-branch flows.

## Inventory effect summary

| Action | Sellable | Damaged | Notes |
|--------|----------|---------|-------|
| Tag damaged (create) | −BaseQty | +BaseQty | Status `Pending` |
| Recover | +BaseQty | −BaseQty | Status `Recovered` |
| Dispose | — | −BaseQty | Status `Disposed` |
| Cancel (pending only) | +BaseQty | −BaseQty | Reverses tag |
| Return to supplier | — | unchanged until return finalized | Creates linked supplier return; status `ReturnedToSupplier` |
| Supplier return credited/replaced | — | −BaseQty (if linked to damage) | Replacement adds sellable if `Replaced` |
| Direct supplier return (damaged reason) | −BaseQty | +BaseQty | MVP: non-damage reasons deduct sellable only |

Stock mutations run through `DamagedStockService` with transactional saves. Stock Adjustment behavior is unchanged.

## Damaged goods workflow

1. **Create** (`DamagedGoods/Create`) — branch, item, unit, quantity, reason, notes.
2. On save: `MoveSellableToDamagedAsync`, inventory movement, audit `DAMAGED_GOODS_CREATED`, status `Pending`.
3. **Details** actions (SweetAlert confirmation):
   - **Dispose** — `DisposeDamagedAsync`, `DAMAGED_GOODS_DISPOSED`, status `Disposed`.
   - **Recover** — `RecoverDamagedAsync`, `DAMAGED_GOODS_RECOVERED`, status `Recovered`.
   - **Return to supplier** — creates `SupplierReturnHeader` linked via `LinkedDamagedGoodsId`, `DAMAGED_GOODS_RETURNED_TO_SUPPLIER`.
   - **Cancel** — only if no final action; restores sellable, `DAMAGED_GOODS_CANCELLED`.

## Supplier return workflow

1. **Create** (`SupplierReturns/Create`) — supplier, branch, lines, optional linked damage record.
2. From damaged goods: no second sellable deduction; link only.
3. Direct create: damaged reasons move sellable → damaged; other reasons deduct sellable only.
4. **Status actions** (SweetAlert):
   - **Mark Sent** — `Pending` → `Sent`, `SUPPLIER_RETURN_SENT`.
   - **Mark Credited** — deducts damaged if linked, `SUPPLIER_RETURN_CREDITED`, closes.
   - **Mark Replaced** — adds replacement to sellable, deducts damaged if linked, `SUPPLIER_RETURN_REPLACED`.
   - **Cancel** — `Pending` only, reverses stock if needed, `SUPPLIER_RETURN_CANCELLED`.

## Unit conversion (Phase 5.1)

Detail lines store `Quantity`, `UnitId`, `ConversionQuantity`, and `BaseQuantity`. Stock effects always use `BaseQuantity` via `ItemUnitConversionService.ToBaseQuantityAsync` / `GetFactorAsync`.

Example: 2 sacks × 25 kg/sack → `BaseQuantity = 50` kg deducted from sellable and added to damaged.

## Wall-to-wall inventory alignment

Physical stock = sellable + damaged at item and branch level. POS and low-stock logic use sellable only. Inventory Index and Valuation reports show sellable, damaged, and physical columns. Damaged cost value shown separately on valuation (MVP).

## Database / routing

- Models: `DamagedGoodsHeader`, `DamagedGoodsDetail`, `SupplierReturnHeader`, `SupplierReturnDetail`.
- Migrations: `20260624072349_Phase53_DamagedGoodsSupplierReturns` (ApplicationDbContext), `20260624072400_Phase53_DamagedGoodsSupplierReturns` (TenantDbContext).
- Dedicated tenants: operational data and stock mutations route through `ITenantOperationalContextProvider`; QA applies `MigrateAsync` on routed context before tests.

## Permissions

| Role | DamagedGoods | SupplierReturns |
|------|--------------|-----------------|
| SuperAdmin | Full (bypass) | Full |
| TenantAdmin | Full | Full |
| BranchManager | View / Create / Edit | View / Create / Edit |
| InventoryStaff | View / Create / Edit | View / Create / Edit |
| Cashier | Denied | Denied |

Seeded via `DbSeeder` module discovery + `inventoryAccess` list for InventoryStaff.

## Audit events

`DAMAGED_GOODS_CREATED`, `DAMAGED_GOODS_DISPOSED`, `DAMAGED_GOODS_RECOVERED`, `DAMAGED_GOODS_RETURNED_TO_SUPPLIER`, `DAMAGED_GOODS_CANCELLED`, `SUPPLIER_RETURN_CREATED`, `SUPPLIER_RETURN_SENT`, `SUPPLIER_RETURN_CREDITED`, `SUPPLIER_RETURN_REPLACED`, `SUPPLIER_RETURN_CANCELLED`.

Logged through `AuditService` with tenant, user, IP, and reference to header id.

## QA results (`dotnet run -- --qa-phase53`)

**11 / 11 passed** (development, dedicated tenant routing):

1. Create item sellable 100  
2. Tag 10 damaged → branch sellable 90, damaged 10  
3. POS available 90  
4. Recover 5 → branch 95 / 5  
5. Dispose 2 → damaged 3  
6. Supplier return 3 + credited → damaged 0  
7. No shared DB item leakage  
8. Audit trail entries present  
9. No audit leakage to shared DB on dedicated tenant  

## Known limitations (MVP)

- `TenantDataMigrationService` copies Phase 5.3 tables (`DamagedGoodsHeaders`, `DamagedGoodsDetails`, `SupplierReturnHeaders`, `SupplierReturnDetails`) in dependency order with count validation (`--qa-phase531`).
- Stock aging / ABC analytics reports not updated for damaged columns (valuation + inventory index updated).
- Excel export for valuation still uses sellable quantity column label “Quantity” (damaged columns not in export).
- Global `Item.CurrentStock` is not adjusted when branch stock exists (matches Stock Adjustment pattern); branch `Quantity` is authoritative for POS.
- Multi-line damage/return headers supported in DB; UI create forms are single-line MVP.

## Build

`dotnet build` — **0 errors, 0 warnings**.

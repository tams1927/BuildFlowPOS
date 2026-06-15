# Phase 5.1 — Unit Conversion Foundation & Multi-Currency Settings

## Business explanation

Hardware stores often **purchase in bulk units** (sack, box, roll) but **track and sell inventory in a base unit** (kg, pcs, meter). Phase 5.1 adds optional unit conversions without rewriting POS: the POS continues to sell and deduct in the item's **base unit**.

## Unit conversion examples

| Item | Base unit | Purchase unit | Conversion |
|------|-----------|---------------|------------|
| Common nail | kg | sack | 1 sack = 25 kg |
| Wood screw | pcs | box | 1 box = 100 pcs |
| THHN wire | m | roll | 1 roll = 100 m |

## Stock-In conversion process

1. Configure conversions on **Products → Unit Conversions** (per item).
2. On **Stock In**, select **Received Unit** and **Received Quantity**.
3. System computes: `BaseQuantity = ReceivedQuantity × ConversionQuantity`.
4. Inventory increases by **BaseQuantity** only.
5. `StockInDetail` stores: `ReceivedUnitId`, `ReceivedQuantity`, `ConversionQuantity`, `BaseQuantity` (and `Quantity` = base for compatibility).

## Purchase Order conversion process

1. When creating a PO line, select the **order unit** (defaults to default purchase unit).
2. System stores `OrderedQuantity`, `OrderedUnitId`, `ConversionQuantity`, `BaseQuantity`.
3. When **receiving**, quantities entered are in the **order unit**; stock increases by converted base quantity.

## Inventory base unit rule

- `Item.BaseUnitId` — inventory, reports, valuation, POS deduction, branch stock.
- `Item.UnitId` — selling/display unit (often same as base).
- Existing items: migration sets `BaseUnitId = UnitId` and seeds a 1:1 conversion row.

## Currency settings

Per-tenant `SystemSetting` fields:

| Field | Example |
|-------|---------|
| CurrencyCode | PHP, USD, EUR |
| CurrencySymbol | ₱, $, € |
| CurrencyName | Philippine Peso |

Configured under **Settings → Currency**. Audit action: `SETTINGS_CURRENCY_UPDATED`.

`CurrencyFormatter` service and `TenantCurrencyFilter` expose `ViewBag.CurrencySymbol` on MVC views. PDF services already read `settings.CurrencySymbol`.

## Dedicated database support

- Models and migrations on **ApplicationDbContext** and **TenantDbContext**.
- `TenantDataMigrationService` copies `ItemUnitConversions` after `Items`.
- Routing-enabled tenants read/write conversions in the dedicated database.

## QA (Development)

```bash
dotnet run -- --qa-phase51
```

Verifies:

- Item `QA_Nails` with kg base, 1 sack = 25 kg
- Stock-in 10 sacks → 250 kg inventory
- POS sale 2.5 kg → 247.5 kg remaining
- Conversion row in dedicated DB, no shared leak
- USD currency on routed settings, shared DB unchanged when routing enabled

## Known limitations

- POS does not sell in alternate units (by design).
- PO Edit for existing lines does not yet expose unit picker on all screens.
- Inventory “equivalent” display is available via service helper; not shown on every report yet.
- Bare controller URLs (e.g. `/POS`) still require `/Index` action.

## QA results

Run `dotnet run -- --qa-phase51` after a dedicated routing-enabled tenant exists. See console for PASS/FAIL lines.

## Build

Phase 5.1 migrations:

- `20260604091656_Phase51_UnitConversionAndCurrency` (shared)
- `20260604091736_Phase51_UnitConversionAndCurrency` (tenant)

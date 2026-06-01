# Development Data Cleanup Tool

> **WARNING — FOR DEVELOPMENT USE ONLY**
>
> This tool permanently deletes operational data from your development database.
> It must **never** be enabled in a production or staging environment.

---

## Purpose

Over time a development database accumulates demo sales, test customers,
dummy products, audit logs, and other noise that makes it hard to test a
clean slate.  This tool removes all of that data in a single, safe operation
**without dropping or recreating the database**, preserving the schema,
identity tables, and all platform-level setup.

---

## What Is Preserved (Never Deleted)

| Item | Reason |
|------|--------|
| SuperAdmin user account | Platform owner account |
| All application roles (`SuperAdmin`, `Admin`, `TenantAdmin`, `Cashier`, `BranchManager`, `InventoryStaff`) | System roles required by RBAC |
| Role permission rows (`RolePermissions` table) | Required by authorization system |
| Subscription plans (`SubscriptionPlans` table) | SaaS tier definitions |
| System settings (`SystemSettings` table) | Business configuration |
| `DEFAULT` tenant record | Required system / legacy-compat tenant |
| EF Migrations history (`__EFMigrationsHistory`) | Prevents re-running old migrations |
| All ASP.NET Identity schema tables | Identity infrastructure |

---

## What Is Deleted (Operational / Demo Data)

| Category | Tables Cleared |
|----------|---------------|
| **Sales** | `SalesHeaders`, `SalesDetails`, `SalesReturnHeaders`, `SalesReturnDetails` |
| **Quotations** | `Quotations`, `QuotationItems` |
| **Delivery Receipts** | `DeliveryReceipts`, `DeliveryReceiptItems` |
| **Stock In** | `StockInHeaders`, `StockInDetails` |
| **Stock Adjustments** | `StockAdjustmentHeaders`, `StockAdjustmentDetails` |
| **Branch Transfers** | `BranchTransfers`, `BranchTransferItems` |
| **Supplier Payments** | `SupplierPayments` |
| **Customer Ledger** | `CustomerLedgers` |
| **Expenses** | `Expenses` |
| **Import** | `ImportBatches`, `ImportBatchRows` |
| **Notifications** | `Notifications` |
| **Audit Logs** | `AuditTrails` |
| **Branch Stock** | `BranchProductStocks` |
| **User–Branch Links** | `UserBranches` |
| **Master Data** | `Customers`, `Suppliers`, `Items`, `Categories`, `Units` |
| **Branches** | All branch records |
| **Tenants** | All tenants whose `Code != 'DEFAULT'` |
| **Demo Users** | Any user **not** in the `SuperAdmin` role (matches by known test usernames/email domains, or any user with a non-null `TenantId`) |

### Demo user matching rules

A user is considered a demo/test account (and will be removed) if their
username or email matches **any** of the rules below.

**Exact username match** (case-insensitive):
`admin`, `cashier`, `test`, `testuser`, `demo`, `demouser`, `staff`, `manager`,
`branchmanager`, `inventorystaff`

**Username prefix match** — username starts with:
`test_`, `demo_`

**Email substring match** — email contains any of:
`test`, `demo`, `example.com`, `hardwarepos.local`, `test.local`, `demo.local`

> **`TenantId` is NOT a deletion criterion.**
> Users are never deleted solely because they have a `TenantId`.
> Only the explicit username/email rules above trigger deletion.
> This protects real tenant users you created intentionally.

The `SuperAdmin` user is **always** skipped regardless of username or email.

### Safety preview log

Before any row is deleted, the service writes a full preview to the startup
log showing:

- Every user that will be deleted (username, email, and the matching rule)
- Every tenant that will be deleted (code, name)
- Row count per table to be cleared

Look for lines tagged `[DevelopmentDataResetService]` in the console or log
file.  The preview ends with `── END PREVIEW — STARTING DELETION` before the
first actual delete happens.

---

## How to Enable

1. Open `appsettings.Development.json`.
2. Set the flag to `true`:

```json
"SeedSettings": {
  "EnableDemoDataCleanup": true
}
```

3. Start (or restart) the application.

The cleanup runs once during startup, immediately after the normal seed
operations.  Log output will confirm what was deleted.

---

## How to Disable Again (Important)

**Set the flag back to `false` immediately after the cleanup run:**

```json
"SeedSettings": {
  "EnableDemoDataCleanup": false
}
```

If you leave it as `true`, the cleanup will re-run on every subsequent
application startup and wipe any new data you enter.

---

## Safety Gates

Both conditions must be satisfied for the cleanup to execute:

| Gate | Check |
|------|-------|
| Environment | `ASPNETCORE_ENVIRONMENT` must equal `Development` |
| Config flag | `SeedSettings:EnableDemoDataCleanup` must be `true` |

If the app runs with `Production`, `Staging`, or any non-Development
environment value, the cleanup block is never entered, even if the flag is set.

---

## Typical Workflow

```
1. Accumulate some dev / demo data during testing.
2. Edit appsettings.Development.json → set EnableDemoDataCleanup: true
3. dotnet run   (or press F5 in Visual Studio / Rider)
4. Watch the startup log for "[DevelopmentDataResetService]" lines.
5. Stop the application.
6. Edit appsettings.Development.json → set EnableDemoDataCleanup: false
7. Resume development with a clean, seeded database.
```

---

## Log Messages to Watch

| Log level | Message | Meaning |
|-----------|---------|---------|
| `WARNING` | `*** DEVELOPMENT DATA CLEANUP STARTED ***` | Cleanup is running |
| `INFORMATION` | `Deleted N row(s) from TableName.` | Rows removed from a table |
| `INFORMATION` | `Removed demo user 'username'.` | A test user was deleted |
| `WARNING` | `*** DEVELOPMENT DATA CLEANUP COMPLETE ***` | Cleanup finished |
| `WARNING` | `Attempted to run in non-Development environment. Aborted.` | Environment safety gate triggered |

---

## Implementation Files

| File | Purpose |
|------|---------|
| `Data/Seeders/DevelopmentDataResetService.cs` | Main cleanup logic |
| `appsettings.Development.json` | Config flag (`SeedSettings:EnableDemoDataCleanup`) |
| `Program.cs` | Dev-only wiring — calls the service when both gates pass |

---

> **Reminder:** `appsettings.Development.json` should be listed in `.gitignore`
> or reviewed carefully before committing to prevent accidentally sharing local
> database credentials or enabling the cleanup flag in source control.

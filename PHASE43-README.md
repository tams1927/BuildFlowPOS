# PHASE 4.3 — Tenant Self-Service Administration

**Date:** May 31, 2026  
**Build Result:** ✅ 0 Errors · 0 Warnings  
**Migration:** `Phase43_TenantSelfService`

---

## Overview

Phase 4.3 reduces SuperAdmin dependency by empowering TenantAdmins to manage their own company profile, users, branches, and branding entirely from within their tenant dashboard.

---

## Tasks Delivered

### Task 1 — Company Profile: TIN + VAT Registration Number

| File | Change |
|---|---|
| `Models/SystemSetting.cs` | Added `TIN` (`[StringLength(30)]`) and `VATRegNumber` (`[StringLength(50)]`) |
| `Controllers/SettingsController.cs` | Both fields assigned in `Update()` action |
| `Views/Settings/Index.cshtml` | Two new form fields in the business info row |

Both fields are nullable. They appear on printed documents wherever TIN/VAT data is relevant.

---

### Task 2 — Company Logo

| File | Change |
|---|---|
| `Models/SystemSetting.cs` | Added `LogoPath` (`[StringLength(300)]`) to store the relative web path |
| `Controllers/SettingsController.cs` | New `UploadLogo` POST action: validates MIME type + 2 MB limit, deletes old file, saves to `wwwroot/uploads/logos/tenant-{id}/logo.{ext}`, audits `COMPANY_LOGO_UPDATED` |
| `Views/Settings/Index.cshtml` | Logo preview card with `<img>` preview, file input, SweetAlert confirmation, toast on success |
| `Services/Pdf/DocumentPdfService.cs` | Added `IWebHostEnvironment`, `ResolveLogoPath()` helper, and conditional `Image(absPath).FitHeight()` in the header row of all three generators (Quotation, DeliveryReceipt, TransferSlip) |
| `Views/Shared/_PrintDoc.cshtml` | `<img>` in the doc-header-left block when `settings?.LogoPath != null` |
| `Views/Sales/Receipt.cshtml` | `<img>` above business name in thermal receipt header |
| `Controllers/QuotationsController.cs` | Passes `settings?.LogoPath` to `GenerateQuotationPdf` |
| `Controllers/DeliveryReceiptsController.cs` | Passes `settings?.LogoPath` to `GenerateDeliveryReceiptPdf` |

**Logo storage path:** `wwwroot/uploads/logos/tenant-{tenantId}/logo.{ext}`  
**Accepted types:** JPG, PNG, GIF, WebP — max 2 MB

---

### Task 3 — Subscription Usage Widget

| File | Change |
|---|---|
| `Controllers/HomeController.cs` | Injected `TenantLimitGuard` and `ITenantContext`; calls `GetUsageAsync()` when user is `TenantAdmin` and populates `ViewBag.SubscriptionUsage` |
| `Views/Home/Index.cshtml` | New widget card rendered only for `TenantAdmin` users: shows Plan Name, Status badge, Expiration Date, and three Bootstrap progress bars for Branches / Users / Products with color-coded thresholds (green < 70%, yellow < 90%, red ≥ 90%) |

---

### Task 4 — User Management Enhancements

| File | Change |
|---|---|
| `Controllers/UsersController.cs` | New `Activate` POST action (sets `IsActive = true`, audits `USER_ACTIVATED`); new `ResetPassword` POST action (validates min 6 chars, uses `UserManager.RemovePasswordAsync` + `AddPasswordAsync`, audits `USER_PASSWORD_RESET`); renamed `Deactivate` audit event from `"DEACTIVATED"` → `"USER_DEACTIVATED"` |
| `Views/Users/Index.cshtml` | Inactive user rows: green Activate button with SweetAlert `"Activate this user?"`; Active user rows: key icon Reset Password button that opens `#resetPasswordModal`; New `#resetPasswordModal` with hidden user ID, new password input, and SweetAlert confirmation |

**Guards applied:** same-tenant check enforced, SuperAdmin account blocked from all three actions.

---

### Task 5 — Branch Management Enhancements

| File | Change |
|---|---|
| `Controllers/BranchesController.cs` | New `ActivateBranch` POST action (audits `BRANCH_ACTIVATED`); new `DeactivateBranch` POST action (blocks main branch + only-active-branch guard, audits `BRANCH_DEACTIVATED`); new `SetMainBranch` POST action (requires active branch, clears all other main flags in tenant, audits `MAIN_BRANCH_CHANGED`) |
| `Views/Branches/Index.cshtml` | Active non-main branches: Deactivate (orange) + Set as Main (blue) buttons with SweetAlerts; Inactive branches: Activate (green) button with SweetAlert; Edit modal now includes `managerName` input with JS population from `data-manager` attribute |

---

### Task 6 — Audit Events

All events include `TenantId` (via User principal claims), `UserId`, `PerformedBy`, `EntityName`, `EntityId`, and IP address.

| Event | Action | Status |
|---|---|---|
| `COMPANY_PROFILE_UPDATED` | `SettingsController.Update` | Updated (was `"UPDATED"`) |
| `COMPANY_LOGO_UPDATED` | `SettingsController.UploadLogo` | New |
| `USER_ACTIVATED` | `UsersController.Activate` | New |
| `USER_DEACTIVATED` | `UsersController.Deactivate` | Renamed (was `"DEACTIVATED"`) |
| `USER_PASSWORD_RESET` | `UsersController.ResetPassword` | New |
| `BRANCH_ACTIVATED` | `BranchesController.ActivateBranch` | New |
| `BRANCH_DEACTIVATED` | `BranchesController.DeactivateBranch` | New |
| `MAIN_BRANCH_CHANGED` | `BranchesController.SetMainBranch` | New |

---

### Task 7 — SweetAlert Confirmations

| Action | Dialog Type |
|---|---|
| Upload Logo | question — "Upload logo? This will replace the current company logo." |
| Activate User | question — "Activate this user?" |
| Reset Password | warning — "Reset this user's password?" |
| Deactivate Branch | warning — "Branch will no longer be available for transactions." |
| Activate Branch | question — "Restore to active status?" |
| Change Main Branch | question — "Branch will become the main branch. Current main will be demoted." |

---

### Task 8 — Toast Notifications

All success operations set `TempData["SuccessMessage"]` which is rendered by the global toast component in `_Layout.cshtml`.

Operations that generate a toast: Profile Update, Logo Upload, Password Reset, User Activate, User Deactivate, Branch Activate, Branch Deactivate, Main Branch Change.

---

## Database Changes

**Migration:** `Phase43_TenantSelfService`

```sql
ALTER TABLE [SystemSettings] ADD [TIN] nvarchar(30) NULL;
ALTER TABLE [SystemSettings] ADD [VATRegNumber] nvarchar(50) NULL;
ALTER TABLE [SystemSettings] ADD [LogoPath] nvarchar(300) NULL;
```

No FK changes. No index changes.

---

## File Change Summary

| File | Change Type |
|---|---|
| `Models/SystemSetting.cs` | +TIN, +VATRegNumber, +LogoPath |
| `Controllers/SettingsController.cs` | +IWebHostEnvironment injection, +UploadLogo action, +TIN/VATReg in Update |
| `Views/Settings/Index.cshtml` | +TIN/VATReg fields, +Logo section with preview + SweetAlert |
| `Controllers/UsersController.cs` | +Activate, +ResetPassword; rename Deactivate audit event |
| `Views/Users/Index.cshtml` | +Activate button, +ResetPassword button + modal + SweetAlerts |
| `Controllers/BranchesController.cs` | +ActivateBranch, +DeactivateBranch, +SetMainBranch |
| `Views/Branches/Index.cshtml` | +action buttons, +SweetAlerts, +managerName in edit modal |
| `Controllers/HomeController.cs` | +TenantLimitGuard + ITenantContext injection, +GetUsageAsync call |
| `Views/Home/Index.cshtml` | +Subscription usage widget (TenantAdmin only) |
| `Services/Pdf/DocumentPdfService.cs` | +IWebHostEnvironment, +ResolveLogoPath, +logo embed in all 3 PDF headers |
| `Views/Shared/_PrintDoc.cshtml` | +logo img in doc-header-left |
| `Views/Sales/Receipt.cshtml` | +logo img above business name |
| `Controllers/QuotationsController.cs` | Pass `LogoPath` to `GenerateQuotationPdf` |
| `Controllers/DeliveryReceiptsController.cs` | Pass `LogoPath` to `GenerateDeliveryReceiptPdf` |
| `Migrations/Phase43_TenantSelfService.cs` | New migration file |
| `PHASE43-README.md` | This file |

---

## Security Notes

- All new controller actions use `[ValidateAntiForgeryToken]` and `[PermissionAuthorize]`
- `ActivateBranch`, `DeactivateBranch`, `SetMainBranch` each verify via `TenantGuard.CanAccessTenantAsync()`
- `Activate`, `ResetPassword`, `Deactivate` (users) each verify same-tenant and block SuperAdmin targets
- Logo upload enforces: MIME type allowlist, 2 MB size limit, server-side extension resolution (no trust of file extension from client)
- Cross-tenant writes: all settings updates filter by `CurrentTenantId`

---

## Build Verification

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

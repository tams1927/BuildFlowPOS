# UI Save Confirmation Audit

## Root cause

Duplicate SweetAlert save confirmations were caused by **`form.requestSubmit()` re-firing the same `submit` listener** after the user confirmed.

Typical broken pattern (Customers, Suppliers, Categories, Units, Users, etc.):

1. `submit` handler runs → `e.preventDefault()` → show Swal  
2. User clicks “Yes, save it” → `form.requestSubmit()`  
3. `submit` fires again → Swal shows a second time (loop risk)

`requestSubmit()` dispatches a new submit event; `form.submit()` does not. Pages that used `requestSubmit()` without a “already confirmed” guard were affected.

Secondary factor: the same handler was copy-pasted on many Index modal pages while `site.js` also applied anti double-submit, but **no global confirm helper** existed.

## Standardized pattern (`wwwroot/js/site.js`)

Global `HbForm` helper:

| Step | Behavior |
|------|----------|
| First submit | `preventDefault` → single Swal |
| User confirms | `data-hb-confirmed="true"` → `requestSubmit()` |
| Second submit | Guard sees confirmed flag → **no second Swal**, form posts once |
| After confirm | `data-submitted` + disabled buttons + spinner (anti double-submit) |
| Modal reopen | `show.bs.modal` resets confirmed/submitted state |

### Usage

**Default save** — add class to form:

```html
<form method="post" class="confirm-submit">
```

Optional customization:

```html
data-hb-confirm-title="Save user?"
data-hb-confirm-text="Please confirm before saving."
data-hb-confirm-button="Yes, save it"
```

**Deactivate / activate presets:**

- `confirm-deactivate`
- `confirm-deactivate-user`
- `confirm-activate-user`

**Custom validation before confirm** — mark form and use `HbForm.confirmAndSubmit`:

```html
<form class="confirm-submit" data-hb-custom-confirm="true">
```

```javascript
form.addEventListener('submit', function (e) {
    if (HbForm.isConfirmed(form)) return;
    e.preventDefault();
    if (!validate()) return;
    HbForm.confirmAndSubmit(form, { title: '...', text: '...' });
});
```

**Button-triggered submit** (Settings, credit limit):

```javascript
HbForm.confirmAndSubmit(document.getElementById('settingsForm'), options);
```

## Module audit table

| Module | Save confirmation | Duplicate submit risk (before) | Submission lock | Notes |
|--------|-------------------|-------------------------------|-----------------|-------|
| Customers | Yes | **Yes — fixed** | Yes | Global `HbForm` |
| Suppliers | Yes | **Yes — fixed** | Yes | |
| Products | Yes (delete) | Low | Yes | Delete via button + hidden form |
| Categories | Yes | **Yes — fixed** | Yes | |
| Units | Yes | **Yes — fixed** | Yes | |
| Purchase Orders | Yes | **Yes — fixed** | Yes | `confirm-submit` + custom validation where needed |
| Stock In | Yes | **Yes — fixed** | Yes | Custom validation + `HbForm` |
| Stock Adjustments | Yes | **Yes — fixed** | Yes | Custom validation + `HbForm` |
| Branch Transfers | Yes | **Yes — fixed** | Yes | Details forms use `confirm-submit` |
| Quotations | Yes | Low | Yes | Details actions use `form.submit()` |
| Delivery Receipts | Yes | Low | Yes | Details use `form.submit()` |
| Sales Returns | Via Sales module | Low | Yes | |
| Damaged Goods | Yes | **Yes — fixed** | Yes | Create + Details `confirm-submit` |
| Supplier Returns | Yes | **Yes — fixed** | Yes | Create + Details `confirm-submit` |
| Expenses | Yes | Low | Yes | Global handler only |
| Settings | Yes | Low | Yes | `HbForm.confirmAndSubmit` |
| Branches | Yes | **Yes — fixed** | Yes | Modals + `HbForm.confirmAndSubmit` for row actions |
| Users | Yes | **Yes — fixed** | Yes | Global + reset password `HbForm` |
| Roles | Yes | Low | Yes | Button Swal → `form.submit()` |
| Subscription Plans | Yes | Low | Yes | Index action forms |
| Tenants | Yes | Low | Yes | `form.submit()` on tenant actions |

## Delete / void / cancel actions

| Area | Single Swal | Single request | Notes |
|------|-------------|----------------|-------|
| Customer deactivate | Yes | Yes | `confirm-deactivate` + `HbForm` |
| Supplier/Category/Unit deactivate | Yes | Yes | `data-hb-*` text on form |
| User deactivate/activate | Yes | Yes | Class presets in `site.js` |
| PO send/cancel | Yes | Yes | Button-triggered, no form listener loop |
| Branch delete/activate/deactivate | Yes | Yes | Hidden forms |
| Product delete | Yes | Yes | `deleteForm` + button Swal |
| Damaged goods / supplier return actions | Yes | Yes | `form.submit()` on Details |
| Settings backup request | Yes | Yes | Button-only Swal |
| Tenant routing actions | Yes | Yes | `form.submit()` |

## Modules fixed (this hotfix)

- `wwwroot/js/site.js` — `HbForm` + confirmed guard + modal reset
- `Views/Customers/Index.cshtml`
- `Views/Suppliers/Index.cshtml`
- `Views/Categories/Index.cshtml`
- `Views/Units/Index.cshtml`
- `Views/Users/Index.cshtml`
- `Views/RolePermissions/Index.cshtml`
- `Views/StockIn/Index.cshtml`
- `Views/StockAdjustment/Create.cshtml`
- `Views/POS/Index.cshtml`
- `Views/Settings/Index.cshtml` (settings save + logo upload)
- `Views/Sales/Index.cshtml` (void sale)
- `Views/Roles/Index.cshtml` (create role)
- `Views/CustomerCollections/Index.cshtml` (save payment)
- **Phase 5.3.2:** Purchase Orders, Branches, Branch Transfers, Damaged Goods, Supplier Returns

## QA

Automated static audit:

```bash
dotnet run -- --qa-save-confirm
```

Checks:

- `HbForm` present in `site.js`
- No remaining `document.querySelectorAll('.confirm-submit').forEach` handlers
- No unguarded Swal → `form.requestSubmit()` loops

Manual verification checklist:

1. Customer Create — one Swal, one record  
2. Customer Edit — one Swal, one update  
3. Product Create (modal) — confirm once  
4. Supplier Create — confirm once  
5. Settings Save — confirm once  
6. Tenant Create — confirm once (SuperAdmin)  
7. Damaged Goods Create — confirm once  

## Remaining risks

- **Branch row actions** (delete/activate/deactivate/set main) use `HbForm.confirmAndSubmit` with dynamic HTML — not raw Swal, but still page-local JS wrappers.
- **Purchase Orders Create/Receive** retain validation-only `Swal.fire` warnings (not submit confirmations).
- Pages using `form.submit()` bypass the submit-event confirm handler (intentional for some legacy action forms); anti double-submit still applies on next navigation.

## Build

`dotnet build` — target 0 errors, 0 warnings.

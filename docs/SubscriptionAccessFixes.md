# Subscription Access Fixes

**Date:** 2026-07-15

---

## Files changed

| File | Reason |
|------|--------|
| `Configuration/SaaSSettings.cs` | Business timezone configuration |
| `Services/IBusinessClock.cs` | Centralized clock abstraction |
| `Services/BusinessClock.cs` | TimeProvider + business timezone |
| `Services/TenantSubscriptionAccessService.cs` | Single eligibility decision |
| `Services/TenantSubscriptionCacheInvalidator.cs` | Invalidate cache on SuperAdmin edits |
| `Services/TenantStatusFilter.cs` | Per-request enforcement + AJAX 403 |
| `Services/TenantLimitGuard.cs` | Widget uses `EffectiveStatus` |
| `Controllers/AccountController.cs` | Login enforcement + SubscriptionExpired page |
| `Controllers/TenantsController.cs` | ExtendSubscription + cache invalidation |
| `Views/Account/SubscriptionExpired.cshtml` | Tenant-facing expired page |
| `Views/Home/Index.cshtml` | Widget status + encoding fix |
| `Views/Tenants/Details.cshtml` | Effective status + extend form |
| `Tools/SubscriptionAccessQaRunner.cs` | Automated test matrix |
| `Program.cs` | DI registration + QA hook |
| `appsettings.json` | `SaaSSettings` section |

---

## Minimality

- No schema migration
- No auth architecture change
- No routing redesign
- Reused existing `TenantStatusFilter` (enhanced, not replaced)
- Decoupled password reset unchanged

---

## Regression risk

| Area | Risk | Mitigation |
|------|------|------------|
| Active tenants before expiration | Low | QA tests + runtime audit |
| SuperAdmin access | Low | Explicit bypass preserved |
| Timezone misconfiguration | Medium | Configurable `BusinessTimeZoneId` |
| Cache staleness | Low | 2-min TTL + invalidation on edit |

---

## Rollback

1. Revert `TenantSubscriptionAccessService` usage in filter/login
2. Restore UTC comparison in filter/login (not recommended)
3. Remove `SaaSSettings` section (falls back to Asia/Manila in code default)

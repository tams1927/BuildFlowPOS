using System.Text.RegularExpressions;
using HardwareManagementSystem.Configuration;
using HardwareManagementSystem.Data;
using HardwareManagementSystem.Data.Seeders;
using HardwareManagementSystem.Models;
using HardwareManagementSystem.Services;
using HardwareManagementSystem.Services.TenantDatabases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HardwareManagementSystem.Controllers
{
    [Authorize(Roles = "SuperAdmin")]
    public class TenantsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly AuditService _auditService;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly TenantLimitGuard _limitGuard;
        private readonly ITenantDatabaseResolver _databaseResolver;
        private readonly ITenantDatabaseProvisioningService _provisioningService;
        private readonly ITenantDataMigrationService _dataMigrationService;
        private readonly ITenantSchemaMigrationService _schemaMigrationService;
        private readonly ILogger<TenantsController> _logger;
        private readonly TenantDatabaseSettings _dbSettings;
        private readonly ITenantSubscriptionAccessService _subscriptionAccess;
        private readonly ITenantSubscriptionCacheInvalidator _subscriptionCache;
        private readonly IBusinessClock _businessClock;

        public TenantsController(
            ApplicationDbContext context,
            AuditService auditService,
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            TenantLimitGuard limitGuard,
            ITenantDatabaseResolver databaseResolver,
            ITenantDatabaseProvisioningService provisioningService,
            ITenantDataMigrationService dataMigrationService,
            ITenantSchemaMigrationService schemaMigrationService,
            ILogger<TenantsController> logger,
            IOptions<TenantDatabaseSettings> dbSettings,
            ITenantSubscriptionAccessService subscriptionAccess,
            ITenantSubscriptionCacheInvalidator subscriptionCache,
            IBusinessClock businessClock)
        {
            _context    = context;
            _auditService = auditService;
            _userManager = userManager;
            _roleManager = roleManager;
            _limitGuard  = limitGuard;
            _databaseResolver = databaseResolver;
            _provisioningService = provisioningService;
            _dataMigrationService = dataMigrationService;
            _schemaMigrationService = schemaMigrationService;
            _logger = logger;
            _dbSettings = dbSettings.Value;
            _subscriptionAccess = subscriptionAccess;
            _subscriptionCache = subscriptionCache;
            _businessClock = businessClock;
        }

        // ─────────────────────────────────────────────────────────────
        // LIST
        // ─────────────────────────────────────────────────────────────

        public async Task<IActionResult> Index()
        {
            ViewData["Title"] = "Tenants";
            var tenants = await _context.Tenants
                .AsNoTracking()
                .Include(t => t.SubscriptionPlan)
                .OrderBy(t => t.Name)
                .ToListAsync();
            return View(tenants);
        }

        // ─────────────────────────────────────────────────────────────
        // DETAILS
        // ─────────────────────────────────────────────────────────────

        public async Task<IActionResult> Details(int id)
        {
            ViewData["Title"] = "Tenant Details";

            var tenant = await _context.Tenants
                .AsNoTracking()
                .Include(t => t.SubscriptionPlan)
                .FirstOrDefaultAsync(t => t.Id == id);

            if (tenant == null) return NotFound();

            // Usage summary + subscription access (centralized)
            var usage = await _limitGuard.GetUsageAsync(id);
            ViewBag.Usage = usage;
            ViewBag.SubscriptionAccess = _subscriptionAccess.Evaluate(tenant);

            // Tenant users (exclude SuperAdmin)
            var tenantUsers = await _userManager.Users
                .AsNoTracking()
                .Where(u => u.TenantId == id)
                .OrderBy(u => u.FullName)
                .ToListAsync();

            var userVms = new List<TenantUserVm>();
            foreach (var u in tenantUsers)
            {
                var roles = await _userManager.GetRolesAsync(u);
                if (roles.Contains("SuperAdmin")) continue;
                userVms.Add(new TenantUserVm
                {
                    Id       = u.Id,
                    FullName = u.FullName,
                    Username = u.UserName ?? "",
                    Email    = u.Email ?? "",
                    Role     = roles.FirstOrDefault() ?? "—",
                    IsActive = u.IsActive,
                    ForcePasswordChange = u.ForcePasswordChange
                });
            }
            ViewBag.Users = userVms;

            // Schema pending count — only for provisioned dedicated DBs
            ViewBag.PendingMigrations = tenant.IsDatabaseProvisioned
                ? await _schemaMigrationService.GetPendingMigrationCountAsync(id)
                : 0;

            return View(tenant);
        }

        // ─────────────────────────────────────────────────────────────
        // CREATE
        // ─────────────────────────────────────────────────────────────

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            ViewData["Title"] = "New Tenant";
            await LoadPlansDropdownAsync();
            return View(new Tenant
            {
                Status      = TenantStatus.Trial,
                MaxBranches = 3,
                MaxUsers    = 10,
                MaxProducts = 500,
                IsActive    = true
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
            Tenant tenant,
            bool    createOwnerAccount = false,
            string? ownerFullName      = null,
            string? ownerUsername      = null,
            string? ownerEmail         = null,
            string? ownerPassword      = null,
            string? ownerRole          = "TenantAdmin")
        {
            ModelState.Remove(nameof(Tenant.SubscriptionPlan));

            // Subscription Plan is required for all new tenants
            if (tenant.SubscriptionPlanId == null)
                ModelState.AddModelError(nameof(Tenant.SubscriptionPlanId), "Please select a Subscription Plan.");

            if (createOwnerAccount)
            {
                if (string.IsNullOrWhiteSpace(ownerFullName))
                    ModelState.AddModelError("ownerFullName", "Full Name is required for the owner account.");
                if (string.IsNullOrWhiteSpace(ownerUsername))
                    ModelState.AddModelError("ownerUsername", "Username is required for the owner account.");
                if (string.IsNullOrWhiteSpace(ownerEmail))
                    ModelState.AddModelError("ownerEmail", "Email is required for the owner account.");
                if (string.IsNullOrWhiteSpace(ownerPassword) || ownerPassword.Length < 6)
                    ModelState.AddModelError("ownerPassword", "Password must be at least 6 characters.");
            }

            if (!ModelState.IsValid)
            {
                var errs = string.Join("; ", ModelState
                    .Where(e => e.Value?.Errors.Any() == true)
                    .SelectMany(e => e.Value!.Errors.Select(x => $"{e.Key}: {x.ErrorMessage}")));
                _logger.LogWarning("Tenant Create rejected by ModelState: {Errors}", errs);

                ViewData["Title"]              = "New Tenant";
                ViewBag.CreateOwnerAccount     = createOwnerAccount;
                ViewBag.OwnerFullName          = ownerFullName;
                ViewBag.OwnerUsername          = ownerUsername;
                ViewBag.OwnerEmail             = ownerEmail;
                await LoadPlansDropdownAsync();
                return View(tenant);
            }

            var codeExists = await _context.Tenants.AnyAsync(t => t.Code == tenant.Code);
            if (codeExists)
            {
                ModelState.AddModelError(nameof(tenant.Code), "A tenant with this code already exists.");
                ViewData["Title"]          = "New Tenant";
                ViewBag.CreateOwnerAccount = createOwnerAccount;
                ViewBag.OwnerFullName      = ownerFullName;
                ViewBag.OwnerUsername      = ownerUsername;
                ViewBag.OwnerEmail         = ownerEmail;
                await LoadPlansDropdownAsync();
                return View(tenant);
            }

            // Pre-validate owner username/email uniqueness before saving tenant
            if (createOwnerAccount && !string.IsNullOrWhiteSpace(ownerUsername))
            {
                if (await _userManager.FindByNameAsync(ownerUsername.Trim()) != null)
                {
                    ModelState.AddModelError("ownerUsername", $"Username '{ownerUsername.Trim()}' is already taken.");
                    ViewData["Title"]          = "New Tenant";
                    ViewBag.CreateOwnerAccount = createOwnerAccount;
                    ViewBag.OwnerFullName      = ownerFullName;
                    ViewBag.OwnerUsername      = ownerUsername;
                    ViewBag.OwnerEmail         = ownerEmail;
                    await LoadPlansDropdownAsync();
                    return View(tenant);
                }

                if (!string.IsNullOrWhiteSpace(ownerEmail)
                    && await _userManager.FindByEmailAsync(ownerEmail.Trim()) != null)
                {
                    ModelState.AddModelError("ownerEmail", $"Email '{ownerEmail.Trim()}' is already registered.");
                    ViewData["Title"]          = "New Tenant";
                    ViewBag.CreateOwnerAccount = createOwnerAccount;
                    ViewBag.OwnerFullName      = ownerFullName;
                    ViewBag.OwnerUsername      = ownerUsername;
                    ViewBag.OwnerEmail         = ownerEmail;
                    await LoadPlansDropdownAsync();
                    return View(tenant);
                }
            }

            tenant.Code         = tenant.Code.Trim().ToUpper();
            tenant.CreatedAtUtc = DateTime.UtcNow;
            tenant.UpdatedAtUtc = DateTime.UtcNow;

            _context.Tenants.Add(tenant);
            await _context.SaveChangesAsync();  // tenant.Id is now assigned

            // ── Auto-create a tenant-scoped SystemSettings row in the shared DB ──
            // Kept for backward-compatibility; the dedicated DB also gets its own
            // SystemSetting row during provisioning (SeedMinimumDataAsync).
            var tenantSettings = new SystemSetting
            {
                TenantId          = tenant.Id,
                BusinessName      = tenant.Name,
                Email             = ownerEmail?.Trim() ?? "",
                BusinessAddress   = tenant.Address ?? "",
                DefaultVatPercent = 12,
                CurrencySymbol    = "₱",
                ReceiptPaperSize  = "80mm",
                ThemeColor        = "white-blue",
                TaxMode           = "VAT",
                UpdatedAt         = DateTime.Now
            };
            _context.SystemSettings.Add(tenantSettings);
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(User, "Tenants", "Create",
                $"Tenant created: {tenant.Name} ({tenant.Code})");

            // ── RC1.7: Auto-provision dedicated database immediately ──────────
            // BuildFlow SaaS strategy: One Tenant = One Dedicated Database.
            // Generate a safe database name from the tenant code and numeric ID,
            // then provision, migrate, seed, and enable routing in one step.
            string? provisioningWarning = null;
            try
            {
                // Generate a SQL-safe database name: letters/digits/underscores only.
                // The prefix is read from TenantDatabaseSettings:DatabasePrefix in appsettings.json.
                var safeCode = Regex.Replace(tenant.Code, @"[^A-Za-z0-9]", "_");
                var dbName   = $"{_dbSettings.EffectivePrefix}_{safeCode}_{tenant.Id}";

                tenant.DatabaseMode = TenantDatabaseMode.Dedicated;
                tenant.DatabaseName = dbName;
                tenant.UpdatedAtUtc = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                _logger.LogInformation(
                    "RC1.7: Auto-provisioning dedicated DB '{DbName}' for tenant {TenantId} ({Code}).",
                    dbName, tenant.Id, tenant.Code);

                var provisionResult = await _provisioningService.ProvisionAsync(tenant.Id);

                if (provisionResult.Success)
                {
                    // Mark as directly initialised — no manual migration step required.
                    tenant.DataMigrated         = true;
                    tenant.DataMigratedAtUtc    = DateTime.UtcNow;
                    tenant.RoutingEnabled       = true;
                    tenant.IsDirectlyProvisioned = true;
                    tenant.UpdatedAtUtc          = DateTime.UtcNow;
                    await _context.SaveChangesAsync();

                    _databaseResolver.Invalidate(tenant.Id);

                    await _auditService.LogAsync(User, "Tenants", "RC17_DIRECT_PROVISION",
                        $"Dedicated database '{dbName}' auto-provisioned and routing enabled for tenant " +
                        $"'{tenant.Name}' ({tenant.Code}).",
                        "Tenant", tenant.Id.ToString(), GetClientIp());

                    _logger.LogInformation(
                        "RC1.7: Tenant {TenantId} ({Code}) now running on dedicated DB '{DbName}'.",
                        tenant.Id, tenant.Code, dbName);
                }
                else
                {
                    // Provisioning failed — tenant record exists but DB is not ready.
                    // Show a warning so SuperAdmin can retry from Details.
                    provisioningWarning = provisionResult.Message;
                    _logger.LogError(
                        "RC1.7: Provisioning failed for tenant {TenantId} ({Code}): {Message}",
                        tenant.Id, tenant.Code, provisionResult.Message);

                    await _auditService.LogAsync(User, "Tenants", "RC17_DIRECT_PROVISION_FAILED",
                        $"Auto-provisioning FAILED for tenant '{tenant.Name}' ({tenant.Code}): {provisionResult.Message}",
                        "Tenant", tenant.Id.ToString(), GetClientIp());
                }
            }
            catch (Exception ex)
            {
                provisioningWarning = $"Auto-provisioning encountered an error: {ex.Message}";
                _logger.LogError(ex,
                    "RC1.7: Unexpected error during auto-provisioning for tenant {TenantId}.",
                    tenant.Id);
            }

            // ── Optional: create first owner/admin account ────────────
            if (createOwnerAccount
                && !string.IsNullOrWhiteSpace(ownerUsername)
                && !string.IsNullOrWhiteSpace(ownerPassword))
            {
                var assignRole = "TenantAdmin";

                var newOwner = new ApplicationUser
                {
                    UserName            = ownerUsername.Trim(),
                    Email               = ownerEmail?.Trim(),
                    FullName            = ownerFullName?.Trim() ?? ownerUsername.Trim(),
                    EmailConfirmed      = true,
                    IsActive            = true,
                    TenantId            = tenant.Id,
                    ForcePasswordChange = false
                };

                var createResult = await _userManager.CreateAsync(newOwner, ownerPassword);

                if (createResult.Succeeded)
                {
                    if (!await _roleManager.RoleExistsAsync(assignRole))
                        await _roleManager.CreateAsync(new IdentityRole(assignRole));

                    await _userManager.AddToRoleAsync(newOwner, assignRole);

                    // Ensure this role has permission rows so the user can log in immediately.
                    await DbSeeder.SeedRolePermissionsAsync(HttpContext.RequestServices);

                    await _auditService.LogAsync(User, "Tenants", "CreateOwner",
                        $"Owner account '{newOwner.UserName}' ({assignRole}) created for tenant {tenant.Name}");
                }
                else
                {
                    var errors = string.Join(", ", createResult.Errors.Select(e => e.Description));
                    TempData["ErrorMessage"] =
                        $"Tenant '{tenant.Name}' created, but owner account failed: {errors}";
                }
            }

            // Build final success/warning message.
            if (provisioningWarning != null)
            {
                TempData["ErrorMessage"] =
                    $"Tenant '{tenant.Name}' was saved, but dedicated database setup failed: {provisioningWarning} " +
                    "You can retry provisioning from the Tenant Details page.";
            }
            else if (createOwnerAccount && !string.IsNullOrWhiteSpace(ownerUsername)
                     && TempData["ErrorMessage"] == null)
            {
                TempData["SuccessMessage"] =
                    $"Tenant '{tenant.Name}' and owner account '{ownerUsername!.Trim()}' created. " +
                    "Dedicated database provisioned and routing is live.";
            }
            else if (TempData["ErrorMessage"] == null)
            {
                TempData["SuccessMessage"] =
                    $"Tenant '{tenant.Name}' created. Dedicated database provisioned and routing is live.";
            }

            return RedirectToAction(nameof(Details), new { id = tenant.Id });
        }

        // ─────────────────────────────────────────────────────────────
        // EDIT
        // ─────────────────────────────────────────────────────────────

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            ViewData["Title"] = "Edit Tenant";
            var tenant = await _context.Tenants.FindAsync(id);
            if (tenant == null) return NotFound();
            await LoadPlansDropdownAsync();
            return View(tenant);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Tenant tenant)
        {
            if (id != tenant.Id) return BadRequest();

            ModelState.Remove(nameof(Tenant.SubscriptionPlan));

            if (!ModelState.IsValid)
            {
                var errs = string.Join("; ", ModelState
                    .Where(e => e.Value?.Errors.Any() == true)
                    .SelectMany(e => e.Value!.Errors.Select(x => $"{e.Key}: {x.ErrorMessage}")));
                _logger.LogWarning("Tenant Edit {Id} rejected by ModelState: {Errors}", id, errs);

                ViewData["Title"] = "Edit Tenant";
                await LoadPlansDropdownAsync();
                return View(tenant);
            }

            var codeExists = await _context.Tenants.AnyAsync(t => t.Code == tenant.Code && t.Id != id);
            if (codeExists)
            {
                ModelState.AddModelError(nameof(tenant.Code), "A tenant with this code already exists.");
                ViewData["Title"] = "Edit Tenant";
                await LoadPlansDropdownAsync();
                return View(tenant);
            }

            var existing = await _context.Tenants.FindAsync(id);
            if (existing == null) return NotFound();

            existing.Name               = tenant.Name;
            existing.Code               = tenant.Code.Trim().ToUpper();
            existing.OwnerName          = tenant.OwnerName;
            existing.Email              = tenant.Email;
            existing.Phone              = tenant.Phone;
            existing.Address            = tenant.Address;
            existing.Notes              = tenant.Notes;
            existing.IsActive           = tenant.IsActive;
            existing.Status             = tenant.Status;
            existing.SubscriptionPlanId = tenant.SubscriptionPlanId;
            existing.ExpirationDate     = tenant.ExpirationDate;
            existing.MaxBranches        = tenant.MaxBranches;
            existing.MaxUsers           = tenant.MaxUsers;
            existing.MaxProducts        = tenant.MaxProducts;
            existing.UpdatedAtUtc       = DateTime.UtcNow;

            // ── Phase 5.0B: safe database-routing metadata ────────────────
            // Only DatabaseMode and DatabaseName are editable from the UI.
            // ConnectionString remains backend-only and is never bound here.
            existing.DatabaseMode = tenant.DatabaseMode;
            existing.DatabaseName = string.IsNullOrWhiteSpace(tenant.DatabaseName)
                ? null
                : tenant.DatabaseName.Trim();

            await _context.SaveChangesAsync();

            _subscriptionCache.InvalidateTenant(id);

            await _auditService.LogAsync(User, "Tenants", "Edit",
                $"Tenant updated: {existing.Name} ({existing.Code})");

            TempData["SuccessMessage"] = $"Tenant '{existing.Name}' updated.";
            return RedirectToAction(nameof(Index));
        }

        // ─────────────────────────────────────────────────────────────
        // SUSPEND / REACTIVATE
        // ─────────────────────────────────────────────────────────────

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Suspend(int id)
        {
            var tenant = await _context.Tenants.FindAsync(id);
            if (tenant == null) return NotFound();

            tenant.Status       = TenantStatus.Suspended;
            tenant.IsActive     = false;
            tenant.UpdatedAtUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            _subscriptionCache.InvalidateTenant(id);

            await _auditService.LogAsync(User, "Tenants", "TENANT_SUSPENDED",
                $"Tenant suspended: {tenant.Name} ({tenant.Code})",
                "Tenant", tenant.Id.ToString());

            TempData["SuccessMessage"] = $"Tenant '{tenant.Name}' has been suspended.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reactivate(int id)
        {
            var tenant = await _context.Tenants.FindAsync(id);
            if (tenant == null) return NotFound();

            tenant.Status       = TenantStatus.Active;
            tenant.IsActive     = true;
            tenant.UpdatedAtUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            _subscriptionCache.InvalidateTenant(id);

            await _auditService.LogAsync(User, "Tenants", "TENANT_REACTIVATED",
                $"Tenant reactivated: {tenant.Name} ({tenant.Code})",
                "Tenant", tenant.Id.ToString());

            TempData["SuccessMessage"] = $"Tenant '{tenant.Name}' has been reactivated.";
            return RedirectToAction(nameof(Index));
        }

        // ─────────────────────────────────────────────────────────────
        // EXTEND / RENEW SUBSCRIPTION
        // ─────────────────────────────────────────────────────────────

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ExtendSubscription(
            int id,
            int? extendDays,
            int? extendMonths,
            DateTime? newExpirationDate,
            bool reactivateTrial = false)
        {
            var tenant = await _context.Tenants.FindAsync(id);
            if (tenant == null) return NotFound();

            var businessToday = _businessClock.BusinessToday;
            var oldExpiration = tenant.ExpirationDate;
            var oldStatus = tenant.Status;

            if (extendDays.HasValue && extendDays.Value <= 0)
            {
                TempData["ErrorMessage"] = "Extension days must be greater than zero.";
                return RedirectToAction(nameof(Details), new { id });
            }
            if (extendMonths.HasValue && extendMonths.Value <= 0)
            {
                TempData["ErrorMessage"] = "Extension months must be greater than zero.";
                return RedirectToAction(nameof(Details), new { id });
            }

            DateTime computed;
            if (newExpirationDate.HasValue)
            {
                var picked = DateOnly.FromDateTime(newExpirationDate.Value.Date);
                if (picked <= businessToday)
                {
                    TempData["ErrorMessage"] = "New expiration must be after the current business date.";
                    return RedirectToAction(nameof(Details), new { id });
                }
                computed = picked.ToDateTime(TimeOnly.MinValue);
            }
            else if (extendDays.HasValue && extendDays.Value > 0)
            {
                var baseDate = ExtensionBaseDate(tenant.ExpirationDate, businessToday);
                computed = baseDate.AddDays(extendDays.Value);
            }
            else if (extendMonths.HasValue && extendMonths.Value > 0)
            {
                var baseDate = ExtensionBaseDate(tenant.ExpirationDate, businessToday);
                computed = baseDate.AddMonths(extendMonths.Value);
            }
            else
            {
                TempData["ErrorMessage"] = "Provide extension days, months, or a new expiration date.";
                return RedirectToAction(nameof(Details), new { id });
            }

            tenant.ExpirationDate = computed;
            if (tenant.Status is TenantStatus.Expired or TenantStatus.Suspended || reactivateTrial)
                tenant.Status = reactivateTrial ? TenantStatus.Trial : TenantStatus.Active;
            tenant.IsActive = true;
            tenant.UpdatedAtUtc = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            _subscriptionCache.InvalidateTenant(id);

            await _auditService.LogAsync(User, "Tenants", "SUBSCRIPTION_EXTENDED",
                $"Subscription extended for {tenant.Name} ({tenant.Code}): " +
                $"status {oldStatus} → {tenant.Status}, " +
                $"expiration {(oldExpiration?.ToString("yyyy-MM-dd") ?? "none")} → {computed:yyyy-MM-dd}",
                "Tenant", tenant.Id.ToString());

            TempData["SuccessMessage"] =
                $"Subscription extended to {computed:MMMM d, yyyy}. Tenant access is restored on next login.";
            return RedirectToAction(nameof(Details), new { id });
        }

        /// <summary>
        /// Active: extend from existing expiration. Expired: extend from current business date.
        /// </summary>
        private static DateTime ExtensionBaseDate(DateTime? currentExpiration, DateOnly businessToday)
        {
            if (!currentExpiration.HasValue)
                return businessToday.ToDateTime(TimeOnly.MinValue);

            var cur = DateOnly.FromDateTime(currentExpiration.Value.Date);
            var access = cur >= businessToday;
            var baseDate = access ? cur : businessToday;
            return baseDate.ToDateTime(TimeOnly.MinValue);
        }

        // ─────────────────────────────────────────────────────────────
        // USER ACTIVATION CONTROLS   (SuperAdmin only)
        // ─────────────────────────────────────────────────────────────

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ActivateUser(string userId, int tenantId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null || user.TenantId != tenantId) return NotFound();

            if (await _userManager.IsInRoleAsync(user, "SuperAdmin"))
            {
                TempData["ErrorMessage"] = "Cannot modify the SuperAdmin account.";
                return RedirectToAction(nameof(Details), new { id = tenantId });
            }

            user.IsActive = true;
            await _userManager.UpdateAsync(user);

            await _auditService.LogAsync(User, "Tenants", "USER_ACTIVATED",
                $"User '{user.UserName}' activated by SuperAdmin.",
                "User", user.Id);

            TempData["SuccessMessage"] = $"User '{user.UserName}' activated.";
            return RedirectToAction(nameof(Details), new { id = tenantId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeactivateUser(string userId, int tenantId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null || user.TenantId != tenantId) return NotFound();

            if (await _userManager.IsInRoleAsync(user, "SuperAdmin"))
            {
                TempData["ErrorMessage"] = "Cannot deactivate the SuperAdmin account.";
                return RedirectToAction(nameof(Details), new { id = tenantId });
            }

            user.IsActive = false;
            await _userManager.UpdateAsync(user);

            await _auditService.LogAsync(User, "Tenants", "USER_DEACTIVATED",
                $"User '{user.UserName}' deactivated by SuperAdmin.",
                "User", user.Id);

            TempData["SuccessMessage"] = $"User '{user.UserName}' deactivated.";
            return RedirectToAction(nameof(Details), new { id = tenantId });
        }

        // ─────────────────────────────────────────────────────────────
        // PASSWORD RESET   (SuperAdmin only, no old password required)
        // ─────────────────────────────────────────────────────────────

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(
            string userId,
            int    tenantId,
            string newPassword,
            string confirmPassword)
        {
            if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
            {
                TempData["ErrorMessage"] = "Password must be at least 6 characters.";
                return RedirectToAction(nameof(Details), new { id = tenantId });
            }

            if (newPassword != confirmPassword)
            {
                TempData["ErrorMessage"] = "Passwords do not match.";
                return RedirectToAction(nameof(Details), new { id = tenantId });
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null || user.TenantId != tenantId) return NotFound();

            if (await _userManager.IsInRoleAsync(user, "SuperAdmin"))
            {
                TempData["ErrorMessage"] = "Cannot reset the SuperAdmin password from tenant screens.";
                return RedirectToAction(nameof(Details), new { id = tenantId });
            }

            // Remove old password and set new one (no old password required for SuperAdmin reset)
            var removeResult = await _userManager.RemovePasswordAsync(user);
            if (!removeResult.Succeeded)
            {
                TempData["ErrorMessage"] = "Password reset failed: " +
                    string.Join(", ", removeResult.Errors.Select(e => e.Description));
                return RedirectToAction(nameof(Details), new { id = tenantId });
            }

            var addResult = await _userManager.AddPasswordAsync(user, newPassword);
            if (!addResult.Succeeded)
            {
                TempData["ErrorMessage"] = "Password reset failed: " +
                    string.Join(", ", addResult.Errors.Select(e => e.Description));
                return RedirectToAction(nameof(Details), new { id = tenantId });
            }

            // Force the user to change their password on next login
            user.ForcePasswordChange = true;
            await _userManager.UpdateAsync(user);

            await _auditService.LogAsync(User, "Tenants", "PASSWORD_RESET",
                $"Password reset for user '{user.UserName}' (TenantId: {tenantId}) by SuperAdmin. ForcePasswordChange set.",
                "User", user.Id);

            TempData["SuccessMessage"] = $"Password reset for '{user.UserName}'. They will be required to change it on next login.";
            return RedirectToAction(nameof(Details), new { id = tenantId });
        }

        // ─────────────────────────────────────────────────────────────
        // DEDICATED DATABASE PROVISIONING   (SuperAdmin only — Phase 5.0C)
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Creates, migrates, and seeds a dedicated database for the tenant.
        /// Phase 5.0C: this prepares the database only — no tenant data is moved and
        /// routing stays inactive. The tenant continues to use the shared database.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Provision(int id)
        {
            var tenant = await _context.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
            if (tenant == null) return NotFound();

            var result = await _provisioningService.ProvisionAsync(id);

            if (result.Success)
            {
                await _auditService.LogAsync(User, "Tenants", "TENANT_DATABASE_PROVISIONED",
                    $"Dedicated database provisioned for tenant '{tenant.Name}' ({tenant.Code}). " +
                    $"Database: {result.DatabaseName}.",
                    "Tenant", tenant.Id.ToString(), GetClientIp());

                TempData["SuccessMessage"] = result.Message;
            }
            else
            {
                await _auditService.LogAsync(User, "Tenants", "TENANT_DATABASE_PROVISION_FAILED",
                    $"Dedicated database provisioning FAILED for tenant '{tenant.Name}' ({tenant.Code}). " +
                    $"Database: {(string.IsNullOrWhiteSpace(result.DatabaseName) ? "(none)" : result.DatabaseName)}. " +
                    $"Reason: {result.Message}",
                    "Tenant", tenant.Id.ToString(), GetClientIp());

                TempData["ErrorMessage"] = result.Message;
            }

            return RedirectToAction(nameof(Details), new { id });
        }

        // ─────────────────────────────────────────────────────────────
        // SCHEMA MIGRATION   (Phase 5.3.2)
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Apply any pending TenantDbContext EF Core migrations to a single tenant's
        /// dedicated database. Safe to run on an already-current database (no-op).
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpgradeSchema(int id)
        {
            var tenant = await _context.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
            if (tenant == null) return NotFound();

            var result = await _schemaMigrationService.UpgradeSchemaAsync(id);

            if (result.Success)
            {
                var detail = result.WasUpToDate
                    ? $"Schema is already up-to-date (latest: {result.LatestMigration ?? "—"})."
                    : $"{result.MigrationsApplied.Count} migration(s) applied. Latest: {result.LatestMigration ?? "—"}.";

                await _auditService.LogAsync(User, "Tenants", "TENANT_SCHEMA_UPGRADED",
                    $"Schema upgrade for tenant '{tenant.Name}' ({tenant.Code}): {detail}",
                    "Tenant", id.ToString(), GetClientIp());

                TempData["SuccessMessage"] = detail;
            }
            else
            {
                await _auditService.LogAsync(User, "Tenants", "TENANT_SCHEMA_UPGRADE_FAILED",
                    $"Schema upgrade FAILED for tenant '{tenant.Name}' ({tenant.Code}): {result.Error}",
                    "Tenant", id.ToString(), GetClientIp());

                TempData["ErrorMessage"] = $"Schema upgrade failed: {result.Error}";
            }

            return RedirectToAction(nameof(Details), new { id });
        }

        /// <summary>
        /// Apply any pending TenantDbContext EF Core migrations to ALL provisioned dedicated
        /// tenant databases. Reports a summary of what was upgraded. Safe to run repeatedly.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpgradeAllSchemas()
        {
            var results = await _schemaMigrationService.UpgradeAllSchemasAsync();

            var upgraded = results.Count(r => r.Success && !r.WasUpToDate);
            var upToDate = results.Count(r => r.Success && r.WasUpToDate);
            var failed   = results.Count(r => !r.Success);

            var summary = $"Upgrade All Schemas: {upgraded} upgraded, {upToDate} already up-to-date, {failed} failed.";

            await _auditService.LogAsync(User, "Tenants", "TENANT_ALL_SCHEMAS_UPGRADED",
                summary, "Tenant", null, GetClientIp());

            if (failed == 0)
                TempData["SuccessMessage"] = summary;
            else
                TempData["ErrorMessage"] = summary + " Check logs for error details.";

            return RedirectToAction(nameof(Index));
        }

        /// <summary>
        /// Tests connectivity for a tenant's database routing.
        /// Shared → reports shared database. Dedicated + provisioned → opens a real
        /// connection. Dedicated + not provisioned → reports not provisioned.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TestDatabaseConnection(int id)
        {
            var tenant = await _context.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
            if (tenant == null) return NotFound();

            string message;
            bool passed;

            if (tenant.DatabaseMode != TenantDatabaseMode.Dedicated)
            {
                passed  = true;
                message = "Using shared database.";
            }
            else if (tenant.DatabaseProvisionedAtUtc == null ||
                     string.IsNullOrWhiteSpace(tenant.ConnectionString))
            {
                passed  = false;
                message = "Database not provisioned.";
            }
            else
            {
                (passed, message) = await TryOpenConnectionAsync(tenant.ConnectionString);
            }

            await _auditService.LogAsync(User, "Tenants", "TENANT_DATABASE_CONNECTION_TESTED",
                $"Database connection tested for tenant '{tenant.Name}' ({tenant.Code}). " +
                $"Database: {(string.IsNullOrWhiteSpace(tenant.DatabaseName) ? "(shared)" : tenant.DatabaseName)}. " +
                $"Result: {(passed ? "Success" : "Failed")} — {message}",
                "Tenant", tenant.Id.ToString(), GetClientIp());

            if (passed) TempData["SuccessMessage"] = message;
            else        TempData["ErrorMessage"]   = message;

            return RedirectToAction(nameof(Details), new { id });
        }

        // ─────────────────────────────────────────────────────────────
        // PILOT DATA MIGRATION + RUNTIME ROUTING   (SuperAdmin — Phase 5.0D)
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Copies the tenant's operational data into its dedicated database and
        /// validates row counts. No shared data is removed. On success the tenant is
        /// marked DataMigrated; routing is still NOT enabled until the SuperAdmin opts in.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MigrateData(int id)
        {
            var tenant = await _context.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
            if (tenant == null) return NotFound();

            await _auditService.LogAsync(User, "Tenants", "TENANT_DATA_MIGRATION_STARTED",
                $"Data migration started for tenant '{tenant.Name}' ({tenant.Code}). " +
                $"Database: {tenant.DatabaseName ?? "(none)"}.",
                "Tenant", tenant.Id.ToString(), GetClientIp());

            var result = await _dataMigrationService.MigrateAsync(id);

            if (result.Success)
            {
                var summary = string.Join("; ",
                    result.Tables.Select(t => $"{t.TableName}={t.DedicatedCount}"));
                await _auditService.LogAsync(User, "Tenants", "TENANT_DATA_MIGRATION_COMPLETED",
                    $"Data migration completed for tenant '{tenant.Name}' ({tenant.Code}). " +
                    $"Database: {result.DatabaseName}. {result.TotalRowsCopied} rows. Counts: {summary}",
                    "Tenant", tenant.Id.ToString(), GetClientIp());

                TempData["SuccessMessage"] = result.Message;
            }
            else
            {
                await _auditService.LogAsync(User, "Tenants", "TENANT_DATA_MIGRATION_FAILED",
                    $"Data migration FAILED for tenant '{tenant.Name}' ({tenant.Code}). " +
                    $"Database: {tenant.DatabaseName ?? "(none)"}. Reason: {result.Message}",
                    "Tenant", tenant.Id.ToString(), GetClientIp());

                TempData["ErrorMessage"] = result.Message;
            }

            return RedirectToAction(nameof(Details), new { id });
        }

        /// <summary>
        /// Switches live routing ON for a tenant. Only permitted once the dedicated
        /// database is provisioned, data has been migrated + validated, AND the
        /// dedicated database schema is current (no pending EF Core migrations).
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EnableRouting(int id)
        {
            var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.Id == id);
            if (tenant == null) return NotFound();

            if (!tenant.IsDatabaseProvisioned)
            {
                TempData["ErrorMessage"] = "Dedicated database is not provisioned.";
                return RedirectToAction(nameof(Details), new { id });
            }
            if (!tenant.DataMigrated)
            {
                TempData["ErrorMessage"] = "Data must be migrated and validated before enabling routing.";
                return RedirectToAction(nameof(Details), new { id });
            }

            // Phase 5.3.2 — schema guard: refuse to route if the dedicated DB is behind.
            var pendingCount = await _schemaMigrationService.GetPendingMigrationCountAsync(id);
            if (pendingCount > 0)
            {
                TempData["ErrorMessage"] =
                    $"Cannot enable routing: the dedicated database has {pendingCount} pending schema migration(s). " +
                    "Use 'Upgrade Schema' first to bring the database up-to-date.";
                return RedirectToAction(nameof(Details), new { id });
            }

            tenant.RoutingEnabled = true;
            tenant.UpdatedAtUtc   = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            _databaseResolver.Invalidate(id);

            await _auditService.LogAsync(User, "Tenants", "TENANT_ROUTING_ENABLED",
                $"Dedicated database routing ENABLED for tenant '{tenant.Name}' ({tenant.Code}). " +
                $"Database: {tenant.DatabaseName}.",
                "Tenant", tenant.Id.ToString(), GetClientIp());

            TempData["SuccessMessage"] =
                $"Routing enabled. '{tenant.Name}' now uses its dedicated database. Rollback remains available.";
            return RedirectToAction(nameof(Details), new { id });
        }

        /// <summary>
        /// Rollback — switches routing OFF. The tenant instantly falls back to the
        /// shared database. No data is deleted from either database.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DisableRouting(int id)
        {
            var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.Id == id);
            if (tenant == null) return NotFound();

            tenant.RoutingEnabled = false;
            tenant.UpdatedAtUtc   = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            _databaseResolver.Invalidate(id);

            await _auditService.LogAsync(User, "Tenants", "TENANT_ROUTING_DISABLED",
                $"Dedicated database routing DISABLED (rollback) for tenant '{tenant.Name}' ({tenant.Code}). " +
                $"Tenant reverted to shared database. No data deleted.",
                "Tenant", tenant.Id.ToString(), GetClientIp());

            TempData["SuccessMessage"] =
                $"Routing disabled. '{tenant.Name}' has rolled back to the shared database. No data was deleted.";
            return RedirectToAction(nameof(Details), new { id });
        }

        // ─────────────────────────────────────────────────────────────
        // DATABASE DIAGNOSTIC   (SuperAdmin only — Phase 5.0B / 5.0C / 5.0D)
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Read-only diagnostic view of a tenant's database routing metadata.
        /// Phase 5.0C: shows routing, whether the dedicated database exists, whether
        /// a live connection succeeds, provision status, and last applied migration.
        /// Routing is still NOT active — every tenant uses the shared database.
        /// Never displays a raw connection string.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> DatabaseInfo(int id)
        {
            ViewData["Title"] = "Tenant Database Info";

            var tenant = await _context.Tenants
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == id);

            if (tenant == null) return NotFound();

            // Resolver lookups (do NOT open a database connection — metadata only).
            var usesDedicated = await _databaseResolver.UsesDedicatedDatabaseAsync(id);
            var databaseName  = await _databaseResolver.GetDatabaseNameAsync(id);

            // Mask the resolved connection string for safe display.
            string maskedConnection;
            string resolved;
            try
            {
                resolved         = await _databaseResolver.GetConnectionStringAsync(id);
                maskedConnection = TenantDatabaseResolver.Mask(resolved);
            }
            catch
            {
                resolved         = string.Empty;
                maskedConnection = "(unavailable)";
            }

            var isProvisioned = tenant.DatabaseProvisionedAtUtc != null;

            // Live checks: only attempt a real connection for a provisioned dedicated DB.
            bool databaseExists  = !usesDedicated; // shared DB always "exists" from app's view
            bool connectionTest  = !usesDedicated; // shared is implicitly reachable
            bool connectionTested = false;

            if (usesDedicated && isProvisioned && !string.IsNullOrWhiteSpace(tenant.ConnectionString))
            {
                connectionTested = true;
                var (ok, _) = await TryOpenConnectionAsync(tenant.ConnectionString);
                databaseExists = ok;
                connectionTest = ok;
            }

            var runtimeDatabase = await _databaseResolver.GetRuntimeDatabaseAsync(id);

            var vm = new TenantDatabaseInfoVm
            {
                TenantId                 = tenant.Id,
                TenantName               = tenant.Name,
                DatabaseMode             = tenant.DatabaseMode,
                DatabaseName             = databaseName,
                DatabaseServer           = tenant.DatabaseServer,
                UsesDedicatedDatabase    = usesDedicated,
                MaskedConnectionString   = maskedConnection,
                LastDatabaseMigration    = tenant.LastDatabaseMigration,
                DatabaseProvisionedAtUtc = tenant.DatabaseProvisionedAtUtc,
                IsProvisioned            = isProvisioned,
                DatabaseExists           = databaseExists,
                ConnectionTested         = connectionTested,
                ConnectionTestPassed     = connectionTest,
                DataMigrated             = tenant.DataMigrated,
                DataMigratedAtUtc        = tenant.DataMigratedAtUtc,
                RoutingEnabled           = tenant.RoutingEnabled,
                RuntimeDatabase          = runtimeDatabase,
                ResolverStatus           = "Resolver ready",
                PhaseStatus              = "Phase 5.0D — Per-tenant runtime routing available (opt-in)"
            };

            return View(vm);
        }

        // ─────────────────────────────────────────────────────────────
        // HELPERS
        // ─────────────────────────────────────────────────────────────

        private string? GetClientIp() =>
            HttpContext?.Connection?.RemoteIpAddress?.ToString();

        /// <summary>
        /// Attempts to open a SQL connection. Returns (success, message).
        /// </summary>
        private static async Task<(bool ok, string message)> TryOpenConnectionAsync(string connectionString)
        {
            try
            {
                await using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();
                return (true, "Connection successful.");
            }
            catch (Exception ex)
            {
                return (false, $"Connection failed: {ex.Message}");
            }
        }

        private async Task LoadPlansDropdownAsync()
        {
            var plans = await _context.SubscriptionPlans
                .AsNoTracking()
                .Where(p => p.IsActive)
                .OrderBy(p => p.SortOrder)
                .ThenBy(p => p.MonthlyPrice)
                .ToListAsync();

            ViewBag.Plans = new SelectList(plans,
                nameof(SubscriptionPlan.Id), nameof(SubscriptionPlan.Name));

            // Full plan details for client-side auto-populate (JSON array)
            ViewBag.PlanDetailsJson = System.Text.Json.JsonSerializer.Serialize(
                plans.Select(p => new
                {
                    id           = p.Id,
                    name         = p.Name,
                    maxBranches  = p.MaxBranches,
                    maxUsers     = p.MaxUsers,
                    maxProducts  = p.MaxProducts,
                    monthlyPrice = p.MonthlyPrice
                }));
        }
    }

    // ── ViewModel for tenant user rows ────────────────────────────────
    public class TenantUserVm
    {
        public string Id                 { get; set; } = string.Empty;
        public string FullName           { get; set; } = string.Empty;
        public string Username           { get; set; } = string.Empty;
        public string Email              { get; set; } = string.Empty;
        public string Role               { get; set; } = string.Empty;
        public bool   IsActive           { get; set; }
        public bool   ForcePasswordChange { get; set; }
    }

    // ── ViewModel for the Phase 5.0B / 5.0C database diagnostic view ──
    public class TenantDatabaseInfoVm
    {
        public int                TenantId                 { get; set; }
        public string             TenantName               { get; set; } = string.Empty;
        public TenantDatabaseMode DatabaseMode             { get; set; }
        public string             DatabaseName             { get; set; } = string.Empty;
        public string?            DatabaseServer           { get; set; }
        public bool               UsesDedicatedDatabase    { get; set; }
        public string             MaskedConnectionString   { get; set; } = "(none)";
        public string?            LastDatabaseMigration    { get; set; }
        public DateTime?          DatabaseProvisionedAtUtc { get; set; }
        public bool               IsProvisioned            { get; set; }
        public bool               DatabaseExists           { get; set; }
        public bool               ConnectionTested         { get; set; }
        public bool               ConnectionTestPassed     { get; set; }
        public bool               DataMigrated             { get; set; }
        public DateTime?          DataMigratedAtUtc        { get; set; }
        public bool               RoutingEnabled           { get; set; }
        public string             RuntimeDatabase          { get; set; } = "Shared";
        public string             ResolverStatus           { get; set; } = string.Empty;
        public string             PhaseStatus              { get; set; } = string.Empty;

        public string CurrentRouting => UsesDedicatedDatabase ? "Dedicated" : "Shared";

        // Phase 5.0D.1 — the actual EF context business requests resolve to at runtime.
        public bool   RoutingActive  =>
            string.Equals(RuntimeDatabase, "Dedicated", StringComparison.OrdinalIgnoreCase);
        public string CurrentContext => RoutingActive ? "TenantDbContext" : "ApplicationDbContext";
    }
}

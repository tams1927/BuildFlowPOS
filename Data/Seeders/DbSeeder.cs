using HardwareManagementSystem.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HardwareManagementSystem.Data.Seeders
{
    public static class DbSeeder
    {
        // ─────────────────────────────────────────────────────────────
        // ROLES + SUPERADMIN ACCOUNT
        // Creates all required roles and the single platform SuperAdmin.
        // No tenant-specific users are seeded here.
        // ─────────────────────────────────────────────────────────────

        public static async Task SeedAdminAsync(IServiceProvider serviceProvider)
        {
            var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var config = serviceProvider.GetRequiredService<IConfiguration>();
            var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DbSeeder");

            // ── Operational roles (no default accounts) ──────────────
            // Tenant admins create user accounts through the Users page.
            // "Admin" is intentionally removed — TenantAdmin is the single
            // tenant-level admin role.
            var operationalRoles = new[]
            {
                "TenantAdmin",
                "Cashier",
                "BranchManager",
                "InventoryStaff"
            };

            foreach (var roleName in operationalRoles)
            {
                if (!await roleManager.RoleExistsAsync(roleName))
                    await roleManager.CreateAsync(new IdentityRole(roleName));
            }

            // ── Platform-only SuperAdmin role + account ───────────────
            if (!await roleManager.RoleExistsAsync("SuperAdmin"))
                await roleManager.CreateAsync(new IdentityRole("SuperAdmin"));

            const string superAdminUsername = "superadmin";
            const string superAdminEmail    = "superadmin@hardbuild.local";
            const string defaultPassword    = "SuperAdmin123!";

            var configuredPassword = config["SeedSettings:InitialSuperAdminPassword"];
            var usingDefaultPassword = string.IsNullOrWhiteSpace(configuredPassword);
            var superAdminPassword = usingDefaultPassword ? defaultPassword : configuredPassword!;

            if (usingDefaultPassword)
            {
                logger.LogWarning(
                    "SeedSettings:InitialSuperAdminPassword is not configured. " +
                    "The SuperAdmin account uses the built-in default password. " +
                    "Set a strong password in configuration before go-live.");
            }

            var existingSuperAdmin = await userManager.FindByNameAsync(superAdminUsername);

            if (existingSuperAdmin == null)
            {
                var superAdmin = new ApplicationUser
                {
                    UserName             = superAdminUsername,
                    Email                = superAdminEmail,
                    FullName             = "Super Administrator",
                    EmailConfirmed       = true,
                    IsActive             = true,
                    TenantId             = null,
                    ForcePasswordChange  = true
                };

                var result = await userManager.CreateAsync(superAdmin, superAdminPassword);

                if (result.Succeeded)
                    await userManager.AddToRoleAsync(superAdmin, "SuperAdmin");
            }
            else if (existingSuperAdmin.ForcePasswordChange == false && usingDefaultPassword)
            {
                logger.LogWarning(
                    "SuperAdmin account exists and SeedSettings:InitialSuperAdminPassword is not set. " +
                    "Rotate the SuperAdmin password and enable ForcePasswordChange before go-live.");
            }
        }

        // ─────────────────────────────────────────────────────────────
        // ROLE PERMISSIONS
        // Seeds full permission rows for Admin, SuperAdmin, and TenantAdmin.
        //
        // SuperAdmin  : full access to all modules
        // Admin       : full access to all modules
        // TenantAdmin : full access to all tenant-operational modules;
        //               SaaS-only modules (Tenants, SubscriptionPlans,
        //               SuperAdmin) are seeded with all false — but those
        //               controllers are additionally protected by
        //               [Authorize(Roles = "SuperAdmin")] so TenantAdmin
        //               cannot reach them regardless.
        //
        // This seeder is idempotent: it only adds missing rows and never
        // modifies existing ones.
        // ─────────────────────────────────────────────────────────────

        // SaaS-platform-only modules. TenantAdmin gets CanView=false for
        // these; all other tenant modules get full access.
        private static readonly HashSet<string> _saasOnlyModules = new(StringComparer.OrdinalIgnoreCase)
        {
            "Tenants",
            "SubscriptionPlans",
            "SuperAdmin"
        };

        public static async Task SeedRolePermissionsAsync(IServiceProvider services)
        {
            var context     = services.GetRequiredService<ApplicationDbContext>();
            var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();

            // "Admin" is removed; SuperAdmin, TenantAdmin, and BranchManager are the seeded roles.
            var requiredRoles = new[] { "SuperAdmin", "TenantAdmin", "BranchManager", "Cashier", "InventoryStaff" };

            foreach (var roleName in requiredRoles)
            {
                if (!await roleManager.RoleExistsAsync(roleName))
                    await roleManager.CreateAsync(new IdentityRole(roleName));
            }

            var modules = GetAllModules();

            var existingPermissions = await context.RolePermissions
                .Select(p => p.RoleName + "|" + p.ModuleName)
                .ToListAsync();

            var permissionsToAdd = new List<RolePermission>();

            foreach (var role in requiredRoles)
            {
                foreach (var module in modules)
                {
                    var key = role + "|" + module;
                    if (existingPermissions.Contains(key))
                        continue;

                    // Lock SaaS-only modules for non-SuperAdmin roles
                    bool isSaasModule  = _saasOnlyModules.Contains(module);
                    bool isSuperAdmin  = role == "SuperAdmin";
                    bool isBranchMgr   = role == "BranchManager";

                    // SaaS modules: SuperAdmin only
                    if (isSaasModule && !isSuperAdmin)
                    {
                        permissionsToAdd.Add(new RolePermission
                        {
                            RoleName   = role,
                            ModuleName = module,
                            CanView    = false,
                            CanCreate  = false,
                            CanEdit    = false,
                            CanDelete  = false,
                            CanPrint   = false,
                            CanExport  = false
                        });
                        continue;
                    }

                    // BranchManager: operational modules — View/Create/Edit/Print/Export but no Delete
                    // on sensitive management modules (Users, Branches, Tenants, Settings)
                    var branchMgrNoDeleteModules = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        "Users", "Branches", "Settings", "Roles", "RolePermissions"
                    };

                    bool canDelete = isBranchMgr && branchMgrNoDeleteModules.Contains(module)
                        ? false
                        : true;

                    bool grantAccess = true;

                    permissionsToAdd.Add(new RolePermission
                    {
                        RoleName   = role,
                        ModuleName = module,
                        CanView    = grantAccess,
                        CanCreate  = grantAccess,
                        CanEdit    = grantAccess,
                        CanDelete  = isBranchMgr ? canDelete : grantAccess,
                        CanPrint   = grantAccess,
                        CanExport  = grantAccess
                    });
                }
            }

            var pilotPerms = BuildPilotRolePermissions(existingPermissions, modules);
            foreach (var p in pilotPerms)
                existingPermissions.Add(p.RoleName + "|" + p.ModuleName);
            permissionsToAdd.AddRange(pilotPerms);

            if (permissionsToAdd.Any())
            {
                await context.RolePermissions.AddRangeAsync(permissionsToAdd);
                await context.SaveChangesAsync();
            }
        }

        /// <summary>Default permissions for pilot roles not covered by the admin seed loop.</summary>
        private static List<RolePermission> BuildPilotRolePermissions(
            List<string> existingKeys,
            List<string> modules)
        {
            var result = new List<RolePermission>();

            var cashierAccess = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "POS", "Sales", "Customers", "SalesReturn"
            };

            var inventoryAccess = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Products", "Inventory", "StockIn", "StockAdjustment", "DamagedGoods", "SupplierReturns",
                "PurchaseOrders",
                "Suppliers", "Categories", "Units", "Import", "InventoryMovement",
                "BranchTransfers", "InventoryValuation", "ReorderSuggestions", "StockAging",
                "FastMovingItems", "SlowMovingItems", "DeadStock", "ABCAnalysis"
            };

            foreach (var role in new[] { "Cashier", "InventoryStaff" })
            {
                var allowed = role == "Cashier" ? cashierAccess : inventoryAccess;

                foreach (var module in modules)
                {
                    var key = role + "|" + module;
                    if (existingKeys.Contains(key))
                        continue;

                    if (_saasOnlyModules.Contains(module))
                    {
                        result.Add(DenyAll(role, module));
                        continue;
                    }

                    if (!allowed.Contains(module))
                    {
                        result.Add(DenyAll(role, module));
                        continue;
                    }

                    if (role == "Cashier")
                    {
                        var viewOnly = module.Equals("Customers", StringComparison.OrdinalIgnoreCase)
                            || module.Equals("Sales", StringComparison.OrdinalIgnoreCase);
                        result.Add(new RolePermission
                        {
                            RoleName   = role,
                            ModuleName = module,
                            CanView    = true,
                            CanCreate  = !viewOnly,
                            CanEdit    = false,
                            CanDelete  = false,
                            CanPrint   = true,
                            CanExport  = false
                        });
                    }
                    else
                    {
                        var reportsOnly = module is "InventoryValuation" or "ReorderSuggestions"
                            or "StockAging" or "FastMovingItems" or "SlowMovingItems"
                            or "DeadStock" or "ABCAnalysis";
                        result.Add(new RolePermission
                        {
                            RoleName   = role,
                            ModuleName = module,
                            CanView    = true,
                            CanCreate  = !reportsOnly,
                            CanEdit    = !reportsOnly,
                            CanDelete  = false,
                            CanPrint   = true,
                            CanExport  = reportsOnly
                        });
                    }
                }
            }

            return result;
        }

        private static RolePermission DenyAll(string role, string module) => new()
        {
            RoleName   = role,
            ModuleName = module,
            CanView    = false,
            CanCreate  = false,
            CanEdit    = false,
            CanDelete  = false,
            CanPrint   = false,
            CanExport  = false
        };

        // SeedDefaultTenantAsync intentionally removed.
        // The SaaS model requires tenants to be created explicitly by SuperAdmin.
        // No default tenant is auto-provisioned on startup.

        // ─────────────────────────────────────────────────────────────
        // HELPERS
        // ─────────────────────────────────────────────────────────────

        private static List<string> GetAllModules()
        {
            var excludedFromNav = new HashSet<string>
            {
                "Account",
                "Notifications",
                "Home"
            };

            var discovered = typeof(Program).Assembly
                .GetTypes()
                .Where(t =>
                    typeof(Microsoft.AspNetCore.Mvc.Controller).IsAssignableFrom(t) &&
                    !t.IsAbstract &&
                    t.Name.EndsWith("Controller"))
                .Select(t => t.Name.Replace("Controller", ""))
                .Where(name => !excludedFromNav.Contains(name))
                .ToHashSet();

            // Ensure SaaS modules are always present
            discovered.Add("SuperAdmin");
            discovered.Add("Tenants");
            discovered.Add("SubscriptionPlans");
            discovered.Add("Quotations");
            discovered.Add("Dashboard");

            // AR/AP permission modules (not tied to a dedicated controller)
            discovered.Add("CustomerStatements");
            discovered.Add("SupplierStatements");
            discovered.Add("CustomerAging");
            discovered.Add("SupplierAging");

            // Phase 4.6 — Inventory Intelligence modules
            discovered.Add("FastMovingItems");
            discovered.Add("SlowMovingItems");
            discovered.Add("DeadStock");
            discovered.Add("ReorderSuggestions");
            discovered.Add("StockAging");
            discovered.Add("InventoryValuation");
            discovered.Add("ABCAnalysis");

            return discovered.OrderBy(n => n).ToList();
        }

        /// <summary>Legacy helper kept for backward compatibility.</summary>
        private static List<string> GetControllerModules()
        {
            var excluded = new[] { "Account", "Notifications" };

            return typeof(Program).Assembly
                .GetTypes()
                .Where(t =>
                    typeof(Microsoft.AspNetCore.Mvc.Controller).IsAssignableFrom(t) &&
                    !t.IsAbstract &&
                    t.Name.EndsWith("Controller"))
                .Select(t => t.Name.Replace("Controller", ""))
                .Where(name => !excluded.Contains(name))
                .OrderBy(name => name)
                .ToList();
        }
    }
}

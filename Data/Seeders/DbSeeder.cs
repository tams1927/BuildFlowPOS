using HardwareManagementSystem.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HardwareManagementSystem.Data.Seeders
{
    public static class DbSeeder
    {
        public static async Task SeedAdminAsync(IServiceProvider serviceProvider)
        {
            var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            string[] roles =
            {
                "Admin",
                "Cashier",
                "InventoryStaff"
            };

            foreach (var role in roles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                {
                    await roleManager.CreateAsync(new IdentityRole(role));
                }
            }

            var adminUsername = "admin";
            var adminEmail = "admin@hardwarepos.local";
            var adminPassword = "admin123";

            var existingAdmin = await userManager.FindByNameAsync(adminUsername);

            if (existingAdmin == null)
            {
                var admin = new ApplicationUser
                {
                    UserName = adminUsername,
                    Email = adminEmail,
                    FullName = "System Administrator",
                    EmailConfirmed = true,
                    IsActive = true
                };

                var result = await userManager.CreateAsync(admin, adminPassword);

                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(admin, "Admin");
                }
            }
        }

        public static async Task SeedRolePermissionsAsync(IServiceProvider services)
        {
            var context = services.GetRequiredService<ApplicationDbContext>();

            var existing = await context.RolePermissions
                .Select(p => p.RoleName + "|" + p.ModuleName)
                .ToListAsync();

            var permissions = new List<RolePermission>();

            var modules = new[]
            {
                "Dashboard",
                "POS",
                "Sales",
                "Customers",
                "Inventory",
                "StockIn",
                "Suppliers",
                "Categories",
                "Units",
                "Reports",
                "Users",
                "Settings",
                "StockAdjustment",
                "SalesReturn"
            };

            // ADMIN

            foreach (var module in modules)
            {
                permissions.Add(new RolePermission
                {
                    RoleName = "Admin",
                    ModuleName = module,
                    CanView = true,
                    CanCreate = true,
                    CanEdit = true,
                    CanDelete = true,
                    CanPrint = true,
                    CanExport = true
                });
            }

            // CASHIER

            var cashierModules = new[]
            {
                "Dashboard",
                "POS",
                "Sales",
                "Customers"
            };

            foreach (var module in cashierModules)
            {
                permissions.Add(new RolePermission
                {
                    RoleName = "Cashier",
                    ModuleName = module,
                    CanView = true,
                    CanCreate = true,
                    CanEdit = true,
                    CanDelete = false,
                    CanPrint = true,
                    CanExport = false
                });
            }

            // INVENTORY STAFF

            var inventoryModules = new[]
            {
                "Dashboard",
                "Inventory",
                "StockIn",
                "Suppliers",
                "Categories",
                "Units",
                "StockAdjustment"
            };

            foreach (var module in inventoryModules)
            {
                permissions.Add(new RolePermission
                {
                    RoleName = "InventoryStaff",
                    ModuleName = module,
                    CanView = true,
                    CanCreate = true,
                    CanEdit = true,
                    CanDelete = false,
                    CanPrint = false,
                    CanExport = false
                });
            }

            var toAdd = permissions
                .Where(p => !existing.Contains(p.RoleName + "|" + p.ModuleName))
                .ToList();

            if (toAdd.Count > 0)
            {
                await context.RolePermissions.AddRangeAsync(toAdd);
                await context.SaveChangesAsync();
            }
        }
    }
}
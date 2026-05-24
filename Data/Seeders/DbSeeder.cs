using HardwareManagementSystem.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HardwareManagementSystem.Data.Seeders
{
    public static class DbSeeder
    {
        public static async Task SeedAdminAsync(IServiceProvider serviceProvider)
        {
            var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            if (!await roleManager.RoleExistsAsync("Admin"))
            {
                await roleManager.CreateAsync(new IdentityRole("Admin"));
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
            var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();

            var roles = await roleManager.Roles
                .Select(r => r.Name!)
                .ToListAsync();

            if (!roles.Contains("Admin"))
            {
                await roleManager.CreateAsync(new IdentityRole("Admin"));
                roles.Add("Admin");
            }

            var modules = GetControllerModules();

            var existingPermissions = await context.RolePermissions
                .Select(p => p.RoleName + "|" + p.ModuleName)
                .ToListAsync();

            var permissionsToAdd = new List<RolePermission>();

            foreach (var role in roles)
            {
                foreach (var module in modules)
                {
                    var key = role + "|" + module;

                    if (existingPermissions.Contains(key))
                    {
                        continue;
                    }

                    permissionsToAdd.Add(new RolePermission
                    {
                        RoleName = role,
                        ModuleName = module,

                        CanView = role == "Admin",
                        CanCreate = role == "Admin",
                        CanEdit = role == "Admin",
                        CanDelete = role == "Admin",
                        CanPrint = role == "Admin",
                        CanExport = role == "Admin"
                    });
                }
            }

            if (permissionsToAdd.Any())
            {
                await context.RolePermissions.AddRangeAsync(permissionsToAdd);
                await context.SaveChangesAsync();
            }
        }

        private static List<string> GetControllerModules()
        {
            var excludedControllers = new[]
            {
                "Account",
                "Notifications"
            };

            return typeof(Program).Assembly
                .GetTypes()
                .Where(t =>
                    typeof(Controller).IsAssignableFrom(t) &&
                    !t.IsAbstract &&
                    t.Name.EndsWith("Controller"))
                .Select(t => t.Name.Replace("Controller", ""))
                .Where(name => !excludedControllers.Contains(name))
                .OrderBy(name => name)
                .ToList();
        }
    }
}
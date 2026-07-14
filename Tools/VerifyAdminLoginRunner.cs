using HardwareManagementSystem.Data;
using HardwareManagementSystem.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HardwareManagementSystem.Tools
{
    /// <summary>
    /// Dev-only: verify tenant admin password from TENANT_ADMIN_PASSWORD env.
    ///   set TENANT_ADMIN_PASSWORD=&lt;secret&gt;
    ///   dotnet run -- --verify-admin-login --user=adminTCS
    /// </summary>
    public static class VerifyAdminLoginRunner
    {
        public static async Task RunAsync(WebApplication app, string[] args)
        {
            var user = GetArg(args, "--user");
            var pass = Environment.GetEnvironmentVariable("TENANT_ADMIN_PASSWORD");
            if (user == null || string.IsNullOrWhiteSpace(pass))
            {
                Console.WriteLine("Usage: set TENANT_ADMIN_PASSWORD then --verify-admin-login --user=NAME");
                Environment.ExitCode = 1;
                return;
            }

            using var scope = app.Services.CreateScope();
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var u = await um.FindByNameAsync(user);
            if (u == null) { Console.WriteLine("FAIL: user not found"); Environment.ExitCode = 1; return; }
            var ok = await um.CheckPasswordAsync(u, pass);
            Console.WriteLine(ok ? "PASS: password valid" : "FAIL: password invalid");
            Environment.ExitCode = ok ? 0 : 1;
        }

        private static string? GetArg(string[] args, string key) =>
            args.FirstOrDefault(a => a.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))?.Split('=', 2)[1];
    }
}

using HardwareManagementSystem.Models;
using Microsoft.AspNetCore.Identity;

namespace HardwareManagementSystem.Tools
{
    /// <summary>
    /// Dev-only password rotation from environment (never CLI args).
    ///   set SUPERADMIN_PASSWORD=&lt;secret&gt;
    ///   dotnet run -- --set-dev-password --user=superadmin
    /// </summary>
    public static class SetDevUserPasswordRunner
    {
        public static async Task RunAsync(WebApplication app, string[] args)
        {
            if (!app.Environment.IsDevelopment())
            {
                Console.WriteLine("[SetDevPassword] Refused — not Development.");
                Environment.ExitCode = 1;
                return;
            }

            string? user = null;
            foreach (var a in args)
                if (a.StartsWith("--user=", StringComparison.OrdinalIgnoreCase))
                    user = a.Split('=')[1];

            if (string.IsNullOrWhiteSpace(user))
            {
                Console.WriteLine("[SetDevPassword] Required: --user=USERNAME");
                Environment.ExitCode = 1;
                return;
            }

            var envKey = user.Equals("superadmin", StringComparison.OrdinalIgnoreCase)
                ? "SUPERADMIN_PASSWORD"
                : "DEV_USER_PASSWORD";
            var password = Environment.GetEnvironmentVariable(envKey);
            if (string.IsNullOrWhiteSpace(password))
            {
                Console.WriteLine($"[SetDevPassword] Set {envKey} environment variable.");
                Environment.ExitCode = 1;
                return;
            }

            using var scope = app.Services.CreateScope();
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var u = await um.FindByNameAsync(user);
            if (u == null)
            {
                Console.WriteLine($"[SetDevPassword] User '{user}' not found.");
                Environment.ExitCode = 1;
                return;
            }

            var token = await um.GeneratePasswordResetTokenAsync(u);
            var result = await um.ResetPasswordAsync(u, token, password);
            if (!result.Succeeded)
            {
                Console.WriteLine("[SetDevPassword] " + string.Join(", ", result.Errors.Select(e => e.Description)));
                Environment.ExitCode = 1;
                return;
            }

            Console.WriteLine($"[SetDevPassword] Password rotated for '{user}'.");
            Environment.ExitCode = 0;
        }
    }
}

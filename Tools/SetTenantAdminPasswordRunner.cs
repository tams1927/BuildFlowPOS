using HardwareManagementSystem.Data.Seeders;

namespace HardwareManagementSystem.Tools
{
    /// <summary>
    /// Dev-only: rotate retained tenant admin password from environment (never from CLI args).
    ///   set TENANT_ADMIN_PASSWORD=&lt;secret&gt;
    ///   dotnet run -- --set-tenant-admin-password --tenant-id=1 --user=adminTCS
    /// </summary>
    public static class SetTenantAdminPasswordRunner
    {
        public static async Task RunAsync(WebApplication app, string[] args)
        {
            if (!app.Environment.IsDevelopment())
            {
                Console.WriteLine("[SetTenantAdminPassword] Refused — not Development.");
                Environment.ExitCode = 1;
                return;
            }

            var password = Environment.GetEnvironmentVariable("TENANT_ADMIN_PASSWORD");
            if (string.IsNullOrWhiteSpace(password))
            {
                Console.WriteLine("[SetTenantAdminPassword] Set TENANT_ADMIN_PASSWORD environment variable.");
                Environment.ExitCode = 1;
                return;
            }

            int? tenantId = null;
            string? user = null;
            foreach (var a in args)
            {
                if (a.StartsWith("--tenant-id=", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(a.Split('=')[1], out var id))
                    tenantId = id;
                else if (a.StartsWith("--user=", StringComparison.OrdinalIgnoreCase))
                    user = a.Split('=')[1];
            }

            if (!tenantId.HasValue || string.IsNullOrWhiteSpace(user))
            {
                Console.WriteLine("[SetTenantAdminPassword] Required: --tenant-id=N --user=USERNAME");
                Environment.ExitCode = 1;
                return;
            }

            using var scope = app.Services.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<TenantOperationalResetService>();
            try
            {
                await svc.SetRetainedAdminPasswordAsync(tenantId.Value, user, password);
                Console.WriteLine($"[SetTenantAdminPassword] Password rotated for tenant {tenantId} user '{user}'.");
                Environment.ExitCode = 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SetTenantAdminPassword] ERROR: {ex.Message}");
                Environment.ExitCode = 1;
            }
        }
    }
}

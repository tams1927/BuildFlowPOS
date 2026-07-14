using HardwareManagementSystem.Data.Seeders;
using System.Text.Json;

namespace HardwareManagementSystem.Tools
{
    /// <summary>
    /// Dev/QA tenant operational reset. Preview by default.
    ///   dotnet run -- --tenant-reset --tenant-id=1
    ///   dotnet run -- --tenant-reset --tenant-id=1 --retain-admin=owner --confirm
    /// </summary>
    public static class TenantCleanResetRunner
    {
        public static async Task RunAsync(WebApplication app, string[] args)
        {
            if (!app.Environment.IsDevelopment())
            {
                Console.WriteLine("[TenantReset] Refused — not Development.");
                Environment.ExitCode = 1;
                return;
            }

            var options = ParseArgs(args);
            if (!options.TenantId.HasValue && string.IsNullOrWhiteSpace(options.TenantCode))
            {
                Console.WriteLine("[TenantReset] Required: --tenant-id=N or --tenant-code=CODE");
                Environment.ExitCode = 1;
                return;
            }

            using var scope = app.Services.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<TenantOperationalResetService>();

            TenantResetReport report;
            try
            {
                report = options.Confirm
                    ? await svc.ExecuteAsync(options)
                    : await svc.PreviewAsync(options);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TenantReset] ERROR: {ex.Message}");
                Environment.ExitCode = 1;
                return;
            }

            Console.WriteLine(report.ToJson());

            var outPath = Path.Combine("docs", "TenantCleanResetAudit.json");
            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
            await File.WriteAllTextAsync(outPath, report.ToJson());

            Console.WriteLine($"[TenantReset] Report written to {outPath}");
            if (!options.Confirm)
                Console.WriteLine("[TenantReset] Preview only. Re-run with --confirm to execute.");

            Environment.ExitCode = report.Success || report.DryRun ? 0 : 1;
        }

        private static TenantResetOptions ParseArgs(string[] args)
        {
            int? tenantId = null;
            string? code = null;
            string? retain = null;
            var confirm = args.Contains("--confirm");

            foreach (var a in args)
            {
                if (a.StartsWith("--tenant-id=", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(a.Split('=')[1], out var id))
                    tenantId = id;
                else if (a.StartsWith("--tenant-code=", StringComparison.OrdinalIgnoreCase))
                    code = a.Split('=')[1];
                else if (a.StartsWith("--retain-admin=", StringComparison.OrdinalIgnoreCase))
                    retain = a.Split('=')[1];
            }

            return new TenantResetOptions
            {
                TenantId = tenantId,
                TenantCode = code,
                RetainAdminUsername = retain,
                Confirm = confirm
            };
        }
    }
}

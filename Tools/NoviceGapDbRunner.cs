using HardwareManagementSystem.Data;
using HardwareManagementSystem.Services.TenantDatabases;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace HardwareManagementSystem.Tools
{
    /// <summary>
    /// Row-count snapshot for novice simulation integrity checks.
    ///   dotnet run -- --qa-novice-db-counts --tenant-id=1
    /// Writes docs/manuals/_assets/novice_db_counts.json
    /// </summary>
    public static class NoviceGapDbRunner
    {
        public static async Task RunAsync(WebApplication app, string[] args)
        {
            if (!app.Environment.IsDevelopment())
            {
                Console.WriteLine("[NOVICE-DB] Development-only.");
                Environment.ExitCode = 1;
                return;
            }

            var tenantId = 1;
            foreach (var a in args)
            {
                if (a.StartsWith("--tenant-id=", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(a.Split('=')[1], out var id))
                    tenantId = id;
            }

            using var scope = app.Services.CreateScope();
            var sp = scope.ServiceProvider;
            var ctxProvider = sp.GetRequiredService<ITenantOperationalContextProvider>();
            var resolver = sp.GetRequiredService<ITenantDatabaseResolver>();
            var ctx = await ctxProvider.GetContextAsync(tenantId);
            var dedicated = await resolver.IsRoutingActiveAsync(tenantId) && ctx is TenantDbContext;

            async Task<int> C<T>(DbSet<T> set) where T : class
            {
                if (dedicated)
                    return await set.CountAsync();
                return await set.CountAsync(e => EF.Property<int?>(e, "TenantId") == tenantId);
            }

            var counts = new Dictionary<string, int>
            {
                ["SalesHeaders"] = await C(ctx.SalesHeaders),
                ["SalesDetails"] = await C(ctx.SalesDetails),
                ["CustomerLedgers"] = await C(ctx.CustomerLedgers),
                ["SupplierPayments"] = await C(ctx.SupplierPayments),
                ["PurchaseOrders"] = await C(ctx.PurchaseOrders),
                ["PurchaseOrderItems"] = await C(ctx.PurchaseOrderItems),
                ["StockInHeaders"] = await C(ctx.StockInHeaders),
                ["StockInDetails"] = await C(ctx.StockInDetails),
                ["StockAdjustmentHeaders"] = await C(ctx.StockAdjustmentHeaders),
                ["StockAdjustmentDetails"] = await C(ctx.StockAdjustmentDetails),
                ["BranchProductStocks"] = await C(ctx.BranchProductStocks),
                ["AuditTrails"] = await C(ctx.AuditTrails),
                ["Units"] = await C(ctx.Units),
                ["Suppliers"] = await C(ctx.Suppliers),
            };

            var payload = new
            {
                tenantId,
                timestampUtc = DateTime.UtcNow.ToString("o"),
                database = ctx.Database.GetDbConnection().Database,
                counts
            };

            var outPath = Path.Combine("docs", "manuals", "_assets", "novice_db_counts.json");
            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
            await File.WriteAllTextAsync(outPath, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));

            Console.WriteLine(JsonSerializer.Serialize(payload));
        }
    }
}

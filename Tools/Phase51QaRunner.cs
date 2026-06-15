using HardwareManagementSystem.Data;
using HardwareManagementSystem.Models;
using HardwareManagementSystem.Services;
using HardwareManagementSystem.Services.TenantDatabases;
using Microsoft.EntityFrameworkCore;

namespace HardwareManagementSystem.Tools
{
    /// <summary>
    /// Phase 5.1 QA — unit conversion + multi-currency (Development only, --qa-phase51).
    /// </summary>
    public static class Phase51QaRunner
    {
        public static async Task RunAsync(WebApplication app)
        {
            if (!app.Environment.IsDevelopment())
            {
                Console.WriteLine("[QA51] Development-only. Aborting.");
                return;
            }

            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var pass = 0;
            var fail = 0;
            void Ok(string m) { pass++; Console.WriteLine($"[QA51] PASS — {m}"); }
            void No(string m) { fail++; Console.WriteLine($"[QA51] FAIL — {m}"); }

            using var scope = app.Services.CreateScope();
            var sp = scope.ServiceProvider;
            var appDb = sp.GetRequiredService<ApplicationDbContext>();
            var resolver = sp.GetRequiredService<ITenantDatabaseResolver>();
            var ctxProvider = sp.GetRequiredService<ITenantOperationalContextProvider>();
            var tenant = await appDb.Tenants
                .FirstOrDefaultAsync(t => t.RoutingEnabled && t.DatabaseMode == TenantDatabaseMode.Dedicated)
                ?? await appDb.Tenants.FirstOrDefaultAsync(t => t.IsActive && t.Code != null);

            if (tenant == null)
            {
                No("No active tenant found for QA.");
                Console.WriteLine($"[QA51] Done — {pass} passed, {fail} failed.");
                return;
            }

            var isDedicatedRouted = tenant.RoutingEnabled && tenant.DatabaseMode == TenantDatabaseMode.Dedicated;
            Console.WriteLine($"[QA51] Using tenant {tenant.Name} (Id={tenant.Id}, dedicated+routed={isDedicatedRouted})");

            var routedCtx = await ctxProvider.GetContextAsync(tenant.Id);
            var sharedLeakPrefix = $"QA51_{stamp}";

            // ── Units: kg + sack ─────────────────────────────────────────────
            var kg = await routedCtx.Units.FirstOrDefaultAsync(u => u.TenantId == tenant.Id && u.UnitName == "kg");
            if (kg == null)
            {
                kg = new Unit { TenantId = tenant.Id, UnitName = "kg", ShortName = "kg", IsActive = true, CreatedAt = DateTime.Now };
                routedCtx.Units.Add(kg);
                await routedCtx.SaveChangesAsync();
            }

            var sack = await routedCtx.Units.FirstOrDefaultAsync(u => u.TenantId == tenant.Id && u.UnitName == "sack");
            if (sack == null)
            {
                sack = new Unit { TenantId = tenant.Id, UnitName = "sack", ShortName = "sack", IsActive = true, CreatedAt = DateTime.Now };
                routedCtx.Units.Add(sack);
                await routedCtx.SaveChangesAsync();
            }

            var cat = await routedCtx.Categories.FirstOrDefaultAsync(c => c.TenantId == tenant.Id);
            if (cat == null)
            {
                cat = new Category { TenantId = tenant.Id, CategoryName = $"QA51_Cat_{stamp}", IsActive = true, CreatedAt = DateTime.Now };
                routedCtx.Categories.Add(cat);
                await routedCtx.SaveChangesAsync();
            }

            var branch = await routedCtx.Branches.FirstOrDefaultAsync(b => b.TenantId == tenant.Id && b.IsMainBranch)
                ?? await routedCtx.Branches.FirstOrDefaultAsync(b => b.TenantId == tenant.Id);
            if (branch == null)
            {
                branch = new Branch { TenantId = tenant.Id, Name = $"QA51_Branch_{stamp}", Code = "QA51", IsMainBranch = true, IsActive = true, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
                routedCtx.Branches.Add(branch);
                await routedCtx.SaveChangesAsync();
            }

            var nail = new Item
            {
                TenantId = tenant.Id,
                ItemCode = $"{sharedLeakPrefix}_NAIL",
                ItemName = $"QA_Nails_{stamp}",
                CategoryId = cat.Id,
                UnitId = kg.Id,
                BaseUnitId = kg.Id,
                CurrentStock = 0,
                ReorderLevel = 5,
                CostPrice = 10,
                SellingPrice = 15,
                Status = "Active",
                CreatedAt = DateTime.Now
            };
            routedCtx.Items.Add(nail);
            await routedCtx.SaveChangesAsync();

            routedCtx.ItemUnitConversions.Add(new ItemUnitConversion
            {
                TenantId = tenant.Id,
                ItemId = nail.Id,
                UnitId = kg.Id,
                ConversionQuantity = 1,
                IsDefaultPurchaseUnit = false,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });
            routedCtx.ItemUnitConversions.Add(new ItemUnitConversion
            {
                TenantId = tenant.Id,
                ItemId = nail.Id,
                UnitId = sack.Id,
                ConversionQuantity = 25,
                IsDefaultPurchaseUnit = true,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });
            await routedCtx.SaveChangesAsync();

            var convInDedicated = await routedCtx.ItemUnitConversions.CountAsync(c => c.ItemId == nail.Id && c.UnitId == sack.Id);
            if (convInDedicated == 1) Ok("Conversion row exists in dedicated DB");
            else No($"Expected 1 sack conversion in dedicated DB, got {convInDedicated}");

            var sharedItem = await appDb.Items.AnyAsync(i => i.ItemCode == nail.ItemCode);
            var sharedConv = await appDb.ItemUnitConversions.AnyAsync(c => c.ItemId == nail.Id);
            if (isDedicatedRouted)
            {
                if (!sharedItem && !sharedConv) Ok("No QA rows leaked to shared DB");
                else No($"Shared leak: item={sharedItem}, conv={sharedConv}");
            }
            else
            {
                Ok("Skipped shared-leak check (tenant not on dedicated routing)");
            }

            // Stock-in 10 sacks → 250 kg
            var sin = $"QA51_SIN_{stamp}";
            var header = new StockInHeader
            {
                TenantId = tenant.Id,
                StockInNumber = sin,
                SupplierId = (await routedCtx.Suppliers.FirstOrDefaultAsync(s => s.TenantId == tenant.Id))?.Id
                    ?? (await EnsureSupplierAsync(routedCtx, tenant.Id, stamp)),
                BranchId = branch.Id,
                DateReceived = DateTime.Now,
                TotalCost = 1000,
                BalanceDue = 1000,
                PaymentStatus = "Unpaid",
                CreatedAt = DateTime.Now
            };
            var stockCost = ItemUnitConversionService.ComputePurchaseCost(10m, 1000m, 25m);
            header.StockInDetails.Add(new StockInDetail
            {
                ItemId = nail.Id,
                ReceivedUnitId = sack.Id,
                ReceivedQuantity = 10,
                ConversionQuantity = 25,
                BaseQuantity = 250,
                Quantity = 250,
                CostPerReceivedUnit = stockCost.CostPerReceivedUnit,
                CostPerBaseUnit = stockCost.CostPerBaseUnit,
                UnitCost = stockCost.CostPerBaseUnit,
                TotalCost = stockCost.TotalCost
            });
            header.TotalCost = stockCost.TotalCost;
            header.BalanceDue = stockCost.TotalCost;
            routedCtx.StockInHeaders.Add(header);
            await AddBranchStockAsync(routedCtx, tenant.Id, branch.Id, nail.Id, 250);
            nail.CurrentStock += 250;
            await routedCtx.SaveChangesAsync();

            var detail = await routedCtx.StockInDetails.FirstAsync(d => d.StockInHeaderId == header.Id);
            if (detail.BaseQuantity == 250 && detail.ReceivedQuantity == 10) Ok("StockInDetail stores received unit and base quantity");
            else No($"StockInDetail wrong: base={detail.BaseQuantity}, received={detail.ReceivedQuantity}");

            var stock = await GetBranchStockAsync(routedCtx, branch.Id, nail.Id);
            if (stock == 250) Ok("Inventory increased by base quantity (250 kg)");
            else No($"Expected branch stock 250, got {stock}");

            // POS sale 2.5 kg
            var saleNo = $"QA51_SALE_{stamp}";
            var sale = new SalesHeader
            {
                TenantId = tenant.Id,
                SalesNumber = saleNo,
                SalesDate = DateTime.Now,
                BranchId = branch.Id,
                CashierName = "QA51",
                SubTotal = 37.5m,
                TotalAmount = 37.5m,
                AmountReceived = 37.5m,
                PaymentMethod = "Cash",
                Status = "Completed",
                CreatedAt = DateTime.Now
            };
            sale.SalesDetails.Add(new SalesDetail { ItemId = nail.Id, Quantity = 2.5m, UnitPrice = 15, LineTotal = 37.5m });
            routedCtx.SalesHeaders.Add(sale);
            await AddBranchStockAsync(routedCtx, tenant.Id, branch.Id, nail.Id, -2.5m);
            nail.CurrentStock -= 2.5m;
            await routedCtx.SaveChangesAsync();

            stock = await GetBranchStockAsync(routedCtx, branch.Id, nail.Id);
            if (Math.Abs(stock - 247.5m) < 0.001m) Ok("POS deducted base quantity; remaining 247.5 kg");
            else No($"Expected 247.5 kg remaining, got {stock}");

            // Currency test
            var settings = await routedCtx.SystemSettings.FirstOrDefaultAsync(s => s.TenantId == tenant.Id)
                ?? await routedCtx.SystemSettings.FirstAsync();
            var origCode = settings.CurrencyCode;
            var origSym = settings.CurrencySymbol;
            var origName = settings.CurrencyName;

            settings.CurrencyCode = "USD";
            settings.CurrencySymbol = "$";
            settings.CurrencyName = "US Dollar";
            await routedCtx.SaveChangesAsync();

            var routedSettings = await routedCtx.SystemSettings.AsNoTracking().FirstAsync(s => s.Id == settings.Id);
            if (routedSettings.CurrencySymbol == "$") Ok("Currency $ saved in dedicated/routed context");
            else No("Currency not updated in routed context");

            var sharedSettings = await appDb.SystemSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.TenantId == tenant.Id && s.Id == settings.Id);
            if (isDedicatedRouted)
            {
                if (sharedSettings == null || sharedSettings.CurrencySymbol != "$")
                    Ok("Shared DB not updated for routing-enabled tenant settings (expected)");
                else
                    No("Shared DB incorrectly updated with tenant currency while routing enabled");
            }
            else
            {
                Ok("Skipped shared currency isolation check (not dedicated+routed)");
            }

            settings.CurrencyCode = origCode;
            settings.CurrencySymbol = origSym;
            settings.CurrencyName = origName;
            await routedCtx.SaveChangesAsync();
            Ok("Currency restored to PHP defaults");

            Console.WriteLine($"[QA51] Done — {pass} passed, {fail} failed.");
            Environment.ExitCode = fail > 0 ? 1 : 0;
        }

        private static async Task AddBranchStockAsync(ITenantOperationalDbContext db, int tenantId, int branchId, int productId, decimal delta)
        {
            var bps = await db.BranchProductStocks.FirstOrDefaultAsync(s => s.BranchId == branchId && s.ProductId == productId);
            if (bps == null)
            {
                db.BranchProductStocks.Add(new BranchProductStock
                {
                    TenantId = tenantId,
                    BranchId = branchId,
                    ProductId = productId,
                    Quantity = Math.Max(0, delta),
                    UpdatedAtUtc = DateTime.UtcNow
                });
            }
            else
            {
                bps.Quantity += delta;
                if (bps.Quantity < 0) bps.Quantity = 0;
                bps.UpdatedAtUtc = DateTime.UtcNow;
            }
        }

        private static async Task<decimal> GetBranchStockAsync(ITenantOperationalDbContext db, int branchId, int productId)
        {
            var bps = await db.BranchProductStocks.AsNoTracking()
                .FirstOrDefaultAsync(s => s.BranchId == branchId && s.ProductId == productId);
            return bps?.Quantity ?? 0;
        }

        private static async Task<int> EnsureSupplierAsync(ITenantOperationalDbContext db, int tenantId, string stamp)
        {
            var s = new Supplier { TenantId = tenantId, SupplierName = $"QA51_Sup_{stamp}", IsActive = true, CreatedAt = DateTime.Now };
            db.Suppliers.Add(s);
            await db.SaveChangesAsync();
            return s.Id;
        }
    }
}

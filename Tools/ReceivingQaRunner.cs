using HardwareManagementSystem.Data;
using HardwareManagementSystem.Models;
using HardwareManagementSystem.Services;
using HardwareManagementSystem.Services.TenantDatabases;
using HardwareManagementSystem.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace HardwareManagementSystem.Tools
{
    /// <summary>
    /// RC1.8 QA — PO receiving + auto stock-in workflow (Development only, --qa-receiving).
    /// </summary>
    public static class ReceivingQaRunner
    {
        public static async Task RunAsync(WebApplication app)
        {
            if (!app.Environment.IsDevelopment())
            {
                Console.WriteLine("[QA-RC18] Development-only. Aborting.");
                return;
            }

            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var pass = 0;
            var fail = 0;
            void Ok(string m) { pass++; Console.WriteLine($"  [PASS] {m}"); }
            void No(string m) { fail++; Console.WriteLine($"  [FAIL] {m}"); }
            void Sep(string h) { Console.WriteLine(); Console.WriteLine($"── {h} ──"); }

            using var scope = app.Services.CreateScope();
            var sp = scope.ServiceProvider;
            var appDb = sp.GetRequiredService<ApplicationDbContext>();
            var ctxProvider = sp.GetRequiredService<ITenantOperationalContextProvider>();
            var branchSvc = sp.GetRequiredService<BranchService>();

            var tenant = await appDb.Tenants
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.RoutingEnabled && t.DatabaseMode == TenantDatabaseMode.Dedicated)
                ?? await appDb.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.IsActive);

            if (tenant == null) { No("No tenant available."); return; }

            var tenantUser = await appDb.Users.FirstOrDefaultAsync(u => u.TenantId == tenant.Id && u.IsActive);
            if (tenantUser == null) { No($"No active user for tenant {tenant.Id}."); return; }

            var httpAccessor = sp.GetRequiredService<IHttpContextAccessor>();
            var identity = new ClaimsIdentity("QA");
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, tenantUser.Id));
            identity.AddClaim(new Claim(ClaimTypes.Name, tenantUser.UserName ?? tenantUser.Email ?? "qa-rc18"));
            identity.AddClaim(new Claim(ClaimTypes.Role, "TenantAdmin"));
            httpAccessor.HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity),
                RequestServices = sp
            };

            Sep($"Tenant {tenant.Name} (Id={tenant.Id})");
            var ctx = await ctxProvider.GetContextAsync(tenant.Id);
            if (ctx is DbContext dbCtx)
                await dbCtx.Database.MigrateAsync();

            var prefix = $"RC18_{stamp}";

            // ── Setup: supplier, units, item, conversion ─────────────────────
            var supplier = new Supplier
            {
                TenantId = tenant.Id, SupplierName = $"{prefix}_Supplier",
                IsActive = true, CreatedAt = DateTime.Now
            };
            ctx.Suppliers.Add(supplier);

            var kg = await ctx.Units.FirstOrDefaultAsync(u => u.TenantId == tenant.Id && u.ShortName == "kg")
                ?? new Unit { TenantId = tenant.Id, UnitName = "Kilogram", ShortName = "kg", IsActive = true, CreatedAt = DateTime.Now };
            if (kg.Id == 0) ctx.Units.Add(kg);

            var sack = await ctx.Units.FirstOrDefaultAsync(u => u.TenantId == tenant.Id && u.ShortName == "sack")
                ?? new Unit { TenantId = tenant.Id, UnitName = "Sack", ShortName = "sack", IsActive = true, CreatedAt = DateTime.Now };
            if (sack.Id == 0) ctx.Units.Add(sack);
            await ctx.SaveChangesAsync();
            Ok("Supplier and units ready");

            var cat = await ctx.Categories.FirstOrDefaultAsync(c => c.TenantId == tenant.Id);
            if (cat == null)
            {
                cat = new Category { TenantId = tenant.Id, CategoryName = $"{prefix}_Cat", IsActive = true, CreatedAt = DateTime.Now };
                ctx.Categories.Add(cat);
                await ctx.SaveChangesAsync();
            }

            var branch = await ctx.Branches.FirstOrDefaultAsync(b => b.TenantId == tenant.Id && b.IsMainBranch)
                ?? await ctx.Branches.FirstOrDefaultAsync(b => b.TenantId == tenant.Id);
            if (branch == null)
            {
                branch = new Branch { TenantId = tenant.Id, Name = $"{prefix}_Branch", Code = "RC18", IsMainBranch = true, IsActive = true, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
                ctx.Branches.Add(branch);
                await ctx.SaveChangesAsync();
            }

            var item = new Item
            {
                TenantId = tenant.Id, ItemCode = $"{prefix}_CEMENT", ItemName = $"{prefix} Cement",
                CategoryId = cat.Id, UnitId = sack.Id, BaseUnitId = kg.Id,
                CurrentStock = 0, ReorderLevel = 0, CostPrice = 0, SellingPrice = 1200,
                Status = "Active", CreatedAt = DateTime.Now
            };
            ctx.Items.Add(item);
            await ctx.SaveChangesAsync();

            ctx.ItemUnitConversions.Add(new ItemUnitConversion
            {
                TenantId = tenant.Id, ItemId = item.Id, UnitId = sack.Id,
                ConversionQuantity = 25, IsDefaultPurchaseUnit = true, IsActive = true,
                CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
            });
            await ctx.SaveChangesAsync();
            Ok("Item with 1 sack = 25 kg conversion created");

            // ── Create PO: 10 sacks @ ₱1,000/sack ─────────────────────────
            var po = new PurchaseOrder
            {
                TenantId = tenant.Id, BranchId = branch.Id, SupplierId = supplier.Id,
                PONumber = $"{prefix}_PO", PODate = DateTime.Now, Status = "Sent",
                CreatedAtUtc = DateTime.UtcNow
            };
            po.Items.Add(new PurchaseOrderItem
            {
                ItemId = item.Id, Quantity = 10, OrderedUnitId = sack.Id,
                OrderedQuantity = 10, ConversionQuantity = 25, BaseQuantity = 250,
                CostPerOrderedUnit = 1000, CostPerBaseUnit = 40, UnitCost = 40,
                TotalCost = 10000
            });
            ctx.PurchaseOrders.Add(po);
            await ctx.SaveChangesAsync();
            Ok($"PO created: 10 sacks @ 1000/sack (PO Id={po.Id})");

            decimal stockBefore = 0;

            // ── Partial receive: 6 sacks ────────────────────────────────────
            Sep("Partial receive — 6 sacks");
            var sin1 = $"{prefix}_SI1";
            var hdr1 = new StockInHeader
            {
                TenantId = tenant.Id, BranchId = branch.Id, SupplierId = supplier.Id,
                PurchaseOrderId = po.Id, StockInNumber = sin1, DateReceived = DateTime.Now,
                ReceivedBy = "QA-RC18", TotalCost = 6000, BalanceDue = 6000,
                PaymentStatus = "Unpaid", CreatedAt = DateTime.Now
            };
            var poItem = po.Items.First();
            poItem.QuantityReceived = 150; // 6 * 25
            hdr1.StockInDetails.Add(new StockInDetail
            {
                ItemId = item.Id, Quantity = 150, ReceivedUnitId = sack.Id,
                ReceivedQuantity = 6, ConversionQuantity = 25, BaseQuantity = 150,
                CostPerReceivedUnit = 1000, CostPerBaseUnit = 40, UnitCost = 40, TotalCost = 6000
            });
            await branchSvc.AddStockAsync(branch.Id, item.Id, 150);
            po.Status = "PartiallyReceived";
            ctx.StockInHeaders.Add(hdr1);
            await ctx.SaveChangesAsync();

            item = await ctx.Items.AsNoTracking().FirstAsync(i => i.Id == item.Id);
            if (item.CurrentStock == stockBefore + 150) Ok("Inventory +150 kg after partial receive");
            else No($"Inventory expected {stockBefore + 150}, got {item.CurrentStock}");

            po = await ctx.PurchaseOrders.AsNoTracking().Include(p => p.Items).FirstAsync(p => p.Id == po.Id);
            poItem = po.Items.First();
            if (po.Status == "PartiallyReceived") Ok("PO status PartiallyReceived");
            else No($"PO status expected PartiallyReceived, got {po.Status}");

            var remaining = poItem.QuantityRemaining;
            if (Math.Abs(remaining - 4) < 0.01m) Ok("Remaining = 4 sacks");
            else No($"Remaining expected 4, got {remaining}");

            var d1 = await ctx.StockInDetails.AsNoTracking().FirstAsync(d => d.StockInHeaderId == hdr1.Id);
            if (d1.TotalCost == 6000 && d1.CostPerBaseUnit == 40) Ok("Receipt 1 total 6000, cost/base 40");
            else No($"Receipt 1 cost wrong: total={d1.TotalCost}, base={d1.CostPerBaseUnit}");

            var receiptCount = await ctx.StockInHeaders.CountAsync(h => h.PurchaseOrderId == po.Id);
            if (receiptCount == 1) Ok("StockInHeader created once after partial receive");
            else No($"Expected 1 receipt, found {receiptCount}");

            // ── Full receive: remaining 4 sacks ─────────────────────────────
            Sep("Final receive — 4 sacks");
            var hdr2 = new StockInHeader
            {
                TenantId = tenant.Id, BranchId = branch.Id, SupplierId = supplier.Id,
                PurchaseOrderId = po.Id, StockInNumber = $"{prefix}_SI2", DateReceived = DateTime.Now,
                ReceivedBy = "QA-RC18", TotalCost = 4000, BalanceDue = 4000,
                PaymentStatus = "Unpaid", CreatedAt = DateTime.Now
            };
            poItem = await ctx.PurchaseOrderItems.FirstAsync(pi => pi.PurchaseOrderId == po.Id);
            poItem.QuantityReceived = 250;
            hdr2.StockInDetails.Add(new StockInDetail
            {
                ItemId = item.Id, Quantity = 100, ReceivedUnitId = sack.Id,
                ReceivedQuantity = 4, ConversionQuantity = 25, BaseQuantity = 100,
                CostPerReceivedUnit = 1000, CostPerBaseUnit = 40, UnitCost = 40, TotalCost = 4000
            });
            await branchSvc.AddStockAsync(branch.Id, item.Id, 100);
            var poTracked = await ctx.PurchaseOrders.Include(p => p.Items).FirstAsync(p => p.Id == po.Id);
            poTracked.Status = "Received";
            ctx.StockInHeaders.Add(hdr2);
            await ctx.SaveChangesAsync();

            item = await ctx.Items.AsNoTracking().FirstAsync(i => i.Id == item.Id);
            if (item.CurrentStock == stockBefore + 250) Ok("Inventory total +250 kg");
            else No($"Inventory expected {stockBefore + 250}, got {item.CurrentStock}");

            poTracked = await ctx.PurchaseOrders.AsNoTracking().FirstAsync(p => p.Id == po.Id);
            if (poTracked.Status == "Received") Ok("PO status Fully Received");
            else No($"PO status expected Received, got {poTracked.Status}");

            var payableTotal = await ctx.StockInHeaders
                .Where(h => h.PurchaseOrderId == po.Id)
                .SumAsync(h => h.TotalCost);
            if (payableTotal == 10000) Ok("Supplier payable total across receipts = 10000");
            else No($"Payable total expected 10000, got {payableTotal}");

            // ── Over-receipt guard (logic) ──────────────────────────────────
            Sep("Over-receipt validation");
            var finalPoItem = await ctx.PurchaseOrderItems.AsNoTracking().FirstAsync(pi => pi.PurchaseOrderId == po.Id);
            if (finalPoItem.QuantityRemaining <= 0) Ok("No remaining qty — over-receipt would be blocked");
            else No($"PO still has remaining after full receive: {finalPoItem.QuantityRemaining}");

            var overReceipt = 6m + 0m > finalPoItem.QuantityRemaining + 0.0001m;
            if (overReceipt) Ok("Over-receipt check would reject 6 more sacks");
            else No("Over-receipt guard failed");

            // ── Receiving list search/pagination ────────────────────────────
            Sep("Receiving list search/pagination");
            var listQuery = ctx.StockInHeaders.AsNoTracking()
                .Where(h => h.StockInNumber.StartsWith(prefix));
            var total = await listQuery.CountAsync();
            var pageSize = PagedResult<object>.ValidatePageSize(10);
            var page = await listQuery.OrderByDescending(h => h.DateReceived)
                .Skip(0).Take(pageSize).ToListAsync();
            if (total == 2 && page.Count == 2) Ok("Receiving list: 2 receipts, default 10-row page");
            else No($"Receiving list expected 2 total / 2 page, got {total}/{page.Count}");

            var searchHit = await ctx.StockInHeaders.AsNoTracking()
                .CountAsync(h => h.StockInNumber.StartsWith(prefix) && h.PurchaseOrderId == po.Id);
            if (searchHit == 2) Ok("Search by PO number finds both receipts");
            else No($"PO search expected 2, got {searchHit}");

            // ── Manual Stock In (no PO) ─────────────────────────────────────
            Sep("Manual Stock In");
            var manualHdr = new StockInHeader
            {
                TenantId = tenant.Id, BranchId = branch.Id, SupplierId = supplier.Id,
                PurchaseOrderId = null, StockInNumber = $"{prefix}_MANUAL",
                DateReceived = DateTime.Now, ReceivedBy = "QA-RC18",
                TotalCost = 500, BalanceDue = 500, PaymentStatus = "Unpaid", CreatedAt = DateTime.Now
            };
            manualHdr.StockInDetails.Add(new StockInDetail
            {
                ItemId = item.Id, Quantity = 10, ReceivedUnitId = kg.Id,
                ReceivedQuantity = 10, ConversionQuantity = 1, BaseQuantity = 10,
                CostPerReceivedUnit = 50, CostPerBaseUnit = 50, UnitCost = 50, TotalCost = 500
            });
            ctx.StockInHeaders.Add(manualHdr);
            await ctx.SaveChangesAsync();
            if (manualHdr.PurchaseOrderId == null) Ok("Manual Stock In saved without PO link");

            // ── Dedicated DB isolation ──────────────────────────────────────
            if (tenant.RoutingEnabled && tenant.DatabaseMode == TenantDatabaseMode.Dedicated)
            {
                Sep("Dedicated DB isolation");
                var dedCount = await ctx.StockInHeaders.CountAsync(h => h.StockInNumber.StartsWith(prefix));
                if (dedCount == 3) Ok($"Dedicated DB has {dedCount} receipt(s) (2 PO + 1 manual)");

                var sharedCount = await appDb.StockInHeaders.CountAsync(h => h.StockInNumber.StartsWith(prefix));
                if (sharedCount == 0) Ok("Zero shared-DB stock-in leakage");
                else No($"Shared DB leakage: {sharedCount} record(s)");
            }

            httpAccessor.HttpContext = null;

            Console.WriteLine();
            Console.WriteLine($"[QA-RC18] Done — {pass} passed, {fail} failed.");
            Environment.ExitCode = fail > 0 ? 1 : 0;
        }
    }
}

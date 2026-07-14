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
    /// <summary>RC1.8.1 receiving UX + warehouse QA (--qa-receiving-ux).</summary>
    public static class ReceivingUxQaRunner
    {
        public static async Task RunAsync(WebApplication app)
        {
            if (!app.Environment.IsDevelopment())
            {
                Console.WriteLine("[QA-RC181] Development-only. Aborting.");
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

            if (tenant == null) { No("No tenant."); return; }

            var tenantUser = await appDb.Users.FirstOrDefaultAsync(u => u.TenantId == tenant.Id && u.IsActive);
            if (tenantUser == null) { No("No tenant user."); return; }

            var httpAccessor = sp.GetRequiredService<IHttpContextAccessor>();
            var identity = new ClaimsIdentity("QA");
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, tenantUser.Id));
            identity.AddClaim(new Claim(ClaimTypes.Name, tenantUser.UserName ?? "qa-rc181"));
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

            var prefix = $"RC181_{stamp}";
            var invoice1 = $"INV-{stamp}-1";
            var invoice2 = $"INV-{stamp}-2";

            // Setup
            var supplier = new Supplier { TenantId = tenant.Id, SupplierName = $"{prefix}_Sup", IsActive = true, CreatedAt = DateTime.Now };
            ctx.Suppliers.Add(supplier);
            var kg = await ctx.Units.FirstOrDefaultAsync(u => u.TenantId == tenant.Id && u.ShortName == "kg")
                ?? new Unit { TenantId = tenant.Id, UnitName = "kg", ShortName = "kg", IsActive = true, CreatedAt = DateTime.Now };
            if (kg.Id == 0) ctx.Units.Add(kg);
            var sack = await ctx.Units.FirstOrDefaultAsync(u => u.TenantId == tenant.Id && u.ShortName == "sack")
                ?? new Unit { TenantId = tenant.Id, UnitName = "sack", ShortName = "sack", IsActive = true, CreatedAt = DateTime.Now };
            if (sack.Id == 0) ctx.Units.Add(sack);
            await ctx.SaveChangesAsync();

            var cat = await ctx.Categories.FirstOrDefaultAsync(c => c.TenantId == tenant.Id)
                ?? new Category { TenantId = tenant.Id, CategoryName = $"{prefix}_Cat", IsActive = true, CreatedAt = DateTime.Now };
            if (cat.Id == 0) { ctx.Categories.Add(cat); await ctx.SaveChangesAsync(); }

            var branch = await ctx.Branches.FirstOrDefaultAsync(b => b.TenantId == tenant.Id)
                ?? new Branch { TenantId = tenant.Id, Name = $"{prefix}_Br", Code = "R181", IsMainBranch = true, IsActive = true, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
            if (branch.Id == 0) { ctx.Branches.Add(branch); await ctx.SaveChangesAsync(); }

            var item = new Item
            {
                TenantId = tenant.Id, ItemCode = $"{prefix}_ITM", ItemName = $"{prefix} Cement",
                CategoryId = cat.Id, UnitId = sack.Id, BaseUnitId = kg.Id,
                CurrentStock = 0, CostPrice = 0, SellingPrice = 100, Status = "Active", CreatedAt = DateTime.Now
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
            Ok("Setup complete");

            var po = new PurchaseOrder
            {
                TenantId = tenant.Id, BranchId = branch.Id, SupplierId = supplier.Id,
                PONumber = $"{prefix}_PO", PODate = DateTime.Now, Status = "Sent", CreatedAtUtc = DateTime.UtcNow
            };
            po.Items.Add(new PurchaseOrderItem
            {
                ItemId = item.Id, Quantity = 10, OrderedUnitId = sack.Id, OrderedQuantity = 10,
                ConversionQuantity = 25, BaseQuantity = 250, CostPerOrderedUnit = 1000,
                CostPerBaseUnit = 40, UnitCost = 40, TotalCost = 10000
            });
            ctx.PurchaseOrders.Add(po);
            await ctx.SaveChangesAsync();

            // Partial receipt 1
            var hdr1 = new StockInHeader
            {
                TenantId = tenant.Id, BranchId = branch.Id, SupplierId = supplier.Id,
                PurchaseOrderId = po.Id, StockInNumber = $"{prefix}_R1", DateReceived = DateTime.Now,
                InvoiceNumber = invoice1, ReceivedBy = "QA-RC181", TotalCost = 6000, BalanceDue = 6000,
                PaymentStatus = "Unpaid", CreatedAt = DateTime.Now
            };
            hdr1.StockInDetails.Add(new StockInDetail
            {
                ItemId = item.Id, Quantity = 150, ReceivedUnitId = sack.Id, ReceivedQuantity = 6,
                ConversionQuantity = 25, BaseQuantity = 150, CostPerReceivedUnit = 1000,
                CostPerBaseUnit = 40, UnitCost = 40, TotalCost = 6000
            });
            po.Items.First().QuantityReceived = 150;
            po.Status = "PartiallyReceived";
            po.UpdatedAtUtc = DateTime.UtcNow;
            await branchSvc.AddStockAsync(branch.Id, item.Id, 150);
            ctx.StockInHeaders.Add(hdr1);
            await ctx.SaveChangesAsync();

            // Partial receipt 2
            var hdr2 = new StockInHeader
            {
                TenantId = tenant.Id, BranchId = branch.Id, SupplierId = supplier.Id,
                PurchaseOrderId = po.Id, StockInNumber = $"{prefix}_R2", DateReceived = DateTime.Now,
                InvoiceNumber = invoice2, ReceivedBy = "QA-RC181", TotalCost = 4000, BalanceDue = 4000,
                PaymentStatus = "Unpaid", CreatedAt = DateTime.Now
            };
            hdr2.StockInDetails.Add(new StockInDetail
            {
                ItemId = item.Id, Quantity = 100, ReceivedUnitId = sack.Id, ReceivedQuantity = 4,
                ConversionQuantity = 25, BaseQuantity = 100, CostPerReceivedUnit = 1000,
                CostPerBaseUnit = 40, UnitCost = 40, TotalCost = 4000
            });
            var poTracked = await ctx.PurchaseOrders.Include(p => p.Items).FirstAsync(p => p.Id == po.Id);
            poTracked.Items.First().QuantityReceived = 250;
            poTracked.Status = "Received";
            poTracked.UpdatedAtUtc = DateTime.UtcNow;
            await branchSvc.AddStockAsync(branch.Id, item.Id, 100);
            ctx.StockInHeaders.Add(hdr2);
            await ctx.SaveChangesAsync();

            Sep("PO progress + history");
            po = await ctx.PurchaseOrders.AsNoTracking().Include(p => p.Items).ThenInclude(i => i.OrderedUnit).FirstAsync(p => p.Id == po.Id);
            var progress = new PoReceivingProgressVm
            {
                Ordered = po.Items.Sum(i => i.OrderedQuantity),
                Received = po.Items.Sum(i => i.QuantityReceived / i.ConversionQuantity),
                Remaining = po.Items.Sum(i => i.QuantityRemaining)
            };
            if (progress.Ordered == 10 && progress.Received == 10 && progress.Remaining == 0)
                Ok("PO progress: 10 ordered, 10 received, 0 remaining");
            else No($"Progress wrong: {progress.Ordered}/{progress.Received}/{progress.Remaining}");

            var historyCount = await ctx.StockInHeaders.CountAsync(h => h.PurchaseOrderId == po.Id);
            if (historyCount == 2) Ok("Receiving history: 2 receipts");
            else No($"Expected 2 receipts, got {historyCount}");

            Sep("Search by PO number and supplier invoice");
            var byPo = await ctx.StockInHeaders.CountAsync(h =>
                h.PurchaseOrder!.PONumber == $"{prefix}_PO");
            if (byPo == 2) Ok("Search by PO number");
            else No($"PO search: {byPo}");

            var byInvoice = await ctx.StockInHeaders.CountAsync(h => h.InvoiceNumber == invoice1);
            if (byInvoice == 1) Ok("Search by supplier invoice");
            else No($"Invoice search: {byInvoice}");

            var byReceivedBy = await ctx.StockInHeaders.CountAsync(h => h.ReceivedBy == "QA-RC181");
            if (byReceivedBy >= 2) Ok("Search by received-by");
            else No($"ReceivedBy search: {byReceivedBy}");

            Sep("Dashboard KPIs (CountAsync)");
            var today = DateTime.Today;
            var tomorrow = today.AddDays(1);
            var todaysReceipts = await ctx.StockInHeaders.CountAsync(h =>
                h.StockInNumber.StartsWith(prefix) && h.DateReceived >= today && h.DateReceived < tomorrow);
            if (todaysReceipts == 2) Ok($"Today's receipts KPI = {todaysReceipts}");
            else No($"Today's receipts expected 2, got {todaysReceipts}");

            var payablesDue = await ctx.StockInHeaders.CountAsync(h =>
                h.StockInNumber.StartsWith(prefix) && h.PaymentStatus != "Paid" && h.BalanceDue > 0);
            if (payablesDue == 2) Ok($"Supplier payables due KPI = {payablesDue}");
            else No($"Payables due expected 2, got {payablesDue}");

            var completedToday = await ctx.PurchaseOrders.CountAsync(p =>
                p.PONumber == $"{prefix}_PO" && p.Status == "Received" &&
                p.UpdatedAtUtc.HasValue && p.UpdatedAtUtc.Value >= DateTime.UtcNow.Date);
            if (completedToday == 1) Ok("Completed PO today KPI");
            else No($"Completed PO today: {completedToday}");

            Sep("Inventory + payable");
            item = await ctx.Items.AsNoTracking().FirstAsync(i => i.Id == item.Id);
            if (item.CurrentStock == 250) Ok("Inventory +250 kg");
            else No($"Inventory expected 250, got {item.CurrentStock}");

            var payableTotal = await ctx.StockInHeaders.Where(h => h.PurchaseOrderId == po.Id).SumAsync(h => h.TotalCost);
            if (payableTotal == 10000) Ok("Payable total 10000 across 2 receipts");
            else No($"Payable total {payableTotal}");

            Sep("Dedicated DB isolation");
            if (tenant.RoutingEnabled && tenant.DatabaseMode == TenantDatabaseMode.Dedicated)
            {
                var sharedLeak = await appDb.StockInHeaders.CountAsync(h => h.StockInNumber.StartsWith(prefix));
                if (sharedLeak == 0) Ok("Zero shared DB leakage");
                else No($"Shared leakage: {sharedLeak}");
            }

            httpAccessor.HttpContext = null;
            Console.WriteLine();
            Console.WriteLine($"[QA-RC181] Done — {pass} passed, {fail} failed.");
            Environment.ExitCode = fail > 0 ? 1 : 0;
        }
    }
}

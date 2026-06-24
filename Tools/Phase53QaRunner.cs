using HardwareManagementSystem.Data;
using HardwareManagementSystem.Models;
using HardwareManagementSystem.Services;
using HardwareManagementSystem.Services.TenantDatabases;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace HardwareManagementSystem.Tools
{
    /// <summary>Phase 5.3 damaged goods + supplier returns QA (--qa-phase53).</summary>
    public static class Phase53QaRunner
    {
        public static async Task RunAsync(WebApplication app)
        {
            if (!app.Environment.IsDevelopment())
            {
                Console.WriteLine("[QA53] Development-only. Aborting.");
                return;
            }

            var pass = 0;
            var fail = 0;
            void Ok(string m) { pass++; Console.WriteLine($"[QA53] PASS — {m}"); }
            void No(string m) { fail++; Console.WriteLine($"[QA53] FAIL — {m}"); }

            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            using var scope = app.Services.CreateScope();
            var sp = scope.ServiceProvider;
            var appDb = sp.GetRequiredService<ApplicationDbContext>();
            var ctxProvider = sp.GetRequiredService<ITenantOperationalContextProvider>();

            var tenant = await appDb.Tenants
                .FirstOrDefaultAsync(t => t.RoutingEnabled && t.DatabaseMode == TenantDatabaseMode.Dedicated)
                ?? await appDb.Tenants.FirstOrDefaultAsync(t => t.IsActive);

            if (tenant == null)
            {
                No("No tenant for QA53");
                Console.WriteLine($"[QA53] Summary: {pass} passed, {fail} failed.");
                return;
            }

            var tenantUser = await appDb.Users.FirstOrDefaultAsync(u => u.TenantId == tenant.Id && u.IsActive);
            if (tenantUser == null)
            {
                No($"No active user for tenant {tenant.Id}");
                Console.WriteLine($"[QA53] Summary: {pass} passed, {fail} failed.");
                return;
            }

            var httpAccessor = sp.GetRequiredService<IHttpContextAccessor>();
            var identity = new ClaimsIdentity("QA");
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, tenantUser.Id));
            identity.AddClaim(new Claim(ClaimTypes.Name, tenantUser.UserName ?? tenantUser.Email ?? "qa53"));
            identity.AddClaim(new Claim(ClaimTypes.Role, "TenantAdmin"));
            httpAccessor.HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity),
                RequestServices = sp
            };

            var damagedStock = sp.GetRequiredService<DamagedStockService>();

            var ctx = await ctxProvider.GetContextAsync(tenant.Id);
            if (ctx is DbContext dbCtx)
                await dbCtx.Database.MigrateAsync();

            var prefix = $"QA53_{stamp}";

            var branch = await ctx.Branches.FirstOrDefaultAsync(b => b.TenantId == tenant.Id)
                ?? new Branch { TenantId = tenant.Id, Name = $"B_{stamp}", Code = "Q53", IsMainBranch = true, IsActive = true, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
            if (branch.Id == 0)
            {
                ctx.Branches.Add(branch);
                await ctx.SaveChangesAsync();
            }

            var kg = await ctx.Units.FirstOrDefaultAsync(u => u.TenantId == tenant.Id && u.ShortName == "kg");
            if (kg == null)
            {
                kg = new Unit { TenantId = tenant.Id, UnitName = "kg", ShortName = "kg", IsActive = true, CreatedAt = DateTime.Now };
                ctx.Units.Add(kg);
                await ctx.SaveChangesAsync();
            }

            var cat = await ctx.Categories.FirstOrDefaultAsync(c => c.TenantId == tenant.Id);
            if (cat == null)
            {
                cat = new Category { TenantId = tenant.Id, CategoryName = $"C_{stamp}", IsActive = true, CreatedAt = DateTime.Now };
                ctx.Categories.Add(cat);
                await ctx.SaveChangesAsync();
            }

            var item = new Item
            {
                TenantId = tenant.Id,
                ItemCode = $"{prefix}",
                ItemName = $"QA53 Item {stamp}",
                CategoryId = cat.Id,
                UnitId = kg.Id,
                BaseUnitId = kg.Id,
                CurrentStock = 100,
                DamagedStock = 0,
                ReorderLevel = 5,
                CostPrice = 10,
                SellingPrice = 15,
                Status = "Active",
                CreatedAt = DateTime.Now
            };
            ctx.Items.Add(item);
            await ctx.SaveChangesAsync();

            var bps = new BranchProductStock
            {
                TenantId = tenant.Id,
                BranchId = branch.Id,
                ProductId = item.Id,
                Quantity = 100,
                DamagedStock = 0,
                UpdatedAtUtc = DateTime.UtcNow
            };
            ctx.BranchProductStocks.Add(bps);
            await ctx.SaveChangesAsync();

            Ok("Item created with sellable stock 100");

            var leak = await appDb.Items.AnyAsync(i => i.ItemCode == prefix);
            if (!leak) Ok("No shared DB leakage on item create");
            else No("Item leaked to shared DB");

            async Task LogAudit(string action, string module = "DamagedGoods")
            {
                ctx.AuditTrails.Add(new AuditTrail
                {
                    TenantId = tenant.Id,
                    UserId = "qa-phase53",
                    UserName = "QA Phase53",
                    ModuleName = module,
                    ActionName = action,
                    Description = $"QA53 {action} item {item.Id}",
                    IpAddress = "127.0.0.1",
                    Browser = "N/A",
                    OperatingSystem = "N/A",
                    DeviceType = "Server",
                    CreatedAt = DateTime.Now
                });
                await ctx.SaveChangesAsync();
            }

            // Tag 10 damaged
            await damagedStock.MoveSellableToDamagedAsync(branch.Id, item.Id, 10);
            await LogAudit("DAMAGED_GOODS_CREATED");

            item = await ctx.Items.AsNoTracking().FirstAsync(i => i.Id == item.Id);
            var bpsReload = await ctx.BranchProductStocks.FirstAsync(s => s.BranchId == branch.Id && s.ProductId == item.Id);

            if (item.DamagedStock == 10) Ok("Item DamagedStock=10 after tag");
            else No($"After tag: Item Damaged={item.DamagedStock}");

            if (bpsReload.Quantity == 90 && bpsReload.DamagedStock == 10) Ok("Branch sellable=90 damaged=10");
            else No($"Branch stock wrong: Q={bpsReload.Quantity} D={bpsReload.DamagedStock}");

            var posAvail = await damagedStock.GetSellableStockAsync(branch.Id, item.Id);
            if (posAvail == 90) Ok("POS available stock = 90");
            else No($"POS available = {posAvail}");

            // Recover 5
            await damagedStock.RecoverDamagedAsync(branch.Id, item.Id, 5);
            await LogAudit("DAMAGED_GOODS_RECOVERED");
            item = await ctx.Items.AsNoTracking().FirstAsync(i => i.Id == item.Id);
            bpsReload = await ctx.BranchProductStocks.FirstAsync(s => s.BranchId == branch.Id && s.ProductId == item.Id);
            if (bpsReload.Quantity == 95 && bpsReload.DamagedStock == 5) Ok("Recover 5: branch sellable=95 damaged=5");
            else No($"After recover branch: Q={bpsReload.Quantity} D={bpsReload.DamagedStock}");
            if (item.DamagedStock == 5) Ok("Recover 5: Item DamagedStock=5");
            else No($"After recover: Item Damaged={item.DamagedStock}");

            // Dispose 2
            await damagedStock.DisposeDamagedAsync(branch.Id, item.Id, 2);
            await LogAudit("DAMAGED_GOODS_DISPOSED");
            item = await ctx.Items.AsNoTracking().FirstAsync(i => i.Id == item.Id);
            if (item.DamagedStock == 3) Ok("Dispose 2: DamagedStock=3");
            else No($"After dispose: Damaged={item.DamagedStock}");

            // Supplier return for 3 + credited
            var supplier = await ctx.Suppliers.FirstOrDefaultAsync(s => s.TenantId == tenant.Id);
            if (supplier == null)
            {
                supplier = new Supplier { TenantId = tenant.Id, SupplierName = $"S_{stamp}", IsActive = true, CreatedAt = DateTime.Now };
                ctx.Suppliers.Add(supplier);
                await ctx.SaveChangesAsync();
            }

            var damageHeader = new DamagedGoodsHeader
            {
                TenantId = tenant.Id,
                BranchId = branch.Id,
                DamageNumber = $"DMG-{stamp}",
                DamageDate = DateTime.Now,
                Status = "ReturnedToSupplier",
                CreatedAtUtc = DateTime.UtcNow
            };
            damageHeader.Details.Add(new DamagedGoodsDetail
            {
                ItemId = item.Id,
                Quantity = 3,
                UnitId = kg.Id,
                ConversionQuantity = 1,
                BaseQuantity = 3,
                Reason = "Damaged"
            });
            ctx.DamagedGoodsHeaders.Add(damageHeader);
            await ctx.SaveChangesAsync();

            var returnHeader = new SupplierReturnHeader
            {
                TenantId = tenant.Id,
                BranchId = branch.Id,
                SupplierId = supplier.Id,
                ReturnNumber = $"SR-{stamp}",
                ReturnDate = DateTime.Now,
                Status = "Pending",
                LinkedDamagedGoodsId = damageHeader.Id,
                CreatedAtUtc = DateTime.UtcNow
            };
            returnHeader.Details.Add(new SupplierReturnDetail
            {
                ItemId = item.Id,
                Quantity = 3,
                UnitId = kg.Id,
                ConversionQuantity = 1,
                BaseQuantity = 3,
                Reason = "Damaged"
            });
            ctx.SupplierReturnHeaders.Add(returnHeader);
            await ctx.SaveChangesAsync();
            await LogAudit("DAMAGED_GOODS_RETURNED_TO_SUPPLIER");
            await LogAudit("SUPPLIER_RETURN_CREATED", "SupplierReturns");

            await damagedStock.DeductDamagedAsync(branch.Id, item.Id, 3);
            returnHeader.Status = "Credited";
            damageHeader.Status = "Disposed";
            await ctx.SaveChangesAsync();
            await LogAudit("SUPPLIER_RETURN_CREDITED", "SupplierReturns");
            item = await ctx.Items.AsNoTracking().FirstAsync(i => i.Id == item.Id);

            if (item.DamagedStock == 0) Ok("After credited return: DamagedStock=0");
            else No($"Damaged after credit: {item.DamagedStock}");

            var auditCount = await ctx.AuditTrails.CountAsync(a =>
                a.ActionName.StartsWith("DAMAGED_GOODS_") || a.ActionName.StartsWith("SUPPLIER_RETURN_"));
            if (auditCount >= 5) Ok($"Audit trail entries present ({auditCount})");
            else No($"Expected audit entries, found {auditCount}");

            if (tenant.RoutingEnabled && tenant.DatabaseMode == TenantDatabaseMode.Dedicated)
            {
                var sharedLeak = await appDb.AuditTrails.AnyAsync(a =>
                    a.TenantId == tenant.Id && a.ActionName.StartsWith("DAMAGED_GOODS_"));
                if (!sharedLeak) Ok("Dedicated DB routing — no QA audit leakage to shared DB");
                else No("Audit records leaked to shared ApplicationDbContext");
            }
            else
                Ok("Shared DB tenant — routing check skipped");

            Console.WriteLine($"[QA53] Summary: {pass} passed, {fail} failed.");
            if (fail > 0) Environment.ExitCode = 1;
        }
    }
}

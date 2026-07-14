using HardwareManagementSystem.Controllers;
using HardwareManagementSystem.Data;
using HardwareManagementSystem.Models;
using HardwareManagementSystem.Services;
using HardwareManagementSystem.Services.TenantDatabases;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using System.Security.Claims;

namespace HardwareManagementSystem.Tools
{
    /// <summary>
    /// RC1.8.2 — Click-through simulation of the full PO/receiving workflow.
    /// Exercises actual MVC controller actions (not just EF unit checks).
    /// Development only: --qa-po-clickthrough
    /// </summary>
    public static class PoWorkflowClickthroughQaRunner
    {
        public static async Task RunAsync(WebApplication app)
        {
            if (!app.Environment.IsDevelopment())
            {
                Console.WriteLine("[QA-PO-CT] Development-only. Aborting.");
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

            var tenant = await appDb.Tenants.AsNoTracking()
                .FirstOrDefaultAsync(t => t.RoutingEnabled && t.DatabaseMode == TenantDatabaseMode.Dedicated)
                ?? await appDb.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.IsActive);
            if (tenant == null) { No("No tenant."); return; }

            var tenantUser = await appDb.Users.FirstOrDefaultAsync(u => u.TenantId == tenant.Id && u.IsActive);
            if (tenantUser == null) { No("No tenant user."); return; }

            var identity = new ClaimsIdentity("QA");
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, tenantUser.Id));
            identity.AddClaim(new Claim(ClaimTypes.Name, tenantUser.UserName ?? tenantUser.Email ?? "qa-po-ct"));
            identity.AddClaim(new Claim(ClaimTypes.Role, "TenantAdmin"));

            var httpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity),
                RequestServices = sp
            };
            httpContext.Features.Set<ISessionFeature>(new SessionFeature { Session = new QaTestSession() });
            sp.GetRequiredService<IHttpContextAccessor>().HttpContext = httpContext;

            var ctx = await ctxProvider.GetContextAsync(tenant.Id);
            if (ctx is DbContext dbCtx)
                await dbCtx.Database.MigrateAsync();

            var prefix = $"POCT_{stamp}";

            Sep("Setup master data");
            var supplier = new Supplier { TenantId = tenant.Id, SupplierName = $"{prefix}_Sup", IsActive = true, CreatedAt = DateTime.Now };
            ctx.Suppliers.Add(supplier);
            var unit = await ctx.Units.FirstOrDefaultAsync(u => u.TenantId == tenant.Id)
                ?? new Unit { TenantId = tenant.Id, UnitName = "pc", ShortName = "pc", IsActive = true, CreatedAt = DateTime.Now };
            if (unit.Id == 0) ctx.Units.Add(unit);
            await ctx.SaveChangesAsync();

            var cat = await ctx.Categories.FirstOrDefaultAsync(c => c.TenantId == tenant.Id)
                ?? new Category { TenantId = tenant.Id, CategoryName = $"{prefix}_Cat", IsActive = true, CreatedAt = DateTime.Now };
            if (cat.Id == 0) { ctx.Categories.Add(cat); await ctx.SaveChangesAsync(); }

            var branch = await ctx.Branches.FirstOrDefaultAsync(b => b.TenantId == tenant.Id)
                ?? new Branch { TenantId = tenant.Id, Name = $"{prefix}_Br", Code = "PCT", IsMainBranch = true, IsActive = true, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
            if (branch.Id == 0) { ctx.Branches.Add(branch); await ctx.SaveChangesAsync(); }

            var item = new Item
            {
                TenantId = tenant.Id, ItemCode = $"{prefix}_ITM", ItemName = $"{prefix} Widget",
                CategoryId = cat.Id, UnitId = unit.Id, BaseUnitId = unit.Id,
                CurrentStock = 0, CostPrice = 0, SellingPrice = 100, Status = "Active", CreatedAt = DateTime.Now
            };
            ctx.Items.Add(item);
            await ctx.SaveChangesAsync();
            Ok("Supplier, category, unit, product created");

            var po = new PurchaseOrder
            {
                TenantId = tenant.Id, BranchId = branch.Id, SupplierId = supplier.Id,
                PONumber = $"{prefix}_PO", PODate = DateTime.Now, Status = "Sent",
                CreatedAtUtc = DateTime.UtcNow
            };
            po.Items.Add(new PurchaseOrderItem
            {
                ItemId = item.Id, Quantity = 10, OrderedUnitId = unit.Id, OrderedQuantity = 10,
                ConversionQuantity = 1, BaseQuantity = 10, CostPerOrderedUnit = 50,
                CostPerBaseUnit = 50, UnitCost = 50, TotalCost = 500
            });
            ctx.PurchaseOrders.Add(po);
            await ctx.SaveChangesAsync();
            Ok($"PO created (Id={po.Id})");

            Sep("Controller click-through");

            // ── PurchaseOrdersController.Details (was throwing on Include Tenant) ──
            var poCtrl = ActivatorUtilities.CreateInstance<PurchaseOrdersController>(sp);
            await PrimeControllerAsync(poCtrl, httpContext, ctxProvider);
            var detailsResult = await poCtrl.Details(po.Id);
            if (detailsResult is ViewResult) Ok("PurchaseOrders/Details");
            else No($"PurchaseOrders/Details returned {detailsResult?.GetType().Name}");

            // ── PurchaseOrdersController.Receive GET ──
            var receiveGet = await poCtrl.Receive(po.Id);
            if (receiveGet is ViewResult) Ok("PurchaseOrders/Receive (GET)");
            else No($"PurchaseOrders/Receive GET returned {receiveGet?.GetType().Name}");

            // ── PurchaseOrdersController.Edit GET ──
            var editGet = await poCtrl.Edit(po.Id);
            if (editGet is RedirectToActionResult) Ok("PurchaseOrders/Edit (redirects for Sent PO — expected)");
            else if (editGet is ViewResult) Ok("PurchaseOrders/Edit (GET)");
            else No($"PurchaseOrders/Edit GET returned {editGet?.GetType().Name}");

            // ── PurchaseOrdersController.Index ──
            var indexResult = await poCtrl.Index();
            if (indexResult is ViewResult) Ok("PurchaseOrders/Index");
            else No($"PurchaseOrders/Index returned {indexResult?.GetType().Name}");

            // ── PurchaseOrdersController.Print (also had Include Tenant) ──
            var printResult = await poCtrl.Print(po.Id);
            if (printResult is ViewResult) Ok("PurchaseOrders/Print");
            else No($"PurchaseOrders/Print returned {printResult?.GetType().Name}");

            // ── Simulate partial receive via POST ──
            var poItem = po.Items.First();
            var partialReceive = await poCtrl.Receive(
                po.Id,
                new[] { poItem.Id },
                new[] { 6m },
                Array.Empty<decimal>(),
                $"INV-{stamp}-1",
                "Partial delivery QA");
            if (partialReceive is RedirectToActionResult r1 && r1.ControllerName == "Receiving")
                Ok("PurchaseOrders/Receive POST — partial → Receiving/Details");
            else No($"Partial receive returned {partialReceive?.GetType().Name}");

            // ── ReceivingController.Details ──
            var receipt = await ctx.StockInHeaders.AsNoTracking()
                .Where(h => h.PurchaseOrderId == po.Id)
                .OrderByDescending(h => h.Id)
                .FirstOrDefaultAsync();
            if (receipt == null) { No("No receipt after partial receive"); }
            else
            {
                var recvCtrl = ActivatorUtilities.CreateInstance<ReceivingController>(sp);
                await PrimeControllerAsync(recvCtrl, httpContext, ctxProvider);
                var recvDetails = await recvCtrl.Details(receipt.Id);
                if (recvDetails is ViewResult) Ok("Receiving/Details");
                else No($"Receiving/Details returned {recvDetails?.GetType().Name}");

                var recvIndex = await recvCtrl.Index(searchTerm: prefix);
                if (recvIndex is ViewResult idx && ((ViewResult)idx).Model != null) Ok("Receiving/Index (search)");
                else No("Receiving/Index failed");
            }

            // ── Complete remaining delivery ──
            await PrimeControllerAsync(poCtrl, httpContext, ctxProvider);
            po = await ctx.PurchaseOrders.AsNoTracking().Include(p => p.Items).FirstAsync(p => p.Id == po.Id);
            poItem = po.Items.First();
            var finalReceive = await poCtrl.Receive(
                po.Id,
                new[] { poItem.Id },
                new[] { 4m },
                Array.Empty<decimal>(),
                $"INV-{stamp}-2",
                "Final delivery QA");
            if (finalReceive is RedirectToActionResult) Ok("PurchaseOrders/Receive POST — final delivery");
            else No($"Final receive returned {finalReceive?.GetType().Name}");

            // ── PO Details again (history + progress) ──
            await PrimeControllerAsync(poCtrl, httpContext, ctxProvider);
            var detailsAfter = await poCtrl.Details(po.Id);
            if (detailsAfter is ViewResult) Ok("PurchaseOrders/Details (after full receive + history)");
            else No("PO Details after full receive failed");

            Sep("Data verification");
            po = await ctx.PurchaseOrders.AsNoTracking().Include(p => p.Items).FirstAsync(p => p.Id == po.Id);
            if (po.Status == "Received") Ok("PO status = Received");
            else No($"PO status = {po.Status}");

            item = await ctx.Items.AsNoTracking().FirstAsync(i => i.Id == item.Id);
            if (item.CurrentStock == 10) Ok("Inventory = 10");
            else No($"Inventory = {item.CurrentStock}");

            var receiptCount = await ctx.StockInHeaders.CountAsync(h => h.PurchaseOrderId == po.Id);
            if (receiptCount == 2) Ok("2 StockIn receipts");
            else No($"Receipt count = {receiptCount}");

            var payableTotal = await ctx.StockInHeaders.Where(h => h.PurchaseOrderId == po.Id).SumAsync(h => h.TotalCost);
            if (payableTotal == 500) Ok("Payable total = 500");
            else No($"Payable total = {payableTotal}");

            Sep("Dashboard + Audit");
            var homeCtrl = ActivatorUtilities.CreateInstance<HomeController>(sp);
            await PrimeControllerAsync(homeCtrl, httpContext, ctxProvider);
            var dashResult = await homeCtrl.Index();
            if (dashResult is ViewResult) Ok("Home/Index (dashboard KPIs)");
            else No($"Dashboard returned {dashResult?.GetType().Name}");

            var auditCtrl = ActivatorUtilities.CreateInstance<AuditTrailController>(sp);
            await PrimeControllerAsync(auditCtrl, httpContext, ctxProvider);
            var auditResult = await auditCtrl.Index(searchTerm: prefix);
            if (auditResult is ViewResult) Ok("AuditTrail/Index (search)");
            else No($"AuditTrail returned {auditResult?.GetType().Name}");

            Sep("Dedicated DB isolation");
            if (tenant.RoutingEnabled && tenant.DatabaseMode == TenantDatabaseMode.Dedicated)
            {
                var sharedLeak = await appDb.PurchaseOrders.CountAsync(p => p.PONumber == $"{prefix}_PO");
                if (sharedLeak == 0) Ok("Zero shared-DB PO leakage");
                else No($"Shared leakage: {sharedLeak}");
            }

            sp.GetRequiredService<IHttpContextAccessor>().HttpContext = null;
            Console.WriteLine();
            Console.WriteLine($"[QA-PO-CT] Done — {pass} passed, {fail} failed.");
            Environment.ExitCode = fail > 0 ? 1 : 0;
        }

        private static async Task PrimeControllerAsync(
            Controller controller,
            HttpContext httpContext,
            ITenantOperationalContextProvider ctxProvider)
        {
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = httpContext,
                RouteData = new RouteData(),
                ActionDescriptor = new ControllerActionDescriptor()
            };

            if (controller is OperationalDbController opCtrl)
            {
                var ctx = await ctxProvider.GetContextAsync();
                var field = typeof(OperationalDbController).GetField(
                    "_context", BindingFlags.Instance | BindingFlags.NonPublic);
                field!.SetValue(opCtrl, ctx);
            }

            // Run the action filter so _context is set if not already
            var actionContext = new ActionExecutingContext(
                controller.ControllerContext,
                new List<IFilterMetadata>(),
                new Dictionary<string, object?>(),
                controller)
            {
                HttpContext = httpContext
            };

            var executed = false;
            ActionExecutionDelegate next = () =>
            {
                executed = true;
                return Task.FromResult(new ActionExecutedContext(
                    controller.ControllerContext,
                    new List<IFilterMetadata>(),
                    controller)
                {
                    Result = new EmptyResult()
                });
            };

            if (controller is OperationalDbController operational)
                await operational.OnActionExecutionAsync(actionContext, next);

            if (!executed && controller is OperationalDbController)
            {
                var ctx = await ctxProvider.GetContextAsync();
                typeof(OperationalDbController).GetField(
                    "_context", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(controller, ctx);
            }
        }

        private sealed class SessionFeature : ISessionFeature
        {
            public ISession Session { get; set; } = default!;
        }

        private sealed class QaTestSession : ISession
        {
            private readonly Dictionary<string, byte[]> _data = new();

            public bool IsAvailable => true;
            public string Id => "qa-session";
            public IEnumerable<string> Keys => _data.Keys;

            public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

            public void Clear() => _data.Clear();
            public void Remove(string key) => _data.Remove(key);
            public void Set(string key, byte[] value) => _data[key] = value;
            public bool TryGetValue(string key, out byte[] value) => _data.TryGetValue(key, out value!);
        }
    }
}

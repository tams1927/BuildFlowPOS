using HardwareManagementSystem.Controllers;
using HardwareManagementSystem.Data;
using HardwareManagementSystem.Models;
using HardwareManagementSystem.Services;
using HardwareManagementSystem.Services.TenantDatabases;
using HardwareManagementSystem.ViewModels;
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
    /// RC1.8.3 — Dedicated tenant runtime stability audit.
    /// Exercises MVC controller GET actions against TenantDbContext (click-through simulation).
    /// Development only: --qa-tenant-runtime-audit
    /// </summary>
    public static class TenantRuntimeAuditQaRunner
    {
        public static async Task RunAsync(WebApplication app)
        {
            if (!app.Environment.IsDevelopment())
            {
                Console.WriteLine("[QA-TRT] Development-only. Aborting.");
                return;
            }

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

            var httpContext = BuildHttpContext(sp, tenantUser);
            var ctx = await ctxProvider.GetContextAsync(tenant.Id);
            if (ctx is DbContext dbCtx)
                await dbCtx.Database.MigrateAsync();

            Sep($"Tenant {tenant.Name} (Id={tenant.Id}) — dedicated routed");

            // ── Ignored navigation guard: Include(Tenant) must throw on TenantDbContext ──
            try
            {
                await ctx.PurchaseOrders.AsNoTracking().Include(p => p.Tenant).FirstOrDefaultAsync();
                No("Include(Tenant) should throw on TenantDbContext");
            }
            catch (InvalidOperationException)
            {
                Ok("Include(Tenant) correctly invalid on TenantDbContext");
            }

            async Task Run(string label, Func<Task<IActionResult>> action)
            {
                try
                {
                    var result = await action();
                    if (result is ViewResult or RedirectToActionResult or RedirectResult
                        or FileContentResult or FileStreamResult or NotFoundResult or ForbidResult
                        or JsonResult or EmptyResult)
                        Ok(label);
                    else
                        No($"{label} — unexpected result: {result?.GetType().Name}");
                }
                catch (Exception ex)
                {
                    No($"{label} — {ex.GetType().Name}: {ex.Message}");
                }
            }

            async Task<TCtrl> Ctrl<TCtrl>() where TCtrl : Controller
            {
                var c = ActivatorUtilities.CreateInstance<TCtrl>(sp);
                await PrimeControllerAsync(c, httpContext, ctxProvider);
                return c;
            }

            // Resolve sample entity IDs
            int? itemId = await ctx.Items.Select(i => (int?)i.Id).FirstOrDefaultAsync();
            int? catId = await ctx.Categories.Select(c => (int?)c.Id).FirstOrDefaultAsync();
            int? unitId = await ctx.Units.Select(u => (int?)u.Id).FirstOrDefaultAsync();
            int? supplierId = await ctx.Suppliers.Select(s => (int?)s.Id).FirstOrDefaultAsync();
            int? customerId = await ctx.Customers.Select(c => (int?)c.Id).FirstOrDefaultAsync();
            int? poId = await ctx.PurchaseOrders.Select(p => (int?)p.Id).FirstOrDefaultAsync();
            int? receiptId = await ctx.StockInHeaders.Select(h => (int?)h.Id).FirstOrDefaultAsync();
            int? branchId = await ctx.Branches.Select(b => (int?)b.Id).FirstOrDefaultAsync();
            int? saleId = await ctx.SalesHeaders.Select(s => (int?)s.Id).FirstOrDefaultAsync();
            int? dmgId = await ctx.DamagedGoodsHeaders.Select(h => (int?)h.Id).FirstOrDefaultAsync();
            int? retId = await ctx.SupplierReturnHeaders.Select(h => (int?)h.Id).FirstOrDefaultAsync();
            int? adjId = await ctx.StockAdjustmentHeaders.Select(h => (int?)h.Id).FirstOrDefaultAsync();
            int? quotId = await ctx.Quotations.Select(q => (int?)q.Id).FirstOrDefaultAsync();
            int? drId = await ctx.DeliveryReceipts.Select(d => (int?)d.Id).FirstOrDefaultAsync();
            int? xferId = await ctx.BranchTransfers.Select(t => (int?)t.Id).FirstOrDefaultAsync();
            int? unpaidStockInId = await ctx.StockInHeaders
                .Where(h => h.BalanceDue > 0).Select(h => (int?)h.Id).FirstOrDefaultAsync();

            Sep("Dashboard & core");
            await Run("Home/Index", async () => await (await Ctrl<HomeController>()).Index());

            Sep("Products / Categories / Units");
            await Run("Products/Index", async () => await (await Ctrl<ProductsController>()).Index());
            if (itemId.HasValue)
                await Run("Products/GetConversions", async () => await (await Ctrl<ProductsController>()).GetConversions(itemId.Value));

            await Run("Categories/Index", async () => await (await Ctrl<CategoriesController>()).Index());
            await Run("Units/Index", async () => await (await Ctrl<UnitsController>()).Index());

            Sep("Suppliers / Customers");
            await Run("Suppliers/Index", async () => await (await Ctrl<SuppliersController>()).Index());
            if (supplierId.HasValue)
            {
                await Run("Suppliers/Statement", async () => await (await Ctrl<SuppliersController>()).Statement(supplierId.Value, null, null));
                await Run("Suppliers/StatementPdf", async () => await (await Ctrl<SuppliersController>()).StatementPdf(supplierId.Value, null, null));
            }

            await Run("Customers/Index", async () => await (await Ctrl<CustomersController>()).Index());
            if (customerId.HasValue)
            {
                await Run("Customers/Statement", async () => await (await Ctrl<CustomersController>()).Statement(customerId.Value, null, null));
                await Run("Customers/StatementPdf", async () => await (await Ctrl<CustomersController>()).StatementPdf(customerId.Value, null, null));
            }

            Sep("Purchasing / Receiving");
            await Run("PurchaseOrders/Index", async () => await (await Ctrl<PurchaseOrdersController>()).Index());
            if (poId.HasValue)
            {
                await Run("PurchaseOrders/Details", async () => await (await Ctrl<PurchaseOrdersController>()).Details(poId.Value));
                await Run("PurchaseOrders/Print", async () => await (await Ctrl<PurchaseOrdersController>()).Print(poId.Value));
                await Run("PurchaseOrders/DownloadPdf", async () => await (await Ctrl<PurchaseOrdersController>()).DownloadPdf(poId.Value));
                var po = await ctx.PurchaseOrders.AsNoTracking().FirstAsync(p => p.Id == poId);
                if (po.Status is "Sent" or "PartiallyReceived")
                    await Run("PurchaseOrders/Receive GET", async () => await (await Ctrl<PurchaseOrdersController>()).Receive(poId.Value));
            }

            await Run("Receiving/Index", async () => await (await Ctrl<ReceivingController>()).Index());
            if (receiptId.HasValue)
                await Run("Receiving/Details", async () => await (await Ctrl<ReceivingController>()).Details(receiptId.Value));

            await Run("StockIn/Index", async () => await (await Ctrl<StockInController>()).Index());

            Sep("Inventory operations");
            await Run("Inventory/Index", async () => await (await Ctrl<InventoryController>()).Index());
            await Run("InventoryMovement/Index", async () => await (await Ctrl<InventoryMovementController>()).Index());
            await Run("StockAdjustment/Index", async () => await (await Ctrl<StockAdjustmentController>()).Index());
            if (adjId.HasValue)
                await Run("StockAdjustment/PrintSlip", async () => await (await Ctrl<StockAdjustmentController>()).PrintSlip(adjId.Value));

            await Run("DamagedGoods/Index", async () => await (await Ctrl<DamagedGoodsController>()).Index());
            await Run("DamagedGoods/Create GET", async () => await (await Ctrl<DamagedGoodsController>()).Create());
            if (dmgId.HasValue)
                await Run("DamagedGoods/Details", async () => await (await Ctrl<DamagedGoodsController>()).Details(dmgId.Value));

            await Run("SupplierReturns/Index", async () => await (await Ctrl<SupplierReturnsController>()).Index());
            await Run("SupplierReturns/Create GET", async () => await (await Ctrl<SupplierReturnsController>()).Create(null));
            if (retId.HasValue)
                await Run("SupplierReturns/Details", async () => await (await Ctrl<SupplierReturnsController>()).Details(retId.Value));

            Sep("Sales / POS / Collections");
            await Run("Sales/Index", async () => await (await Ctrl<SalesController>()).Index());
            if (saleId.HasValue)
                await Run("Sales/Receipt", async () => await (await Ctrl<SalesController>()).Receipt(saleId.Value));
            await Run("POS/Index", async () => await (await Ctrl<POSController>()).Index());
            await Run("SalesReturn/Index", async () => await (await Ctrl<SalesReturnController>()).Index());
            await Run("CustomerCollections/Index", async () => await (await Ctrl<CustomerCollectionsController>()).Index(null));

            Sep("Payments / Quotes / Delivery");
            if (unpaidStockInId.HasValue)
                await Run("SupplierPayments/Pay GET", async () => await (await Ctrl<SupplierPaymentsController>()).Pay(unpaidStockInId.Value));
            await Run("Quotations/Index", async () => await (await Ctrl<QuotationsController>()).Index());
            if (quotId.HasValue)
                await Run("Quotations/Print", async () => await (await Ctrl<QuotationsController>()).Print(quotId.Value));
            await Run("DeliveryReceipts/Index", async () => await (await Ctrl<DeliveryReceiptsController>()).Index());

            Sep("Branches / Transfers / Expenses");
            await Run("Branches/Index", async () => await (await Ctrl<BranchesController>()).Index());
            if (branchId.HasValue)
                await Run("Branches/Details", async () => await (await Ctrl<BranchesController>()).Details(branchId.Value));
            await Run("BranchTransfers/Index", async () => await (await Ctrl<BranchTransfersController>()).Index());
            if (xferId.HasValue)
                await Run("BranchTransfers/PrintSlip", async () => await (await Ctrl<BranchTransfersController>()).PrintSlip(xferId.Value));
            await Run("Expenses/Index", async () => await (await Ctrl<ExpensesController>()).Index());

            Sep("Reports / Settings / Admin");
            await Run("Reports/Index", async () => await (await Ctrl<ReportsController>()).Index(null, null, null));
            await Run("Reports/SupplierPayables", async () => await (await Ctrl<ReportsController>()).SupplierPayables(null, null, null));
            await Run("Reports/InventoryStatus", async () => await (await Ctrl<ReportsController>()).InventoryStatus());
            await Run("Reports/CustomerBalance", async () => await (await Ctrl<ReportsController>()).CustomerBalance());
            await Run("Settings/Index", async () => await (await Ctrl<SettingsController>()).Index());
            await Run("AuditTrail/Index", async () => await (await Ctrl<AuditTrailController>()).Index());
            await Run("Import/History", async () => await (await Ctrl<ImportController>()).History());

            Sep("Users / Account");
            var usersCtrl = ActivatorUtilities.CreateInstance<UsersController>(sp);
            usersCtrl.ControllerContext = new ControllerContext { HttpContext = httpContext };
            await Run("Users/Index", async () => await usersCtrl.Index());

            var acctCtrl = ActivatorUtilities.CreateInstance<AccountController>(sp);
            acctCtrl.ControllerContext = new ControllerContext { HttpContext = httpContext };
            await Run("Account/ChangePassword GET", async () => await acctCtrl.ChangePassword());

            Sep("Dedicated DB isolation");
            if (tenant.RoutingEnabled && tenant.DatabaseMode == TenantDatabaseMode.Dedicated)
            {
                var ctxType = (await ctxProvider.GetContextAsync(tenant.Id)).GetType().Name;
                if (ctxType == "TenantDbContext") Ok($"Operational context = {ctxType}");
                else No($"Expected TenantDbContext, got {ctxType}");
            }

            sp.GetRequiredService<IHttpContextAccessor>().HttpContext = null;
            Console.WriteLine();
            Console.WriteLine($"[QA-TRT] Done — {pass} passed, {fail} failed.");
            Environment.ExitCode = fail > 0 ? 1 : 0;
        }

        private static DefaultHttpContext BuildHttpContext(IServiceProvider sp, ApplicationUser user)
        {
            var identity = new ClaimsIdentity("QA");
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, user.Id));
            identity.AddClaim(new Claim(ClaimTypes.Name, user.UserName ?? user.Email ?? "qa-trt"));
            identity.AddClaim(new Claim(ClaimTypes.Role, "TenantAdmin"));

            var httpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity),
                RequestServices = sp
            };
            httpContext.Features.Set<ISessionFeature>(new SessionFeature { Session = new QaTestSession() });
            sp.GetRequiredService<IHttpContextAccessor>().HttpContext = httpContext;
            return httpContext;
        }

        private static async Task PrimeControllerAsync(
            Controller controller, HttpContext httpContext, ITenantOperationalContextProvider ctxProvider)
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
                typeof(OperationalDbController).GetField("_context", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(opCtrl, ctx);
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

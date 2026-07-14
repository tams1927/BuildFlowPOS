using HardwareManagementSystem.Constants;
using HardwareManagementSystem.Data;
using HardwareManagementSystem.Models;
using HardwareManagementSystem.Services;
using HardwareManagementSystem.Services.TenantDatabases;
using HardwareManagementSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace HardwareManagementSystem.Controllers
{
    [Authorize]
    [PermissionAuthorize("Dashboard", "View")]
    public class HomeController : OperationalDbController
    {
        private readonly BranchService _branchService;
        private readonly AuditService _auditService;
        private readonly TenantLimitGuard _limitGuard;
        private readonly ITenantContext _tenantContext;
        private readonly ILogger<HomeController> _logger;

        public HomeController(
            ITenantOperationalContextProvider ctxProvider,
            BranchService branchService,
            AuditService auditService,
            TenantLimitGuard limitGuard,
            ITenantContext tenantContext,
            ILogger<HomeController> logger)
            : base(ctxProvider)
        {
            _branchService = branchService;
            _auditService = auditService;
            _limitGuard = limitGuard;
            _tenantContext = tenantContext;
            _logger = logger;
        }

        public async Task<IActionResult> Index()
        {
            // SuperAdmin has no store to manage — send straight to the platform dashboard
            if (User.IsInRole("SuperAdmin"))
                return RedirectToAction("Dashboard", "SuperAdmin");

            // Subscription usage widget for TenantAdmin
            if (User.IsInRole("TenantAdmin") && _tenantContext.CurrentTenantId.HasValue)
            {
                ViewBag.SubscriptionUsage = await _limitGuard.GetUsageAsync(_tenantContext.CurrentTenantId.Value);
            }

            var today     = DateTime.Today;
            var tomorrow  = today.AddDays(1);
            var monthStart = new DateTime(today.Year, today.Month, 1);

            var currentBranch = await _branchService.GetCurrentBranchAsync(User);
            ViewBag.CurrentBranch = currentBranch;

            if (_tenantContext.CurrentTenantId.HasValue)
            {
                var tenantId = _tenantContext.CurrentTenantId.Value;
                ViewBag.ShowSetupGuide = !await _context.Branches
                    .AsNoTracking()
                    .AnyAsync(b => b.TenantId == tenantId && b.IsActive);
            }

            // ── Sales KPIs ────────────────────────────────────────────
            var salesQuery = _context.SalesHeaders
                .AsNoTracking()
                .Where(s => s.Status == AppStatuses.Completed);

            if (currentBranch != null)
                salesQuery = salesQuery.Where(s => s.BranchId == currentBranch.Id);

            ViewBag.TodaySales = await salesQuery
                .Where(s => s.SalesDate >= today && s.SalesDate < tomorrow)
                .SumAsync(s => s.TotalAmount);

            ViewBag.MonthlySales = await salesQuery
                .Where(s => s.SalesDate >= monthStart)
                .SumAsync(s => s.TotalAmount);

            ViewBag.TodayTransactions = await salesQuery
                .CountAsync(s => s.SalesDate >= today && s.SalesDate < tomorrow);

            // ── Inventory KPIs (server-side — no full load) ───────────
            if (currentBranch != null)
            {
                var bpsQuery = _context.BranchProductStocks
                    .AsNoTracking()
                    .Where(s => s.BranchId == currentBranch.Id &&
                                s.Product != null &&
                                s.Product.Status == AppStatuses.Active);

                ViewBag.InventoryValue = await bpsQuery
                    .SumAsync(s => s.Quantity * s.Product!.CostPrice);

                ViewBag.TotalItems = await bpsQuery
                    .CountAsync();

                ViewBag.LowStockCount = await bpsQuery
                    .CountAsync(s => s.Quantity > 0 &&
                                    s.Quantity <= (s.ReorderLevel ?? s.Product!.ReorderLevel));

                ViewBag.OutOfStockCount = await bpsQuery
                    .CountAsync(s => s.Quantity <= 0);

                // Low-stock list: limited fetch with only the columns needed by the view
                ViewBag.LowStockItems = await _context.BranchProductStocks
                    .AsNoTracking()
                    .Include(s => s.Product)
                        .ThenInclude(p => p!.Category)
                    .Include(s => s.Product)
                        .ThenInclude(p => p!.Unit)
                    .Where(s => s.BranchId == currentBranch.Id &&
                                s.Product != null &&
                                s.Product.Status == AppStatuses.Active &&
                                s.Quantity <= (s.ReorderLevel ?? s.Product.ReorderLevel))
                    .OrderBy(s => s.Quantity)
                    .Take(5)
                    .Select(s => s.Product)
                    .ToListAsync();
            }
            else
            {
                ViewBag.InventoryValue = await _context.Items
                    .AsNoTracking()
                    .Where(i => i.Status == AppStatuses.Active)
                    .SumAsync(i => i.CurrentStock * i.CostPrice);

                ViewBag.TotalItems = await _context.Items
                    .AsNoTracking()
                    .CountAsync(i => i.Status == AppStatuses.Active);

                ViewBag.LowStockCount = await _context.Items
                    .AsNoTracking()
                    .CountAsync(i => i.Status == AppStatuses.Active &&
                                     i.CurrentStock > 0 &&
                                     i.CurrentStock <= i.ReorderLevel);

                ViewBag.OutOfStockCount = await _context.Items
                    .AsNoTracking()
                    .CountAsync(i => i.Status == AppStatuses.Active && i.CurrentStock <= 0);

                ViewBag.LowStockItems = await _context.Items
                    .AsNoTracking()
                    .Include(i => i.Category)
                    .Include(i => i.Unit)
                    .Where(i => i.Status == AppStatuses.Active && i.CurrentStock <= i.ReorderLevel)
                    .OrderBy(i => i.CurrentStock)
                    .Take(5)
                    .ToListAsync();
            }

            // ── Recent Sales ──────────────────────────────────────────
            var recentSalesQuery = _context.SalesHeaders
                .AsNoTracking()
                .Include(s => s.Customer)
                .Where(s => s.Status == AppStatuses.Completed);

            if (currentBranch != null)
                recentSalesQuery = recentSalesQuery.Where(s => s.BranchId == currentBranch.Id);

            ViewBag.RecentSales = await recentSalesQuery
                .OrderByDescending(s => s.SalesDate)
                .Take(5)
                .ToListAsync();

            // ── Today Expenses ────────────────────────────────────────
            var expenseQuery = _context.Expenses
                .AsNoTracking()
                .Where(e => e.ExpenseDate >= today && e.ExpenseDate < tomorrow);

            if (currentBranch != null)
                expenseQuery = expenseQuery.Where(e => e.BranchId == currentBranch.Id);

            ViewBag.TodayExpenses = await expenseQuery.SumAsync(e => e.Amount);

            // ── Receivables — net outstanding (debit − credit across all customers) ──
            var receivablesQuery = _context.CustomerLedgers.AsNoTracking().AsQueryable();
            var totalDebit  = await receivablesQuery.SumAsync(l => (decimal?)l.DebitAmount  ?? 0m);
            var totalCredit = await receivablesQuery.SumAsync(l => (decimal?)l.CreditAmount ?? 0m);
            ViewBag.TotalReceivables = Math.Max(0, totalDebit - totalCredit);

            // ── Payables — outstanding stock-in balance ───────────────
            var payablesQuery = _context.StockInHeaders
                .AsNoTracking()
                .Where(s => s.PaymentStatus != "Paid");

            if (currentBranch != null)
                payablesQuery = payablesQuery.Where(s => s.BranchId == currentBranch.Id);

            ViewBag.TotalPayables = await payablesQuery.SumAsync(s => (decimal?)s.BalanceDue ?? 0m);

            // ── Overdue AR / AP ───────────────────────────────────────
            var overdueDate = today.AddDays(-30);

            // Overdue receivables: customers with charges older than 30 days that have outstanding balance
            var overdueReceivablesCustomerIds = await _context.CustomerLedgers.AsNoTracking()
                .Where(l => l.TransactionType == "CHARGE" &&
                            l.TransactionDate.Date <= overdueDate)
                .Select(l => l.CustomerId)
                .Distinct()
                .ToListAsync();

            // Cross-check: only count those with positive balance
            var overdueARCount = 0;
            if (overdueReceivablesCustomerIds.Any())
            {
                overdueARCount = await _context.CustomerLedgers.AsNoTracking()
                    .Where(l => overdueReceivablesCustomerIds.Contains(l.CustomerId))
                    .GroupBy(l => l.CustomerId)
                    .CountAsync(g => g.OrderByDescending(x => x.Id).First().RunningBalance > 0);
            }

            // Overdue payables: unpaid stock-ins older than 30 days
            var overdueAPQuery = _context.StockInHeaders.AsNoTracking()
                .Where(s => s.PaymentStatus != "Paid" &&
                            s.DateReceived.Date <= overdueDate &&
                            s.BalanceDue > 0);

            if (_tenantContext.CurrentTenantId.HasValue && !_tenantContext.IsGlobalUser)
                overdueAPQuery = overdueAPQuery.Where(s => s.TenantId == _tenantContext.CurrentTenantId || s.TenantId == null);

            if (currentBranch != null)
                overdueAPQuery = overdueAPQuery.Where(s => s.BranchId == currentBranch.Id || s.BranchId == null);

            ViewBag.OverdueARCount = overdueARCount;
            ViewBag.OverdueAPAmount = await overdueAPQuery.SumAsync(s => (decimal?)s.BalanceDue) ?? 0m;

            // ── Purchase Order KPIs ───────────────────────────────────
            var poQuery = _context.PurchaseOrders.AsNoTracking();

            if (_tenantContext.CurrentTenantId.HasValue && !_tenantContext.IsGlobalUser)
                poQuery = poQuery.Where(po => po.TenantId == _tenantContext.CurrentTenantId || po.TenantId == null);

            if (currentBranch != null)
                poQuery = poQuery.Where(po => po.BranchId == currentBranch.Id || po.BranchId == null);

            ViewBag.PendingPOCount             = await poQuery.CountAsync(po => po.Status == "Draft");
            ViewBag.SentPOCount                = await poQuery.CountAsync(po => po.Status == "Sent");
            ViewBag.PartiallyReceivedPOCount   = await poQuery.CountAsync(po => po.Status == "PartiallyReceived");

            // ── RC1.8.1 Warehouse / Receiving KPIs ────────────────────────
            var receiptQuery = _context.StockInHeaders.AsNoTracking();
            if (_tenantContext.CurrentTenantId.HasValue && !_tenantContext.IsGlobalUser)
                receiptQuery = receiptQuery.Where(h => h.TenantId == _tenantContext.CurrentTenantId || h.TenantId == null);
            if (currentBranch != null)
                receiptQuery = receiptQuery.Where(h => h.BranchId == currentBranch.Id || h.BranchId == null);

            ViewBag.TodaysReceiptsCount = await receiptQuery
                .CountAsync(h => h.DateReceived >= today && h.DateReceived < tomorrow);

            ViewBag.PendingDeliveriesCount = await poQuery
                .CountAsync(po => po.Status == "Sent" || po.Status == "PartiallyReceived");

            ViewBag.CompletedPOsTodayCount = await poQuery
                .CountAsync(po => po.Status == "Received" &&
                                  po.UpdatedAtUtc.HasValue &&
                                  po.UpdatedAtUtc.Value >= DateTime.UtcNow.Date &&
                                  po.UpdatedAtUtc.Value < DateTime.UtcNow.Date.AddDays(1));

            ViewBag.SupplierPayablesDueCount = await receiptQuery
                .CountAsync(h => h.PaymentStatus != "Paid" && h.BalanceDue > 0);

            // Pending deliveries widget (max 5, newest first)
            var pendingPoList = await poQuery
                .AsNoTracking()
                .Include(po => po.Supplier)
                .Include(po => po.Items)
                    .ThenInclude(i => i.OrderedUnit)
                .Include(po => po.Items)
                    .ThenInclude(i => i.Item)
                        .ThenInclude(i => i!.Unit)
                .Where(po => po.Status == "Sent" || po.Status == "PartiallyReceived")
                .OrderByDescending(po => po.PODate)
                .ThenByDescending(po => po.Id)
                .Take(5)
                .ToListAsync();

            ViewBag.PendingDeliveries = pendingPoList.Select(po =>
            {
                var remaining = po.Items.Sum(i => i.QuantityRemaining);
                var unitLabel = po.Items.Count == 1
                    ? po.Items.First().OrderedUnit?.ShortName
                      ?? po.Items.First().Item?.Unit?.ShortName
                      ?? "units"
                    : "units";
                return new PendingDeliveryVm
                {
                    PurchaseOrderId = po.Id,
                    PONumber = po.PONumber,
                    SupplierName = po.Supplier?.SupplierName ?? "—",
                    RemainingQty = remaining,
                    UnitLabel = unitLabel,
                    Status = po.Status,
                    PODate = po.PODate
                };
            }).ToList();

            // ── Inventory Intelligence: Critical / Low stock items ────────
            var criticalStockCount = 0;
            var lowStockReorderCount = 0;

            if (currentBranch != null)
            {
                criticalStockCount = await _context.BranchProductStocks.AsNoTracking()
                    .CountAsync(s => s.BranchId == currentBranch.Id &&
                                     s.Product != null &&
                                     s.Product.Status == "Active" &&
                                     s.Quantity <= 0);

                lowStockReorderCount = await _context.BranchProductStocks.AsNoTracking()
                    .CountAsync(s => s.BranchId == currentBranch.Id &&
                                     s.Product != null &&
                                     s.Product.Status == "Active" &&
                                     s.Quantity > 0 &&
                                     s.Quantity <= (s.ReorderLevel ?? s.Product.ReorderLevel));
            }
            else
            {
                var itemsBase = _context.Items.AsNoTracking().Where(i => i.Status == "Active");
                if (_tenantContext.CurrentTenantId.HasValue && !_tenantContext.IsGlobalUser)
                    itemsBase = itemsBase.Where(i => i.TenantId == _tenantContext.CurrentTenantId || i.TenantId == null);

                criticalStockCount   = await itemsBase.CountAsync(i => i.CurrentStock <= 0);
                lowStockReorderCount = await itemsBase.CountAsync(i => i.CurrentStock > 0 && i.CurrentStock <= i.ReorderLevel);
            }

            ViewBag.CriticalStockCount = criticalStockCount;
            ViewBag.ReorderStockCount  = lowStockReorderCount;

            // ── Inventory Intelligence KPIs ────────────────────────────
            var deadStockCutoff = DateTime.Today.AddDays(-90);

            // For tenant-scoped items
            var invItemsQuery = _context.Items.AsNoTracking()
                .Where(i => i.Status == "Active");

            if (_tenantContext.CurrentTenantId.HasValue && !_tenantContext.IsGlobalUser)
                invItemsQuery = invItemsQuery.Where(i =>
                    i.TenantId == _tenantContext.CurrentTenantId || i.TenantId == null);

            var totalInvItems = await invItemsQuery.CountAsync();

            // Total inventory value
            var totalInventoryValue = await invItemsQuery
                .SumAsync(i => (decimal?)(i.CurrentStock * i.CostPrice)) ?? 0m;

            // Overstock: items with stock > 2× reorder level AND reorder level > 0
            var overstockCount = await invItemsQuery
                .CountAsync(i => i.ReorderLevel > 0 && i.CurrentStock > i.ReorderLevel * 2);

            // Dead stock: items with stock > 0 and no sale in 90 days
            var recentSaleItemIds = await _context.SalesDetails.AsNoTracking()
                .Where(d => d.SalesHeader != null &&
                            d.SalesHeader.Status == "Completed" &&
                            d.SalesHeader.SalesDate >= deadStockCutoff)
                .Select(d => d.ItemId)
                .Distinct()
                .ToListAsync();

            var deadStockCount = await invItemsQuery
                .CountAsync(i => i.CurrentStock > 0 && !recentSaleItemIds.Contains(i.Id));

            // Fast/slow moving (last 30 days)
            var thirtyDaysAgo = DateTime.Today.AddDays(-30);
            var activeItemIds = await invItemsQuery.Select(i => i.Id).ToListAsync();

            var soldItemIds30 = await _context.SalesDetails.AsNoTracking()
                .Where(d => d.SalesHeader != null &&
                            d.SalesHeader.Status == "Completed" &&
                            d.SalesHeader.SalesDate >= thirtyDaysAgo &&
                            activeItemIds.Contains(d.ItemId))
                .Select(d => d.ItemId)
                .Distinct()
                .CountAsync();

            var fastMovingCount = soldItemIds30;
            var slowMovingCount = await invItemsQuery
                .CountAsync(i => i.CurrentStock > 0 && !recentSaleItemIds.Contains(i.Id));

            // Health score
            var healthScore = HardwareManagementSystem.Controllers.ReportsController.ComputeHealthScore(
                totalInvItems, lowStockReorderCount, criticalStockCount, deadStockCount, overstockCount);

            // Top 10 fast moving (last 30 days)
            var top10Fast = await _context.SalesDetails.AsNoTracking()
                .Where(d => d.SalesHeader != null &&
                            d.SalesHeader.Status == "Completed" &&
                            d.SalesHeader.SalesDate >= thirtyDaysAgo)
                .GroupBy(d => new { d.ItemId, d.Item!.ItemCode, d.Item.ItemName })
                .Select(g => new { g.Key.ItemCode, g.Key.ItemName, QtySold = g.Sum(x => x.Quantity) })
                .OrderByDescending(x => x.QtySold)
                .Take(10)
                .ToListAsync();

            // Top 10 low stock (currently)
            var top10LowStock = await invItemsQuery
                .Where(i => i.CurrentStock <= i.ReorderLevel && i.CurrentStock >= 0)
                .OrderBy(i => i.CurrentStock)
                .Take(10)
                .Select(i => new { i.ItemCode, i.ItemName, i.CurrentStock, i.ReorderLevel })
                .ToListAsync();

            // Top 10 highest inventory value
            var top10HighValue = await invItemsQuery
                .Where(i => i.CurrentStock > 0)
                .OrderByDescending(i => i.CurrentStock * i.CostPrice)
                .Take(10)
                .Select(i => new { i.ItemCode, i.ItemName, i.CurrentStock, i.CostPrice, Value = i.CurrentStock * i.CostPrice })
                .ToListAsync();

            ViewBag.TotalInventoryValue = totalInventoryValue;
            ViewBag.FastMovingCount     = fastMovingCount;
            ViewBag.SlowMovingCount     = slowMovingCount;
            ViewBag.DeadStockCount      = deadStockCount;
            ViewBag.OverstockCount      = overstockCount;
            ViewBag.InventoryHealthScore = healthScore;
            ViewBag.Top10FastMoving     = top10Fast;
            ViewBag.Top10LowStock       = top10LowStock;
            ViewBag.Top10HighValue      = top10HighValue;

            // ── Pending Transfers ─────────────────────────────────────
            var pendingTransfersQuery = _context.BranchTransfers
                .AsNoTracking()
                .Where(t => t.Status == AppStatuses.Pending || t.Status == AppStatuses.Approved);

            if (currentBranch != null)
            {
                pendingTransfersQuery = pendingTransfersQuery
                    .Where(t => t.FromBranchId == currentBranch.Id ||
                                t.ToBranchId == currentBranch.Id);
            }

            ViewBag.PendingTransfers = await pendingTransfersQuery.CountAsync();

            // ── Recent Transfers ──────────────────────────────────────
            var recentTransfersQuery = _context.BranchTransfers
                .AsNoTracking()
                .Include(t => t.FromBranch)
                .Include(t => t.ToBranch)
                .OrderByDescending(t => t.CreatedAtUtc);

            if (currentBranch != null)
            {
                ViewBag.RecentTransfers = await recentTransfersQuery
                    .Where(t => t.FromBranchId == currentBranch.Id ||
                                t.ToBranchId == currentBranch.Id)
                    .Take(5)
                    .ToListAsync();
            }
            else
            {
                ViewBag.RecentTransfers = await recentTransfersQuery
                    .Take(5)
                    .ToListAsync();
            }

            return View();
        }

        [AllowAnonymous]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public async Task<IActionResult> Error()
        {
            var requestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;

            var exceptionFeature = HttpContext.Features.Get<IExceptionHandlerPathFeature>();

            if (exceptionFeature?.Error != null)
            {
                _logger.LogError(
                    exceptionFeature.Error,
                    "Unhandled exception on {Path}",
                    exceptionFeature.Path);

                if (User.Identity?.IsAuthenticated == true)
                {
                    await _auditService.LogAsync(
                        User,
                        "System",
                        "UNHANDLED EXCEPTION",
                        $"Unhandled exception on path {exceptionFeature.Path}. Error: {exceptionFeature.Error.Message}",
                        "Exception",
                        requestId,
                        HttpContext.Connection.RemoteIpAddress?.ToString()
                    );
                }
            }

            return View(new ErrorViewModel { RequestId = requestId });
        }
    }
}

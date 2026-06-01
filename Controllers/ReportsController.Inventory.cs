using HardwareManagementSystem.Models;
using HardwareManagementSystem.Services;
using HardwareManagementSystem.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HardwareManagementSystem.Controllers
{
    public partial class ReportsController
    {
        // ================================================================
        //  FAST MOVING ITEMS
        // ================================================================

        [PermissionAuthorize("FastMovingItems", "View")]
        public async Task<IActionResult> FastMovingItems(
            DateTime? dateFrom,
            DateTime? dateTo,
            int? branchId,
            int? categoryId)
        {
            var tenantId   = await GetReportTenantIdAsync();
            var isGlobal   = _branchService.IsGlobalUser(User);
            var allBranches = await _branchService.GetAllActiveBranchesAsync();
            var curBranch  = await _branchService.GetCurrentBranchAsync(User);
            int? effBranch = isGlobal ? (branchId > 0 ? branchId : null) : curBranch?.Id;

            var from = dateFrom?.Date ?? DateTime.Today.AddDays(-30);
            var to   = dateTo?.Date   ?? DateTime.Today;
            var toEx = to.AddDays(1);

            var rows = await BuildFastMovingRows(tenantId, effBranch, categoryId, from, toEx);
            rows = rows.OrderByDescending(r => r.QtySold).ToList();

            await _auditService.LogAsync(User, "FastMovingItems", "FAST_MOVING_REPORT_VIEWED",
                $"Fast Moving report viewed: {from:yyyy-MM-dd} to {to:yyyy-MM-dd}, Branch:{effBranch}",
                "Report", null, HttpContext.Connection.RemoteIpAddress?.ToString());

            LoadInventoryViewBag(allBranches, effBranch, categoryId, tenantId);
            return View(new FastMovingItemsViewModel
            {
                DateFrom = from, DateTo = to,
                BranchId = effBranch,
                BranchName = allBranches.FirstOrDefault(b => b.Id == effBranch)?.Name,
                CategoryId = categoryId,
                Rows = rows
            });
        }

        [PermissionAuthorize("FastMovingItems", "Print")]
        public async Task<IActionResult> FastMovingItemsPdf(
            DateTime? dateFrom, DateTime? dateTo, int? branchId, int? categoryId)
        {
            var tenantId  = await GetReportTenantIdAsync();
            var isGlobal  = _branchService.IsGlobalUser(User);
            var curBranch = await _branchService.GetCurrentBranchAsync(User);
            int? effBranch = isGlobal ? (branchId > 0 ? branchId : null) : curBranch?.Id;

            var from = dateFrom?.Date ?? DateTime.Today.AddDays(-30);
            var to   = dateTo?.Date   ?? DateTime.Today;
            var rows = await BuildFastMovingRows(tenantId, effBranch, categoryId, from, to.AddDays(1));
            rows = rows.OrderByDescending(r => r.QtySold).ToList();

            var settings  = await GetSettingsAsync(tenantId);
            var tenant    = await GetTenantAsync(tenantId);
            var allBranches = await _branchService.GetAllActiveBranchesAsync();
            var branchName  = allBranches.FirstOrDefault(b => b.Id == effBranch)?.Name;
            var currency    = settings?.CurrencySymbol ?? "₱";

            var headers = new List<string> { "SKU", "Item Name", "Category", "Unit", "Qty Sold", "Revenue", "Current Stock" };
            var tableRows = rows.Select(r => new List<string>
            {
                r.ItemCode, r.ItemName, r.Category, r.Unit,
                r.QtySold.ToString("N3"), $"{currency}{r.Revenue:N2}", r.CurrentStock.ToString("N3")
            }).ToList();

            var bytes = _reportPdfService.GenerateSimpleReportPdf(
                "Fast Moving Items Report",
                $"{from:MMM dd, yyyy} — {to:MMM dd, yyyy}",
                headers, tableRows,
                tenant?.Name ?? settings?.BusinessName,
                branchName);

            return File(bytes, "application/pdf", $"FastMoving-{from:yyyyMMdd}-{to:yyyyMMdd}.pdf");
        }

        // ================================================================
        //  SLOW MOVING ITEMS
        // ================================================================

        [PermissionAuthorize("SlowMovingItems", "View")]
        public async Task<IActionResult> SlowMovingItems(
            int? branchId, int? categoryId, int thresholdDays = 90)
        {
            var tenantId   = await GetReportTenantIdAsync();
            var isGlobal   = _branchService.IsGlobalUser(User);
            var allBranches = await _branchService.GetAllActiveBranchesAsync();
            var curBranch  = await _branchService.GetCurrentBranchAsync(User);
            int? effBranch = isGlobal ? (branchId > 0 ? branchId : null) : curBranch?.Id;

            var rows = await BuildSlowMovingRows(tenantId, effBranch, categoryId, thresholdDays);

            await _auditService.LogAsync(User, "SlowMovingItems", "SLOW_MOVING_REPORT_VIEWED",
                $"Slow Moving report viewed. Threshold:{thresholdDays}d, Branch:{effBranch}",
                "Report", null, HttpContext.Connection.RemoteIpAddress?.ToString());

            LoadInventoryViewBag(allBranches, effBranch, categoryId, tenantId);
            ViewBag.ThresholdDays = thresholdDays;
            return View(new SlowMovingItemsViewModel
            {
                BranchId = effBranch,
                BranchName = allBranches.FirstOrDefault(b => b.Id == effBranch)?.Name,
                CategoryId = categoryId,
                ThresholdDays = thresholdDays,
                Rows = rows
            });
        }

        [PermissionAuthorize("SlowMovingItems", "Print")]
        public async Task<IActionResult> SlowMovingItemsPdf(int? branchId, int? categoryId, int thresholdDays = 90)
        {
            var tenantId  = await GetReportTenantIdAsync();
            var isGlobal  = _branchService.IsGlobalUser(User);
            var curBranch = await _branchService.GetCurrentBranchAsync(User);
            int? effBranch = isGlobal ? (branchId > 0 ? branchId : null) : curBranch?.Id;

            var rows     = await BuildSlowMovingRows(tenantId, effBranch, categoryId, thresholdDays);
            var settings = await GetSettingsAsync(tenantId);
            var tenant   = await GetTenantAsync(tenantId);
            var allBranches = await _branchService.GetAllActiveBranchesAsync();
            var branchName  = allBranches.FirstOrDefault(b => b.Id == effBranch)?.Name;
            var currency    = settings?.CurrencySymbol ?? "₱";

            var headers = new List<string> { "SKU", "Item Name", "Category", "Current Stock", "Inv. Value", "Qty Sold (Period)", "Last Sold", "Days Inactive" };
            var tableRows = rows.Select(r => new List<string>
            {
                r.ItemCode, r.ItemName, r.Category,
                r.CurrentStock.ToString("N3"),
                $"{currency}{r.InventoryValue:N2}",
                r.QtySold.ToString("N3"),
                r.LastSoldDate.HasValue ? r.LastSoldDate.Value.ToString("MM/dd/yyyy") : "Never",
                r.DaysSinceLastSale.ToString()
            }).ToList();

            var bytes = _reportPdfService.GenerateSimpleReportPdf(
                $"Slow Moving Items (last {thresholdDays} days)",
                $"As of {DateTime.Today:MMM dd, yyyy}",
                headers, tableRows,
                tenant?.Name ?? settings?.BusinessName, branchName);

            return File(bytes, "application/pdf", $"SlowMoving-{DateTime.Today:yyyyMMdd}.pdf");
        }

        // ================================================================
        //  DEAD STOCK
        // ================================================================

        [PermissionAuthorize("DeadStock", "View")]
        public async Task<IActionResult> DeadStock(
            int days = 90, int? branchId = null, int? categoryId = null)
        {
            var tenantId   = await GetReportTenantIdAsync();
            var isGlobal   = _branchService.IsGlobalUser(User);
            var allBranches = await _branchService.GetAllActiveBranchesAsync();
            var curBranch  = await _branchService.GetCurrentBranchAsync(User);
            int? effBranch = isGlobal ? (branchId > 0 ? branchId : null) : curBranch?.Id;

            var rows = await BuildDeadStockRows(tenantId, effBranch, categoryId, days);

            await _auditService.LogAsync(User, "DeadStock", "DEAD_STOCK_REPORT_VIEWED",
                $"Dead Stock report viewed. Days:{days}, Branch:{effBranch}",
                "Report", null, HttpContext.Connection.RemoteIpAddress?.ToString());

            LoadInventoryViewBag(allBranches, effBranch, categoryId, tenantId);
            ViewBag.Days = days;
            return View(new DeadStockViewModel
            {
                DaysThreshold = days, BranchId = effBranch,
                BranchName = allBranches.FirstOrDefault(b => b.Id == effBranch)?.Name,
                CategoryId = categoryId,
                Rows = rows
            });
        }

        [PermissionAuthorize("DeadStock", "Print")]
        public async Task<IActionResult> DeadStockPdf(int days = 90, int? branchId = null, int? categoryId = null)
        {
            var tenantId  = await GetReportTenantIdAsync();
            var isGlobal  = _branchService.IsGlobalUser(User);
            var curBranch = await _branchService.GetCurrentBranchAsync(User);
            int? effBranch = isGlobal ? (branchId > 0 ? branchId : null) : curBranch?.Id;

            var rows     = await BuildDeadStockRows(tenantId, effBranch, categoryId, days);
            var settings = await GetSettingsAsync(tenantId);
            var tenant   = await GetTenantAsync(tenantId);
            var allBranches = await _branchService.GetAllActiveBranchesAsync();
            var branchName  = allBranches.FirstOrDefault(b => b.Id == effBranch)?.Name;
            var currency    = settings?.CurrencySymbol ?? "₱";

            var headers = new List<string> { "SKU", "Item Name", "Category", "Current Stock", "Inv. Value", "Last Sale Date", "Days Inactive" };
            var tableRows = rows.Select(r => new List<string>
            {
                r.ItemCode, r.ItemName, r.Category,
                r.CurrentStock.ToString("N3"),
                $"{currency}{r.InventoryValue:N2}",
                r.LastSaleDate.HasValue ? r.LastSaleDate.Value.ToString("MM/dd/yyyy") : "Never",
                r.DaysSinceLastSale.ToString()
            }).ToList();

            var bytes = _reportPdfService.GenerateSimpleReportPdf(
                $"Dead Stock Report ({days}+ days)",
                $"As of {DateTime.Today:MMM dd, yyyy}",
                headers, tableRows,
                tenant?.Name ?? settings?.BusinessName, branchName);

            return File(bytes, "application/pdf", $"DeadStock-{DateTime.Today:yyyyMMdd}.pdf");
        }

        // ================================================================
        //  REORDER SUGGESTIONS
        // ================================================================

        [PermissionAuthorize("ReorderSuggestions", "View")]
        public async Task<IActionResult> ReorderSuggestions(int? branchId, int? categoryId)
        {
            var tenantId   = await GetReportTenantIdAsync();
            var isGlobal   = _branchService.IsGlobalUser(User);
            var allBranches = await _branchService.GetAllActiveBranchesAsync();
            var curBranch  = await _branchService.GetCurrentBranchAsync(User);
            int? effBranch = isGlobal ? (branchId > 0 ? branchId : null) : curBranch?.Id;

            var rows = await BuildReorderSuggestions(tenantId, effBranch, categoryId);

            await _auditService.LogAsync(User, "ReorderSuggestions", "REORDER_REPORT_VIEWED",
                $"Reorder Suggestions viewed. Branch:{effBranch}",
                "Report", null, HttpContext.Connection.RemoteIpAddress?.ToString());

            LoadInventoryViewBag(allBranches, effBranch, categoryId, tenantId);
            return View(new ReorderSuggestionsViewModel
            {
                BranchId = effBranch,
                BranchName = allBranches.FirstOrDefault(b => b.Id == effBranch)?.Name,
                CategoryId = categoryId,
                Rows = rows
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermissionAuthorize("PurchaseOrders", "Create")]
        public async Task<IActionResult> GeneratePOFromReorder(
            [FromForm] List<int> selectedItems,
            [FromForm] int? supplierId)
        {
            if (!selectedItems.Any())
            {
                TempData["ErrorMessage"] = "No items selected for purchase order.";
                return RedirectToAction(nameof(ReorderSuggestions));
            }

            var tenantId  = await _tenantGuard.GetEffectiveTenantIdAsync();
            var curBranch = await _branchService.GetCurrentBranchAsync(User);

            // Build query string for pre-populated PO Create with low-stock items
            var queryItems = string.Join("&", selectedItems.Select(id => $"itemIds={id}"));
            var redirectUrl = $"/PurchaseOrders/Create?fromReorder=1&{queryItems}";
            if (supplierId.HasValue)
                redirectUrl += $"&supplierId={supplierId}";

            await _auditService.LogAsync(User, "PurchaseOrders", "PURCHASE_ORDER_GENERATED_FROM_REORDER",
                $"PO generation initiated from Reorder Suggestions. Items: [{string.Join(",", selectedItems)}]",
                "PurchaseOrder", null, HttpContext.Connection.RemoteIpAddress?.ToString());

            TempData["SuccessMessage"] = $"Purchase Order pre-filled with {selectedItems.Count} items. Review and submit.";
            return Redirect(redirectUrl);
        }

        [PermissionAuthorize("ReorderSuggestions", "Print")]
        public async Task<IActionResult> ReorderSuggestionsPdf(int? branchId, int? categoryId)
        {
            var tenantId  = await GetReportTenantIdAsync();
            var isGlobal  = _branchService.IsGlobalUser(User);
            var curBranch = await _branchService.GetCurrentBranchAsync(User);
            int? effBranch = isGlobal ? (branchId > 0 ? branchId : null) : curBranch?.Id;

            var rows     = await BuildReorderSuggestions(tenantId, effBranch, categoryId);
            var settings = await GetSettingsAsync(tenantId);
            var tenant   = await GetTenantAsync(tenantId);
            var allBranches = await _branchService.GetAllActiveBranchesAsync();
            var branchName  = allBranches.FirstOrDefault(b => b.Id == effBranch)?.Name;
            var currency    = settings?.CurrencySymbol ?? "₱";

            var headers = new List<string> { "SKU", "Item Name", "Category", "Current Stock", "Reorder Level", "Suggested Qty", "Preferred Supplier", "Est. Cost" };
            var tableRows = rows.Select(r => new List<string>
            {
                r.ItemCode, r.ItemName, r.Category,
                r.CurrentStock.ToString("N3"),
                r.ReorderLevel.ToString("N3"),
                r.SuggestedQty.ToString("N3"),
                r.PreferredSupplier ?? "—",
                $"{currency}{(r.SuggestedQty * r.CostPrice):N2}"
            }).ToList();

            var bytes = _reportPdfService.GenerateSimpleReportPdf(
                "Reorder Suggestions",
                $"As of {DateTime.Today:MMM dd, yyyy}",
                headers, tableRows,
                tenant?.Name ?? settings?.BusinessName, branchName);

            return File(bytes, "application/pdf", $"ReorderSuggestions-{DateTime.Today:yyyyMMdd}.pdf");
        }

        // ================================================================
        //  STOCK AGING
        // ================================================================

        [PermissionAuthorize("StockAging", "View")]
        public async Task<IActionResult> StockAging(int? branchId, int? categoryId)
        {
            var tenantId   = await GetReportTenantIdAsync();
            var isGlobal   = _branchService.IsGlobalUser(User);
            var allBranches = await _branchService.GetAllActiveBranchesAsync();
            var curBranch  = await _branchService.GetCurrentBranchAsync(User);
            int? effBranch = isGlobal ? (branchId > 0 ? branchId : null) : curBranch?.Id;

            var rows = await BuildStockAgingRows(tenantId, effBranch, categoryId);

            await _auditService.LogAsync(User, "StockAging", "STOCK_AGING_REPORT_VIEWED",
                $"Stock Aging report viewed. Branch:{effBranch}",
                "Report", null, HttpContext.Connection.RemoteIpAddress?.ToString());

            LoadInventoryViewBag(allBranches, effBranch, categoryId, tenantId);
            return View(new StockAgingViewModel
            {
                BranchId = effBranch,
                BranchName = allBranches.FirstOrDefault(b => b.Id == effBranch)?.Name,
                CategoryId = categoryId,
                AsOfDate = DateTime.Today,
                Rows = rows
            });
        }

        [PermissionAuthorize("StockAging", "Print")]
        public async Task<IActionResult> StockAgingPdf(int? branchId, int? categoryId)
        {
            var tenantId  = await GetReportTenantIdAsync();
            var isGlobal  = _branchService.IsGlobalUser(User);
            var curBranch = await _branchService.GetCurrentBranchAsync(User);
            int? effBranch = isGlobal ? (branchId > 0 ? branchId : null) : curBranch?.Id;

            var rows     = await BuildStockAgingRows(tenantId, effBranch, categoryId);
            var settings = await GetSettingsAsync(tenantId);
            var tenant   = await GetTenantAsync(tenantId);
            var allBranches = await _branchService.GetAllActiveBranchesAsync();
            var branchName  = allBranches.FirstOrDefault(b => b.Id == effBranch)?.Name;
            var currency    = settings?.CurrencySymbol ?? "₱";

            var headers = new List<string> { "SKU", "Item Name", "Category", "0–30 Days Qty", "31–60 Qty", "61–90 Qty", "91–180 Qty", "180+ Qty", "Total Value" };
            var tableRows = rows.Select(r => new List<string>
            {
                r.ItemCode, r.ItemName, r.Category,
                r.Qty0_30.ToString("N3"), r.Qty31_60.ToString("N3"),
                r.Qty61_90.ToString("N3"), r.Qty91_180.ToString("N3"),
                r.Qty180Plus.ToString("N3"), $"{currency}{r.TotalValue:N2}"
            }).ToList();

            var bytes = _reportPdfService.GenerateSimpleReportPdf(
                "Stock Aging Report",
                $"As of {DateTime.Today:MMM dd, yyyy}",
                headers, tableRows,
                tenant?.Name ?? settings?.BusinessName, branchName);

            return File(bytes, "application/pdf", $"StockAging-{DateTime.Today:yyyyMMdd}.pdf");
        }

        // ================================================================
        //  INVENTORY VALUATION
        // ================================================================

        [PermissionAuthorize("InventoryValuation", "View")]
        public async Task<IActionResult> InventoryValuationReport(int? branchId, int? categoryId)
        {
            var tenantId   = await GetReportTenantIdAsync();
            var isGlobal   = _branchService.IsGlobalUser(User);
            var allBranches = await _branchService.GetAllActiveBranchesAsync();
            var curBranch  = await _branchService.GetCurrentBranchAsync(User);
            int? effBranch = isGlobal ? (branchId > 0 ? branchId : null) : curBranch?.Id;

            var vm = await BuildInventoryValuation(tenantId, effBranch, categoryId);

            await _auditService.LogAsync(User, "InventoryValuation", "INVENTORY_VALUATION_VIEWED",
                $"Inventory Valuation viewed. Branch:{effBranch}",
                "Report", null, HttpContext.Connection.RemoteIpAddress?.ToString());

            LoadInventoryViewBag(allBranches, effBranch, categoryId, tenantId);
            TempData["SuccessMessage"] ??= null;
            return View(vm);
        }

        [PermissionAuthorize("InventoryValuation", "Print")]
        public async Task<IActionResult> InventoryValuationPdf(int? branchId, int? categoryId)
        {
            var tenantId  = await GetReportTenantIdAsync();
            var isGlobal  = _branchService.IsGlobalUser(User);
            var curBranch = await _branchService.GetCurrentBranchAsync(User);
            int? effBranch = isGlobal ? (branchId > 0 ? branchId : null) : curBranch?.Id;

            var vm       = await BuildInventoryValuation(tenantId, effBranch, categoryId);
            var settings = await GetSettingsAsync(tenantId);
            var tenant   = await GetTenantAsync(tenantId);
            var allBranches = await _branchService.GetAllActiveBranchesAsync();
            var branchName  = allBranches.FirstOrDefault(b => b.Id == effBranch)?.Name;

            TempData["SuccessMessage"] = "Inventory valuation report generated.";

            var bytes = _reportPdfService.GenerateInventoryValuationPdf(vm, settings, tenant, settings?.LogoPath);
            return File(bytes, "application/pdf", $"InventoryValuation-{DateTime.Today:yyyyMMdd}.pdf");
        }

        // ================================================================
        //  ABC ANALYSIS
        // ================================================================

        [PermissionAuthorize("ABCAnalysis", "View")]
        public async Task<IActionResult> ABCAnalysis(
            DateTime? dateFrom, DateTime? dateTo, int? branchId)
        {
            var tenantId   = await GetReportTenantIdAsync();
            var isGlobal   = _branchService.IsGlobalUser(User);
            var allBranches = await _branchService.GetAllActiveBranchesAsync();
            var curBranch  = await _branchService.GetCurrentBranchAsync(User);
            int? effBranch = isGlobal ? (branchId > 0 ? branchId : null) : curBranch?.Id;

            var from = dateFrom?.Date ?? DateTime.Today.AddDays(-365);
            var to   = dateTo?.Date   ?? DateTime.Today;

            var vm = await BuildABCAnalysis(tenantId, effBranch, from, to.AddDays(1));
            vm.BranchId = effBranch;
            vm.BranchName = allBranches.FirstOrDefault(b => b.Id == effBranch)?.Name;

            await _auditService.LogAsync(User, "ABCAnalysis", "ABC_ANALYSIS_VIEWED",
                $"ABC Analysis viewed: {from:yyyy-MM-dd} to {to:yyyy-MM-dd}, Branch:{effBranch}",
                "Report", null, HttpContext.Connection.RemoteIpAddress?.ToString());

            LoadInventoryViewBag(allBranches, effBranch, null, tenantId);
            return View(vm);
        }

        [PermissionAuthorize("ABCAnalysis", "Print")]
        public async Task<IActionResult> ABCAnalysisPdf(
            DateTime? dateFrom, DateTime? dateTo, int? branchId)
        {
            var tenantId  = await GetReportTenantIdAsync();
            var isGlobal  = _branchService.IsGlobalUser(User);
            var curBranch = await _branchService.GetCurrentBranchAsync(User);
            int? effBranch = isGlobal ? (branchId > 0 ? branchId : null) : curBranch?.Id;

            var from = dateFrom?.Date ?? DateTime.Today.AddDays(-365);
            var to   = dateTo?.Date   ?? DateTime.Today;

            var vm       = await BuildABCAnalysis(tenantId, effBranch, from, to.AddDays(1));
            var settings = await GetSettingsAsync(tenantId);
            var tenant   = await GetTenantAsync(tenantId);
            var allBranches = await _branchService.GetAllActiveBranchesAsync();
            var branchName  = allBranches.FirstOrDefault(b => b.Id == effBranch)?.Name;
            var currency    = settings?.CurrencySymbol ?? "₱";

            var headers = new List<string> { "SKU", "Item Name", "Category", "Revenue", "Contribution %", "Cumulative %", "Class" };
            var tableRows = vm.Rows.Select(r => new List<string>
            {
                r.ItemCode, r.ItemName, r.Category,
                $"{currency}{r.Revenue:N2}",
                $"{r.ContributionPct:N2}%",
                $"{r.CumulativePct:N2}%",
                r.Classification
            }).ToList();

            var bytes = _reportPdfService.GenerateSimpleReportPdf(
                "ABC Analysis Report",
                $"{from:MMM dd, yyyy} — {to:MMM dd, yyyy}",
                headers, tableRows,
                tenant?.Name ?? settings?.BusinessName, branchName);

            return File(bytes, "application/pdf", $"ABCAnalysis-{DateTime.Today:yyyyMMdd}.pdf");
        }

        // ================================================================
        //  QUERY BUILDERS
        // ================================================================

        private async Task<List<FastMovingItemRow>> BuildFastMovingRows(
            int? tenantId, int? branchId, int? categoryId, DateTime from, DateTime toEx)
        {
            var salesQuery = _context.SalesDetails.AsNoTracking()
                .Where(d => d.SalesHeader != null &&
                            d.SalesHeader.Status == "Completed" &&
                            d.SalesHeader.SalesDate >= from &&
                            d.SalesHeader.SalesDate < toEx);

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
                salesQuery = salesQuery.Where(d =>
                    d.SalesHeader != null &&
                    (d.SalesHeader.TenantId == tenantId || d.SalesHeader.TenantId == null));

            if (branchId.HasValue)
                salesQuery = salesQuery.Where(d =>
                    d.SalesHeader != null && d.SalesHeader.BranchId == branchId);

            var aggregated = await salesQuery
                .GroupBy(d => d.ItemId)
                .Select(g => new
                {
                    ItemId  = g.Key,
                    QtySold = g.Sum(x => x.Quantity),
                    Revenue = g.Sum(x => x.LineTotal)
                })
                .ToListAsync();

            if (!aggregated.Any()) return new List<FastMovingItemRow>();

            var itemIds  = aggregated.Select(a => a.ItemId).ToList();
            var itemsQ = ApplyTenantScope(_context.Items.AsNoTracking(), tenantId)
                .Include(i => i.Category)
                .Include(i => i.Unit)
                .Where(i => itemIds.Contains(i.Id));

            if (categoryId.HasValue)
                itemsQ = itemsQ.Where(i => i.CategoryId == categoryId);

            var items = await itemsQ.ToListAsync();
            var itemDict = items.ToDictionary(i => i.Id);

            return aggregated
                .Where(a => itemDict.ContainsKey(a.ItemId))
                .Select(a =>
                {
                    var itm = itemDict[a.ItemId];
                    return new FastMovingItemRow
                    {
                        ItemId       = itm.Id,
                        ItemCode     = itm.ItemCode,
                        ItemName     = itm.ItemName,
                        Category     = itm.Category?.CategoryName ?? "—",
                        Unit         = itm.Unit?.ShortName ?? "—",
                        QtySold      = a.QtySold,
                        Revenue      = a.Revenue,
                        CurrentStock = itm.CurrentStock,
                        CostPrice    = itm.CostPrice
                    };
                })
                .ToList();
        }

        private async Task<List<SlowMovingItemRow>> BuildSlowMovingRows(
            int? tenantId, int? branchId, int? categoryId, int thresholdDays)
        {
            var cutoff = DateTime.Today.AddDays(-thresholdDays);

            var salesQuery = _context.SalesDetails.AsNoTracking()
                .Where(d => d.SalesHeader != null &&
                            d.SalesHeader.Status == "Completed" &&
                            d.SalesHeader.SalesDate >= cutoff);

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
                salesQuery = salesQuery.Where(d =>
                    d.SalesHeader != null &&
                    (d.SalesHeader.TenantId == tenantId || d.SalesHeader.TenantId == null));

            if (branchId.HasValue)
                salesQuery = salesQuery.Where(d =>
                    d.SalesHeader != null && d.SalesHeader.BranchId == branchId);

            var recentSales = await salesQuery
                .GroupBy(d => d.ItemId)
                .Select(g => new { ItemId = g.Key, QtySold = g.Sum(x => x.Quantity) })
                .ToDictionaryAsync(x => x.ItemId, x => x.QtySold);

            // Last sale date per item (all time)
            var lastSalesQ = _context.SalesDetails.AsNoTracking()
                .Where(d => d.SalesHeader != null && d.SalesHeader.Status == "Completed");

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
                lastSalesQ = lastSalesQ.Where(d =>
                    d.SalesHeader != null &&
                    (d.SalesHeader.TenantId == tenantId || d.SalesHeader.TenantId == null));

            var lastSaleDates = await lastSalesQ
                .GroupBy(d => d.ItemId)
                .Select(g => new { ItemId = g.Key, LastDate = g.Max(x => x.SalesHeader!.SalesDate) })
                .ToDictionaryAsync(x => x.ItemId, x => (DateTime?)x.LastDate);

            var itemsQ = ApplyTenantScope(_context.Items.AsNoTracking(), tenantId)
                .Include(i => i.Category)
                .Where(i => i.Status == "Active" && i.CurrentStock > 0);

            if (categoryId.HasValue)
                itemsQ = itemsQ.Where(i => i.CategoryId == categoryId);

            var items = await itemsQ.OrderBy(i => i.ItemName).ToListAsync();
            var today = DateTime.Today;

            // Threshold: < 20% of average for that category or simply < 1 unit/30 days
            var threshold = thresholdDays / 30.0m; // units per day threshold = 0

            return items
                .Select(i =>
                {
                    var qtySold = recentSales.GetValueOrDefault(i.Id, 0m);
                    var lastDate = lastSaleDates.GetValueOrDefault(i.Id);
                    var daysSince = lastDate.HasValue ? (int)(today - lastDate.Value.Date).TotalDays : 999;
                    return new SlowMovingItemRow
                    {
                        ItemId            = i.Id,
                        ItemCode          = i.ItemCode,
                        ItemName          = i.ItemName,
                        Category          = i.Category?.CategoryName ?? "—",
                        CurrentStock      = i.CurrentStock,
                        InventoryValue    = i.CurrentStock * i.CostPrice,
                        QtySold           = qtySold,
                        LastSoldDate      = lastDate,
                        DaysSinceLastSale = daysSince
                    };
                })
                .Where(r => r.QtySold < threshold || r.DaysSinceLastSale > thresholdDays)
                .OrderBy(r => r.QtySold)
                .ThenByDescending(r => r.DaysSinceLastSale)
                .ToList();
        }

        private async Task<List<DeadStockRow>> BuildDeadStockRows(
            int? tenantId, int? branchId, int? categoryId, int days)
        {
            var cutoff = DateTime.Today.AddDays(-days);
            var today  = DateTime.Today;

            var salesQ = _context.SalesDetails.AsNoTracking()
                .Where(d => d.SalesHeader != null && d.SalesHeader.Status == "Completed");

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
                salesQ = salesQ.Where(d =>
                    d.SalesHeader != null &&
                    (d.SalesHeader.TenantId == tenantId || d.SalesHeader.TenantId == null));

            var lastSaleDates = await salesQ
                .GroupBy(d => d.ItemId)
                .Select(g => new { ItemId = g.Key, LastDate = g.Max(x => x.SalesHeader!.SalesDate) })
                .ToDictionaryAsync(x => x.ItemId, x => (DateTime?)x.LastDate);

            var itemsQ = ApplyTenantScope(_context.Items.AsNoTracking(), tenantId)
                .Include(i => i.Category)
                .Where(i => i.Status == "Active" && i.CurrentStock > 0);

            if (categoryId.HasValue)
                itemsQ = itemsQ.Where(i => i.CategoryId == categoryId);

            var items = await itemsQ.ToListAsync();

            return items
                .Select(i =>
                {
                    var lastDate = lastSaleDates.GetValueOrDefault(i.Id);
                    var daysSince = lastDate.HasValue ? (int)(today - lastDate.Value.Date).TotalDays : 9999;
                    return (Item: i, LastDate: lastDate, DaysSince: daysSince);
                })
                .Where(x => !x.LastDate.HasValue || x.LastDate.Value.Date <= cutoff)
                .OrderByDescending(x => x.DaysSince)
                .Select(x =>
                {
                    var age = x.DaysSince;
                    var color = age > 365 ? "red" : age > 180 ? "orange" : "yellow";
                    return new DeadStockRow
                    {
                        ItemId           = x.Item.Id,
                        ItemCode         = x.Item.ItemCode,
                        ItemName         = x.Item.ItemName,
                        Category         = x.Item.Category?.CategoryName ?? "—",
                        CurrentStock     = x.Item.CurrentStock,
                        InventoryValue   = x.Item.CurrentStock * x.Item.CostPrice,
                        LastSaleDate     = x.LastDate,
                        DaysSinceLastSale = age == 9999 ? -1 : age,
                        AgeColor         = color
                    };
                })
                .ToList();
        }

        private async Task<List<ReorderSuggestionRow>> BuildReorderSuggestions(
            int? tenantId, int? branchId, int? categoryId)
        {
            var cutoff90 = DateTime.Today.AddDays(-90);

            var salesQ = _context.SalesDetails.AsNoTracking()
                .Where(d => d.SalesHeader != null &&
                            d.SalesHeader.Status == "Completed" &&
                            d.SalesHeader.SalesDate >= cutoff90);

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
                salesQ = salesQ.Where(d =>
                    d.SalesHeader != null &&
                    (d.SalesHeader.TenantId == tenantId || d.SalesHeader.TenantId == null));

            var recentSales = await salesQ
                .GroupBy(d => d.ItemId)
                .Select(g => new { ItemId = g.Key, QtySold = g.Sum(x => x.Quantity) })
                .ToDictionaryAsync(x => x.ItemId, x => x.QtySold);

            var itemsQ = ApplyTenantScope(_context.Items.AsNoTracking(), tenantId)
                .Include(i => i.Category)
                .Include(i => i.Supplier)
                .Where(i => i.Status == "Active" && i.CurrentStock <= i.ReorderLevel);

            if (categoryId.HasValue)
                itemsQ = itemsQ.Where(i => i.CategoryId == categoryId);

            var items = await itemsQ.OrderBy(i => i.CurrentStock).ToListAsync();

            return items.Select(i =>
            {
                var avgMonthly = recentSales.GetValueOrDefault(i.Id, 0m) / 3m; // 90 days / 3 = monthly
                var suggestedQty = i.MaxStockLevel.HasValue
                    ? Math.Max(0, i.MaxStockLevel.Value - i.CurrentStock)
                    : Math.Max(0, avgMonthly * 2 - i.CurrentStock);

                if (suggestedQty <= 0) suggestedQty = i.ReorderLevel - i.CurrentStock + 1;

                return new ReorderSuggestionRow
                {
                    ItemId            = i.Id,
                    ItemCode          = i.ItemCode,
                    ItemName          = i.ItemName,
                    Category          = i.Category?.CategoryName ?? "—",
                    CurrentStock      = i.CurrentStock,
                    ReorderLevel      = i.ReorderLevel,
                    MaxStockLevel     = i.MaxStockLevel,
                    SuggestedQty      = Math.Ceiling(suggestedQty),
                    PreferredSupplier = i.Supplier?.SupplierName,
                    SupplierId        = i.SupplierId,
                    AvgMonthlySales   = avgMonthly,
                    CostPrice         = i.CostPrice
                };
            }).ToList();
        }

        private async Task<List<StockAgingRow>> BuildStockAgingRows(
            int? tenantId, int? branchId, int? categoryId)
        {
            var today = DateTime.Today;

            // Last stock-in date per item
            var stockInQ = _context.StockInDetails.AsNoTracking()
                .Where(d => d.StockInHeader != null);

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
                stockInQ = stockInQ.Where(d =>
                    d.StockInHeader != null &&
                    (d.StockInHeader.TenantId == tenantId || d.StockInHeader.TenantId == null));

            if (branchId.HasValue)
                stockInQ = stockInQ.Where(d =>
                    d.StockInHeader != null &&
                    (d.StockInHeader.BranchId == branchId || d.StockInHeader.BranchId == null));

            var stockInByItem = await stockInQ
                .GroupBy(d => d.ItemId)
                .Select(g => new
                {
                    ItemId  = g.Key,
                    LastDate = g.Max(x => x.StockInHeader!.DateReceived),
                    AvgCost  = g.Sum(x => x.TotalCost) /
                               (g.Sum(x => x.Quantity) == 0 ? 1 : g.Sum(x => x.Quantity))
                })
                .ToDictionaryAsync(x => x.ItemId);

            var itemsQ = ApplyTenantScope(_context.Items.AsNoTracking(), tenantId)
                .Include(i => i.Category)
                .Where(i => i.Status == "Active" && i.CurrentStock > 0);

            if (categoryId.HasValue)
                itemsQ = itemsQ.Where(i => i.CategoryId == categoryId);

            var items = await itemsQ.ToListAsync();

            return items.Select(i =>
            {
                var costPrice = i.CostPrice;
                var age = stockInByItem.TryGetValue(i.Id, out var si)
                    ? (int)(today - si.LastDate.Date).TotalDays
                    : 999;

                if (stockInByItem.TryGetValue(i.Id, out var si2))
                    costPrice = si2.AvgCost > 0 ? si2.AvgCost : i.CostPrice;

                var qty   = i.CurrentStock;
                var value = qty * costPrice;

                var row = new StockAgingRow
                {
                    ItemId   = i.Id,
                    ItemCode = i.ItemCode,
                    ItemName = i.ItemName,
                    Category = i.Category?.CategoryName ?? "—"
                };

                if      (age <= 30)  { row.Qty0_30   = qty; row.Value0_30   = value; }
                else if (age <= 60)  { row.Qty31_60  = qty; row.Value31_60  = value; }
                else if (age <= 90)  { row.Qty61_90  = qty; row.Value61_90  = value; }
                else if (age <= 180) { row.Qty91_180 = qty; row.Value91_180 = value; }
                else                 { row.Qty180Plus = qty; row.Value180Plus = value; }

                return row;
            }).ToList();
        }

        private async Task<InventoryValuationReport> BuildInventoryValuation(
            int? tenantId, int? branchId, int? categoryId)
        {
            var itemsQ = ApplyTenantScope(_context.Items.AsNoTracking(), tenantId)
                .Include(i => i.Category)
                .Include(i => i.Unit)
                .Where(i => i.Status == "Active" && i.CurrentStock > 0);

            if (categoryId.HasValue)
                itemsQ = itemsQ.Where(i => i.CategoryId == categoryId);

            var items = await itemsQ.OrderBy(i => i.Category!.CategoryName).ThenBy(i => i.ItemName).ToListAsync();

            var groups = items
                .GroupBy(i => i.Category?.CategoryName ?? "Uncategorized")
                .Select(g => new InventoryValuationGroup
                {
                    Category = g.Key,
                    Items = g.Select(i => new InventoryValuationRow
                    {
                        ItemId         = i.Id,
                        ItemCode       = i.ItemCode,
                        ItemName       = i.ItemName,
                        Unit           = i.Unit?.ShortName ?? "—",
                        QtyOnHand      = i.CurrentStock,
                        AverageCost    = i.CostPrice,
                        InventoryValue = i.CurrentStock * i.CostPrice
                    }).ToList()
                })
                .ToList();

            return new InventoryValuationReport
            {
                BranchId   = branchId,
                CategoryId = categoryId,
                AsOfDate   = DateTime.Today,
                Groups     = groups
            };
        }

        private async Task<ABCAnalysisViewModel> BuildABCAnalysis(
            int? tenantId, int? branchId, DateTime from, DateTime toEx)
        {
            var salesQ = _context.SalesDetails.AsNoTracking()
                .Where(d => d.SalesHeader != null &&
                            d.SalesHeader.Status == "Completed" &&
                            d.SalesHeader.SalesDate >= from &&
                            d.SalesHeader.SalesDate < toEx);

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
                salesQ = salesQ.Where(d =>
                    d.SalesHeader != null &&
                    (d.SalesHeader.TenantId == tenantId || d.SalesHeader.TenantId == null));

            if (branchId.HasValue)
                salesQ = salesQ.Where(d =>
                    d.SalesHeader != null && d.SalesHeader.BranchId == branchId);

            var revenueByItem = await salesQ
                .GroupBy(d => d.ItemId)
                .Select(g => new { ItemId = g.Key, Revenue = g.Sum(x => x.LineTotal) })
                .ToDictionaryAsync(x => x.ItemId, x => x.Revenue);

            if (!revenueByItem.Any())
                return new ABCAnalysisViewModel { DateFrom = from, DateTo = toEx.AddDays(-1) };

            var itemIds = revenueByItem.Keys.ToList();
            var items   = await ApplyTenantScope(_context.Items.AsNoTracking(), tenantId)
                .Include(i => i.Category)
                .Where(i => itemIds.Contains(i.Id))
                .ToDictionaryAsync(i => i.Id);

            var rows = revenueByItem
                .OrderByDescending(kv => kv.Value)
                .Select(kv =>
                {
                    items.TryGetValue(kv.Key, out var item);
                    return new ABCAnalysisRow
                    {
                        ItemId   = kv.Key,
                        ItemCode = item?.ItemCode ?? "—",
                        ItemName = item?.ItemName ?? "Unknown",
                        Category = item?.Category?.CategoryName ?? "—",
                        Revenue  = kv.Value
                    };
                })
                .ToList();

            var total = rows.Sum(r => r.Revenue);
            decimal cumulative = 0;
            foreach (var r in rows)
            {
                r.ContributionPct = total > 0 ? r.Revenue / total * 100 : 0;
                cumulative += r.ContributionPct;
                r.CumulativePct = cumulative;
                r.Classification = cumulative <= 80 ? "A" : cumulative <= 95 ? "B" : "C";
            }

            return new ABCAnalysisViewModel
            {
                DateFrom = from,
                DateTo   = toEx.AddDays(-1),
                Rows     = rows
            };
        }

        // ── Inventory Health Score ─────────────────────────────────────

        public static InventoryHealthScore ComputeHealthScore(
            int totalItems,
            int lowStockCount,
            int criticalCount,
            int deadStockCount,
            int overstockCount)
        {
            if (totalItems == 0)
                return new InventoryHealthScore
                {
                    Score = 100, Status = "Good", StatusColor = "success",
                    Recommendation = "No inventory items found."
                };

            var lowPct       = (double)lowStockCount   / totalItems;
            var critPct      = (double)criticalCount   / totalItems;
            var deadPct      = (double)deadStockCount  / totalItems;
            var overPct      = (double)overstockCount  / totalItems;

            var score = (int)Math.Round(100
                - (lowPct  * 30)
                - (critPct * 20)
                - (deadPct * 30)
                - (overPct * 20));

            score = Math.Clamp(score, 0, 100);

            string status, color, rec;
            if (score >= 80)      { status = "Good"; color = "success"; rec = "Inventory is well-managed. Continue monitoring reorder levels."; }
            else if (score >= 60) { status = "Fair"; color = "warning"; rec = "Address low-stock and dead-stock items to improve inventory health."; }
            else                  { status = "Poor"; color = "danger";  rec = "Immediate action required: critical stock levels and high dead-stock volume detected."; }

            return new InventoryHealthScore
            {
                Score = score, Status = status, StatusColor = color,
                Recommendation = rec,
                TotalItems     = totalItems,
                LowStockCount  = lowStockCount,
                CriticalCount  = criticalCount,
                DeadStockCount = deadStockCount,
                OverstockCount = overstockCount
            };
        }

        // ── Shared helpers ─────────────────────────────────────────────

        private void LoadInventoryViewBag(IEnumerable<Branch> allBranches, int? effBranch, int? categoryId, int? tenantId)
        {
            ViewBag.AllBranches    = allBranches;
            ViewBag.BranchId       = effBranch;
            ViewBag.CategoryId     = categoryId;
            ViewBag.IsGlobalUser   = _branchService.IsGlobalUser(User);
            ViewBag.AllCategories  = _context.Categories.AsNoTracking()
                .Where(c => !_tenantContext.IsGlobalUser && tenantId.HasValue
                    ? c.TenantId == tenantId || c.TenantId == null
                    : true)
                .OrderBy(c => c.CategoryName)
                .Select(c => new { c.Id, c.CategoryName })
                .ToList();
        }

        private async Task<SystemSetting?> GetSettingsAsync(int? tenantId) =>
            await _context.SystemSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.TenantId == tenantId);

        private async Task<Tenant?> GetTenantAsync(int? tenantId) =>
            tenantId.HasValue
                ? await _platformDb.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId)
                : null;
    }
}

using HardwareManagementSystem.Data;
using HardwareManagementSystem.Models;
using HardwareManagementSystem.Services;
using HardwareManagementSystem.Services.TenantDatabases;
using HardwareManagementSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ClosedXML.Excel;
using System.IO;
using HardwareManagementSystem.Services.Pdf;

namespace HardwareManagementSystem.Controllers
{
    [Authorize]
    [PermissionAuthorize("Reports", "View")]
    public partial class ReportsController : OperationalDbController
    {
        // Shared platform context — used ONLY for platform-owned reads (e.g. Tenants),
        // which never live in a tenant's dedicated database.
        private readonly ApplicationDbContext _platformDb;
        private readonly ReportPdfService _reportPdfService;
        private readonly BranchService _branchService;
        private readonly ITenantContext _tenantContext;
        private readonly TenantGuard _tenantGuard;
        private readonly AuditService _auditService;

        public ReportsController(
            ITenantOperationalContextProvider ctxProvider,
            ApplicationDbContext platformDb,
            ReportPdfService reportPdfService,
            BranchService branchService,
            ITenantContext tenantContext,
            TenantGuard tenantGuard,
            AuditService auditService)
            : base(ctxProvider)
        {
            _platformDb = platformDb;
            _reportPdfService = reportPdfService;
            _branchService = branchService;
            _tenantContext = tenantContext;
            _tenantGuard = tenantGuard;
            _auditService = auditService;
        }

        public async Task<IActionResult> Index(DateTime? dateFrom, DateTime? dateTo, int? branchId)
        {
            var tenantId = await GetReportTenantIdAsync();

            var from = dateFrom?.Date ?? DateTime.Today;
            var to = dateTo?.Date ?? DateTime.Today;

            var startDate = from;
            var endDate = to.AddDays(1);

            // Branch resolution
            var isGlobal = _branchService.IsGlobalUser(User);
            var allBranches = await _branchService.GetAllActiveBranchesAsync();
            var currentBranch = await _branchService.GetCurrentBranchAsync(User);

            // Non-global users always locked to their branch
            int? effectiveBranchId = isGlobal
                ? (branchId.HasValue && branchId > 0 ? branchId : null)
                : currentBranch?.Id;

            string? effectiveBranchName = effectiveBranchId.HasValue
                ? allBranches.FirstOrDefault(b => b.Id == effectiveBranchId)?.Name
                : null;

            ViewBag.BranchId = effectiveBranchId;
            ViewBag.BranchName = effectiveBranchName;
            ViewBag.AllBranches = allBranches;
            ViewBag.IsGlobalUser = isGlobal;

            var salesQuery = ApplyTenantScope(
                    _context.SalesHeaders.AsNoTracking(),
                    tenantId)
                .Where(s => s.SalesDate >= startDate && s.SalesDate < endDate);

            if (effectiveBranchId.HasValue)
                salesQuery = salesQuery.Where(s => s.BranchId == effectiveBranchId);

            var completedSales = salesQuery
                .Where(s => s.Status == "Completed");

            var voidedSales = salesQuery
                .Where(s => s.Status == "Voided");

            var collectionsQuery = ApplyTenantScope(
                    _context.CustomerLedgers.AsNoTracking(),
                    tenantId)
                .Where(l =>
                    l.TransactionType == "PAYMENT" &&
                    l.TransactionDate >= startDate &&
                    l.TransactionDate < endDate);

            var returnsQuery = ApplyTenantScope(
                    _context.SalesReturnHeaders.AsNoTracking(),
                    tenantId)
                .Where(r => r.ReturnDate >= startDate && r.ReturnDate < endDate);

            var model = new ReportsDashboardViewModel
            {
                DateFrom = from,
                DateTo = to,
                BranchId = effectiveBranchId,
                BranchName = effectiveBranchName,

                GrossSales = await completedSales.SumAsync(s => s.SubTotal),
                TotalDiscounts = await completedSales.SumAsync(s => s.DiscountAmount),
                NetSales = await completedSales.SumAsync(s => s.TotalAmount),
                TransactionCount = await completedSales.CountAsync(),

                CashSales = await completedSales
                    .Where(s => s.PaymentMethod == "Cash")
                    .SumAsync(s => s.TotalAmount),

                GCashSales = await completedSales
                    .Where(s => s.PaymentMethod == "GCash")
                    .SumAsync(s => s.TotalAmount),

                CreditSales = await completedSales
                    .Where(s => s.PaymentMethod == "Credit")
                    .SumAsync(s => s.TotalAmount),

                Collections = await collectionsQuery.SumAsync(l => l.CreditAmount),
                Returns = await returnsQuery.SumAsync(r => r.RefundAmount),
                VoidedSales = await voidedSales.SumAsync(s => s.TotalAmount),

                OutstandingCustomerBalance = await ApplyTenantScope(
                        _context.CustomerLedgers.AsNoTracking(),
                        tenantId)
                    .GroupBy(l => l.CustomerId)
                    .Select(g => g.OrderByDescending(x => x.Id)
                        .Select(x => x.RunningBalance)
                        .FirstOrDefault())
                    .SumAsync(),

                LowStockCount = await ApplyTenantScope(
                        _context.Items.AsNoTracking(),
                        tenantId)
                    .CountAsync(i =>
                        i.Status == "Active" &&
                        i.CurrentStock > 0 &&
                        i.CurrentStock <= i.ReorderLevel),

                OutOfStockCount = await ApplyTenantScope(
                        _context.Items.AsNoTracking(),
                        tenantId)
                    .CountAsync(i =>
                        i.Status == "Active" &&
                        i.CurrentStock <= 0)
            };

            model.PaymentSummary = await completedSales
                .GroupBy(s => s.PaymentMethod)
                .Select(g => new PaymentSummaryReportRow
                {
                    PaymentMethod = g.Key,
                    TransactionCount = g.Count(),
                    TotalAmount = g.Sum(s => s.TotalAmount)
                })
                .OrderByDescending(x => x.TotalAmount)
                .ToListAsync();

            model.TopSellingItems = await ApplyTenantScope(
                    _context.SalesDetails.AsNoTracking(),
                    tenantId)
                .Include(d => d.Item)
                .Include(d => d.SalesHeader)
                .Where(d =>
                    d.SalesHeader != null &&
                    d.SalesHeader.Status == "Completed" &&
                    d.SalesHeader.SalesDate >= startDate &&
                    d.SalesHeader.SalesDate < endDate)
                .GroupBy(d => d.Item != null ? d.Item.ItemName : "N/A")
                .Select(g => new TopSellingItemReportRow
                {
                    ItemName = g.Key,
                    QuantitySold = g.Sum(d => d.Quantity),
                    SalesAmount = g.Sum(d => d.LineTotal)
                })
                .OrderByDescending(x => x.QuantitySold)
                .Take(10)
                .ToListAsync();

            model.CustomerBalances = await ApplyTenantScope(
                    _context.CustomerLedgers.AsNoTracking(),
                    tenantId)
                .Include(l => l.Customer)
                .GroupBy(l => new
                {
                    l.CustomerId,
                    CustomerName = l.Customer != null ? l.Customer.CustomerName : "N/A"
                })
                .Select(g => new CustomerBalanceReportRow
                {
                    CustomerId = g.Key.CustomerId,
                    CustomerName = g.Key.CustomerName,
                    Balance = g.OrderByDescending(x => x.Id)
                        .Select(x => x.RunningBalance)
                        .FirstOrDefault()
                })
                .Where(x => x.Balance > 0)
                .OrderByDescending(x => x.Balance)
                .Take(10)
                .ToListAsync();

            return View(model);
        }

        public async Task<IActionResult> ExportExcel(
    DateTime? dateFrom,
    DateTime? dateTo,
    string reportType = "All")
        {
            var tenantId = await GetReportTenantIdAsync();

            var from = dateFrom?.Date ?? DateTime.Today;
            var to = dateTo?.Date ?? DateTime.Today;

            var startDate = from;
            var endDate = to.AddDays(1);

            reportType = string.IsNullOrWhiteSpace(reportType)
                ? "All"
                : reportType;

            using var workbook = new XLWorkbook();

            // =====================================================
            // SALES REPORT
            // =====================================================

            if (reportType == "Sales" || reportType == "All")
            {
                var sales = await ApplyTenantScope(
                        _context.SalesHeaders.AsNoTracking(),
                        tenantId)
                    .Where(s => s.SalesDate >= startDate &&
                                s.SalesDate < endDate)
                    .OrderByDescending(s => s.SalesDate)
                    .ToListAsync();

                var ws = workbook.Worksheets.Add("Sales Report");

                ws.Cell(1, 1).Value = "Hardware POS Sales Report";

                ws.Range(1, 1, 1, 8).Merge();
                ws.Range(1, 1, 1, 8).Style.Font.Bold = true;
                ws.Range(1, 1, 1, 8).Style.Font.FontSize = 16;

                ws.Cell(2, 1).Value =
                    $"Date Range: {from:MMM dd, yyyy} - {to:MMM dd, yyyy}";

                ws.Range(2, 1, 2, 8).Merge();

                ws.Cell(4, 1).Value = "Receipt #";
                ws.Cell(4, 2).Value = "Date";
                ws.Cell(4, 3).Value = "Cashier";
                ws.Cell(4, 4).Value = "Payment";
                ws.Cell(4, 5).Value = "Subtotal";
                ws.Cell(4, 6).Value = "Discount";
                ws.Cell(4, 7).Value = "Total";
                ws.Cell(4, 8).Value = "Status";

                var header = ws.Range(4, 1, 4, 8);

                header.Style.Font.Bold = true;
                header.Style.Fill.BackgroundColor = XLColor.LightGray;

                var row = 5;

                foreach (var sale in sales)
                {
                    ws.Cell(row, 1).Value = sale.SalesNumber;
                    ws.Cell(row, 2).Value = sale.SalesDate;
                    ws.Cell(row, 3).Value = sale.CashierName ?? "Unknown";
                    ws.Cell(row, 4).Value = sale.PaymentMethod;
                    ws.Cell(row, 5).Value = sale.SubTotal;
                    ws.Cell(row, 6).Value = sale.DiscountAmount;
                    ws.Cell(row, 7).Value = sale.TotalAmount;
                    ws.Cell(row, 8).Value = sale.Status;

                    row++;
                }

                ws.Cell(row + 1, 6).Value = "Net Sales:";
                ws.Cell(row + 1, 6).Style.Font.Bold = true;

                ws.Cell(row + 1, 7).Value =
                    sales.Where(s => s.Status == "Completed")
                         .Sum(s => s.TotalAmount);

                ws.Cell(row + 1, 7).Style.Font.Bold = true;

                ws.Columns().AdjustToContents();
            }

            // =====================================================
            // PAYMENT SUMMARY
            // =====================================================

            if (reportType == "Payments" || reportType == "All")
            {
                var payments = await ApplyTenantScope(
                        _context.SalesHeaders.AsNoTracking(),
                        tenantId)
                    .Where(s =>
                        s.Status == "Completed" &&
                        s.SalesDate >= startDate &&
                        s.SalesDate < endDate)
                    .GroupBy(s => s.PaymentMethod)
                    .Select(g => new
                    {
                        PaymentMethod = g.Key,
                        Count = g.Count(),
                        Total = g.Sum(x => x.TotalAmount)
                    })
                    .ToListAsync();

                var ws = workbook.Worksheets.Add("Payment Summary");

                ws.Cell(1, 1).Value = "Payment Summary";
                ws.Range(1, 1, 1, 3).Merge();

                ws.Range(1, 1, 1, 3).Style.Font.Bold = true;
                ws.Range(1, 1, 1, 3).Style.Font.FontSize = 16;

                ws.Cell(3, 1).Value = "Payment Method";
                ws.Cell(3, 2).Value = "Transactions";
                ws.Cell(3, 3).Value = "Total Amount";

                var header = ws.Range(3, 1, 3, 3);

                header.Style.Font.Bold = true;
                header.Style.Fill.BackgroundColor = XLColor.LightGray;

                var row = 4;

                foreach (var payment in payments)
                {
                    ws.Cell(row, 1).Value = payment.PaymentMethod;
                    ws.Cell(row, 2).Value = payment.Count;
                    ws.Cell(row, 3).Value = payment.Total;

                    row++;
                }

                ws.Columns().AdjustToContents();
            }

            // =====================================================
            // TOP SELLING
            // =====================================================

            if (reportType == "TopSelling" || reportType == "All")
            {
                var topSelling = await ApplyTenantScope(
                        _context.SalesDetails.AsNoTracking(),
                        tenantId)
                    .Include(d => d.Item)
                    .Include(d => d.SalesHeader)
                    .Where(d =>
                        d.SalesHeader != null &&
                        d.SalesHeader.Status == "Completed" &&
                        d.SalesHeader.SalesDate >= startDate &&
                        d.SalesHeader.SalesDate < endDate)
                    .GroupBy(d => d.Item != null
                        ? d.Item.ItemName
                        : "N/A")
                    .Select(g => new
                    {
                        ItemName = g.Key,
                        QtySold = g.Sum(x => x.Quantity),
                        SalesAmount = g.Sum(x => x.LineTotal)
                    })
                    .OrderByDescending(x => x.QtySold)
                    .Take(20)
                    .ToListAsync();

                var ws = workbook.Worksheets.Add("Top Selling");

                ws.Cell(1, 1).Value = "Top Selling Items";
                ws.Range(1, 1, 1, 3).Merge();

                ws.Range(1, 1, 1, 3).Style.Font.Bold = true;
                ws.Range(1, 1, 1, 3).Style.Font.FontSize = 16;

                ws.Cell(3, 1).Value = "Item";
                ws.Cell(3, 2).Value = "Qty Sold";
                ws.Cell(3, 3).Value = "Sales Amount";

                var header = ws.Range(3, 1, 3, 3);

                header.Style.Font.Bold = true;
                header.Style.Fill.BackgroundColor = XLColor.LightGray;

                var row = 4;

                foreach (var item in topSelling)
                {
                    ws.Cell(row, 1).Value = item.ItemName;
                    ws.Cell(row, 2).Value = item.QtySold;
                    ws.Cell(row, 3).Value = item.SalesAmount;

                    row++;
                }

                ws.Columns().AdjustToContents();
            }

            // =====================================================
            // CUSTOMER BALANCES
            // =====================================================

            if (reportType == "CustomerBalances" || reportType == "All")
            {
                var balances = await ApplyTenantScope(
                        _context.CustomerLedgers.AsNoTracking(),
                        tenantId)
                    .Include(l => l.Customer)
                    .GroupBy(l => new
                    {
                        l.CustomerId,
                        CustomerName = l.Customer != null
                            ? l.Customer.CustomerName
                            : "N/A"
                    })
                    .Select(g => new
                    {
                        g.Key.CustomerName,
                        Balance = g.OrderByDescending(x => x.Id)
                            .Select(x => x.RunningBalance)
                            .FirstOrDefault()
                    })
                    .Where(x => x.Balance > 0)
                    .OrderByDescending(x => x.Balance)
                    .ToListAsync();

                var ws = workbook.Worksheets.Add("Customer Balances");

                ws.Cell(1, 1).Value = "Customer Outstanding Balances";

                ws.Range(1, 1, 1, 2).Merge();

                ws.Range(1, 1, 1, 2).Style.Font.Bold = true;
                ws.Range(1, 1, 1, 2).Style.Font.FontSize = 16;

                ws.Cell(3, 1).Value = "Customer";
                ws.Cell(3, 2).Value = "Outstanding Balance";

                var header = ws.Range(3, 1, 3, 2);

                header.Style.Font.Bold = true;
                header.Style.Fill.BackgroundColor = XLColor.LightGray;

                var row = 4;

                foreach (var balance in balances)
                {
                    ws.Cell(row, 1).Value = balance.CustomerName;
                    ws.Cell(row, 2).Value = balance.Balance;

                    row++;
                }

                ws.Columns().AdjustToContents();
            }

            // =====================================================
            // INVENTORY STATUS
            // =====================================================

            if (reportType == "Inventory" || reportType == "All")
            {
                var inventory = await ApplyTenantScope(
                        _context.Items.AsNoTracking(),
                        tenantId)
                    .Where(i => i.Status == "Active")
                    .OrderBy(i => i.ItemName)
                    .ToListAsync();

                var ws = workbook.Worksheets.Add("Inventory Status");

                ws.Cell(1, 1).Value = "Inventory Status";

                ws.Range(1, 1, 1, 5).Merge();

                ws.Range(1, 1, 1, 5).Style.Font.Bold = true;
                ws.Range(1, 1, 1, 5).Style.Font.FontSize = 16;

                ws.Cell(3, 1).Value = "Item";
                ws.Cell(3, 2).Value = "Stock";
                ws.Cell(3, 3).Value = "Reorder Level";
                ws.Cell(3, 4).Value = "Cost Price";
                ws.Cell(3, 5).Value = "Inventory Value";

                var header = ws.Range(3, 1, 3, 5);

                header.Style.Font.Bold = true;
                header.Style.Fill.BackgroundColor = XLColor.LightGray;

                var row = 4;

                foreach (var item in inventory)
                {
                    ws.Cell(row, 1).Value = item.ItemName;
                    ws.Cell(row, 2).Value = item.CurrentStock;
                    ws.Cell(row, 3).Value = item.ReorderLevel;
                    ws.Cell(row, 4).Value = item.CostPrice;
                    ws.Cell(row, 5).Value =
                        item.CurrentStock * item.CostPrice;

                    row++;
                }

                ws.Columns().AdjustToContents();
            }

            using var stream = new MemoryStream();

            workbook.SaveAs(stream);

            var fileName =
                $"Hardware_Report_{reportType}_{from:yyyyMMdd}_{to:yyyyMMdd}.xlsx";

            return File(
                stream.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName
            );
        }

        public async Task<IActionResult> Print(
    DateTime? dateFrom,
    DateTime? dateTo)
        {
            var tenantId = await GetReportTenantIdAsync();

            var from = dateFrom?.Date ?? DateTime.Today;
            var to = dateTo?.Date ?? DateTime.Today;

            var startDate = from;
            var endDate = to.AddDays(1);

            var model = new ReportsDashboardViewModel
            {
                DateFrom = from,
                DateTo = to,

                GrossSales = await ApplyTenantScope(_context.SalesHeaders, tenantId)
                    .Where(s =>
                        s.Status == "Completed" &&
                        s.SalesDate >= startDate &&
                        s.SalesDate < endDate)
                    .SumAsync(s => s.SubTotal),

                TotalDiscounts = await ApplyTenantScope(_context.SalesHeaders, tenantId)
                    .Where(s =>
                        s.Status == "Completed" &&
                        s.SalesDate >= startDate &&
                        s.SalesDate < endDate)
                    .SumAsync(s => s.DiscountAmount),

                NetSales = await ApplyTenantScope(_context.SalesHeaders, tenantId)
                    .Where(s =>
                        s.Status == "Completed" &&
                        s.SalesDate >= startDate &&
                        s.SalesDate < endDate)
                    .SumAsync(s => s.TotalAmount),

                TransactionCount = await ApplyTenantScope(_context.SalesHeaders, tenantId)
                    .Where(s =>
                        s.Status == "Completed" &&
                        s.SalesDate >= startDate &&
                        s.SalesDate < endDate)
                    .CountAsync(),

                Collections = await ApplyTenantScope(_context.CustomerLedgers, tenantId)
                    .Where(l =>
                        l.TransactionType == "PAYMENT" &&
                        l.TransactionDate >= startDate &&
                        l.TransactionDate < endDate)
                    .SumAsync(l => l.CreditAmount),

                Returns = await ApplyTenantScope(_context.SalesReturnHeaders, tenantId)
                    .Where(r =>
                        r.ReturnDate >= startDate &&
                        r.ReturnDate < endDate)
                    .SumAsync(r => r.RefundAmount),

                VoidedSales = await ApplyTenantScope(_context.SalesHeaders, tenantId)
                    .Where(s =>
                        s.Status == "Voided" &&
                        s.SalesDate >= startDate &&
                        s.SalesDate < endDate)
                    .SumAsync(s => s.TotalAmount)
            };

            return View(model);
        }

        public async Task<IActionResult> SalesDetail(
    DateTime? dateFrom,
    DateTime? dateTo,
    string? searchTerm = null,
    string? paymentMethod = null,
    string? status = null,
    int? branchId = null,
    int pageNumber = 1,
    int pageSize = 10)
        {
            var tenantId = await GetReportTenantIdAsync();

            pageSize = PagedResult<object>.ValidatePageSize(pageSize);

            var from = dateFrom?.Date ?? DateTime.Today;
            var to = dateTo?.Date ?? DateTime.Today;

            var startDate = from;
            var endDate = to.AddDays(1);

            // Branch resolution
            var isGlobal = _branchService.IsGlobalUser(User);
            var allBranches = await _branchService.GetAllActiveBranchesAsync();
            var currentBranch = await _branchService.GetCurrentBranchAsync(User);
            int? effectiveBranchId = isGlobal
                ? (branchId.HasValue && branchId > 0 ? branchId : null)
                : currentBranch?.Id;

            ViewBag.AllBranches = allBranches;
            ViewBag.IsGlobalUser = isGlobal;
            ViewBag.BranchId = effectiveBranchId;

            var query = ApplyTenantScope(
                    _context.SalesHeaders.AsNoTracking(),
                    tenantId)
                .Include(s => s.Customer)
                .Where(s => s.SalesDate >= startDate &&
                            s.SalesDate < endDate)
                .AsQueryable();

            if (effectiveBranchId.HasValue)
                query = query.Where(s => s.BranchId == effectiveBranchId);

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLower();

                query = query.Where(s =>
                    s.SalesNumber.ToLower().Contains(term) ||
                    (s.Customer != null && s.Customer.CustomerName.ToLower().Contains(term)) ||
                    (s.CashierName != null && s.CashierName.ToLower().Contains(term)));
            }

            if (!string.IsNullOrWhiteSpace(paymentMethod))
            {
                query = query.Where(s => s.PaymentMethod == paymentMethod);
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(s => s.Status == status);
            }

            var totalRecords = await query.CountAsync();

            pageNumber = PagedResult<object>.ValidatePageNumber(
                pageNumber,
                (int)Math.Ceiling(totalRecords / (double)pageSize));

            var rows = await query
                .OrderByDescending(s => s.SalesDate)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .Select(s => new SalesDetailReportViewModel
                {
                    SalesNumber = s.SalesNumber,
                    SalesDate = s.SalesDate,
                    CustomerName = s.Customer != null
                        ? s.Customer.CustomerName
                        : "Walk-in Customer",
                    CashierName = s.CashierName ?? "Unknown",
                    PaymentMethod = s.PaymentMethod,
                    SubTotal = s.SubTotal,
                    DiscountAmount = s.DiscountAmount,
                    TotalAmount = s.TotalAmount,
                    Status = s.Status
                })
                .ToListAsync();

            ViewBag.DateFrom = from;
            ViewBag.DateTo = to;
            ViewBag.PaymentMethod = paymentMethod;
            ViewBag.Status = status;

            return View(new PagedResult<SalesDetailReportViewModel>
            {
                Items = rows,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalRecords = totalRecords,
                SearchTerm = searchTerm
            });
        }
        public async Task<IActionResult> CollectionsDetail(
    DateTime? dateFrom,
    DateTime? dateTo,
    string? searchTerm = null,
    string? paymentMethod = null,
    int pageNumber = 1,
    int pageSize = 10)
        {
            var tenantId = await GetReportTenantIdAsync();

            pageSize = PagedResult<object>.ValidatePageSize(pageSize);

            var from = dateFrom?.Date ?? DateTime.Today;
            var to = dateTo?.Date ?? DateTime.Today;

            var startDate = from;
            var endDate = to.AddDays(1);

            var query = ApplyTenantScope(
                    _context.CustomerLedgers.AsNoTracking(),
                    tenantId)
                .Include(l => l.Customer)
                .Where(l =>
                    l.TransactionType == "PAYMENT" &&
                    l.TransactionDate >= startDate &&
                    l.TransactionDate < endDate)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLower();

                query = query.Where(l =>
                    l.ReferenceNumber.ToLower().Contains(term) ||
                    (l.Customer != null && l.Customer.CustomerName.ToLower().Contains(term)) ||
                    (l.CreatedBy != null && l.CreatedBy.ToLower().Contains(term)) ||
                    (l.PaymentReferenceNumber != null && l.PaymentReferenceNumber.ToLower().Contains(term)));
            }

            if (!string.IsNullOrWhiteSpace(paymentMethod))
            {
                query = query.Where(l => l.PaymentMethod == paymentMethod);
            }

            var totalRecords = await query.CountAsync();

            pageNumber = PagedResult<object>.ValidatePageNumber(
                pageNumber,
                (int)Math.Ceiling(totalRecords / (double)pageSize));

            var rows = await query
                .OrderByDescending(l => l.TransactionDate)
                .ThenByDescending(l => l.Id)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .Select(l => new CollectionDetailReportViewModel
                {
                    CollectionNumber = l.ReferenceNumber,
                    CollectionDate = l.TransactionDate,
                    CustomerName = l.Customer != null
                        ? l.Customer.CustomerName
                        : "N/A",
                    PaymentMethod = l.PaymentMethod ?? "N/A",
                    PaymentReferenceNumber = l.PaymentReferenceNumber,
                    PreviousBalance = l.BalanceBefore,
                    PaymentAmount = l.CreditAmount,
                    RemainingBalance = l.RunningBalance,
                    CollectedBy = l.CreatedBy ?? "Unknown"
                })
                .ToListAsync();

            ViewBag.DateFrom = from;
            ViewBag.DateTo = to;
            ViewBag.PaymentMethod = paymentMethod;

            return View(new PagedResult<CollectionDetailReportViewModel>
            {
                Items = rows,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalRecords = totalRecords,
                SearchTerm = searchTerm
            });
        }

        public async Task<IActionResult> CustomerBalance(
    string? searchTerm = null,
    string? customerType = null,
    int pageNumber = 1,
    int pageSize = 10)
        {
            var tenantId = await GetReportTenantIdAsync();

            pageSize = PagedResult<object>.ValidatePageSize(pageSize);

            var query = ApplyCustomerTenantScope(
                    _context.Customers.AsNoTracking(),
                    tenantId)
                .Where(c => c.IsActive)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLower();

                query = query.Where(c =>
                    c.CustomerName.ToLower().Contains(term) ||
                    (c.ContactNumber != null && c.ContactNumber.ToLower().Contains(term)));
            }

            if (!string.IsNullOrWhiteSpace(customerType))
            {
                query = query.Where(c => c.CustomerType == customerType);
            }

            var rowsQuery = query
                .Select(c => new CustomerBalanceReportViewModel
                {
                    CustomerId = c.Id,
                    CustomerName = c.CustomerName,
                    CustomerType = c.CustomerType,
                    ContactNumber = c.ContactNumber,

                    TotalCharges = ApplyTenantScope(_context.CustomerLedgers, tenantId)
                        .Where(l => l.CustomerId == c.Id)
                        .Sum(l => l.DebitAmount),

                    TotalPayments = ApplyTenantScope(_context.CustomerLedgers, tenantId)
                        .Where(l => l.CustomerId == c.Id)
                        .Sum(l => l.CreditAmount),

                    OutstandingBalance = ApplyTenantScope(_context.CustomerLedgers, tenantId)
                        .Where(l => l.CustomerId == c.Id)
                        .OrderByDescending(l => l.Id)
                        .Select(l => l.RunningBalance)
                        .FirstOrDefault(),

                    LastTransactionDate = ApplyTenantScope(_context.CustomerLedgers, tenantId)
                        .Where(l => l.CustomerId == c.Id)
                        .OrderByDescending(l => l.Id)
                        .Select(l => (DateTime?)l.TransactionDate)
                        .FirstOrDefault()
                })
                .Where(x => x.OutstandingBalance > 0);

            var totalRecords = await rowsQuery.CountAsync();

            pageNumber = PagedResult<object>.ValidatePageNumber(
                pageNumber,
                (int)Math.Ceiling(totalRecords / (double)pageSize));

            var rows = await rowsQuery
                .OrderByDescending(x => x.OutstandingBalance)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.CustomerType = customerType;

            return View(new PagedResult<CustomerBalanceReportViewModel>
            {
                Items = rows,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalRecords = totalRecords,
                SearchTerm = searchTerm
            });
        }

        public async Task<IActionResult> InventoryStatus(
    string? searchTerm = null,
    string? stockStatus = null,
    int pageNumber = 1,
    int pageSize = 10)
        {
            var tenantId = await GetReportTenantIdAsync();

            pageSize = PagedResult<object>.ValidatePageSize(pageSize);

            var query = ApplyTenantScope(
                    _context.Items.AsNoTracking(),
                    tenantId)
                .Include(i => i.Category)
                .Include(i => i.Unit)
                .Where(i => i.Status == "Active")
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLower();

                query = query.Where(i =>
                    i.ItemCode.ToLower().Contains(term) ||
                    i.ItemName.ToLower().Contains(term) ||
                    (i.Category != null && i.Category.CategoryName.ToLower().Contains(term)));
            }

            if (!string.IsNullOrWhiteSpace(stockStatus))
            {
                if (stockStatus == "low")
                {
                    query = query.Where(i =>
                        i.CurrentStock > 0 &&
                        i.CurrentStock <= i.ReorderLevel);
                }
                else if (stockStatus == "out")
                {
                    query = query.Where(i => i.CurrentStock <= 0);
                }
                else if (stockStatus == "normal")
                {
                    query = query.Where(i =>
                        i.CurrentStock > i.ReorderLevel);
                }
            }

            var totalRecords = await query.CountAsync();

            pageNumber = PagedResult<object>.ValidatePageNumber(
                pageNumber,
                (int)Math.Ceiling(totalRecords / (double)pageSize));

            var rows = await query
                .OrderBy(i => i.ItemName)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .Select(i => new InventoryStatusReportViewModel
                {
                    ItemId = i.Id,
                    ItemCode = i.ItemCode,
                    ItemName = i.ItemName,
                    CategoryName = i.Category != null ? i.Category.CategoryName : "N/A",
                    UnitName = i.Unit != null ? i.Unit.ShortName : "",
                    CurrentStock = i.CurrentStock,
                    ReorderLevel = i.ReorderLevel,
                    CostPrice = i.CostPrice,
                    SellingPrice = i.SellingPrice,
                    InventoryValue = i.CurrentStock * i.CostPrice,
                    StockStatus = i.CurrentStock <= 0
                        ? "Out of Stock"
                        : i.CurrentStock <= i.ReorderLevel
                            ? "Low Stock"
                            : "Normal"
                })
                .ToListAsync();

            ViewBag.StockStatus = stockStatus;

            return View(new PagedResult<InventoryStatusReportViewModel>
            {
                Items = rows,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalRecords = totalRecords,
                SearchTerm = searchTerm
            });
        }

        public async Task<IActionResult> ReturnsVoid(
    DateTime? dateFrom,
    DateTime? dateTo,
    string? reportType = null,
    string? searchTerm = null,
    int pageNumber = 1,
    int pageSize = 10)
        {
            var tenantId = await GetReportTenantIdAsync();

            pageSize = PagedResult<object>.ValidatePageSize(pageSize);

            var from = dateFrom?.Date ?? DateTime.Today;
            var to = dateTo?.Date ?? DateTime.Today;

            var startDate = from;
            var endDate = to.AddDays(1);

            var rows = new List<ReturnsVoidReportViewModel>();

            // =====================================================
            // RETURNS
            // =====================================================

            if (string.IsNullOrWhiteSpace(reportType) ||
                reportType == "RETURN")
            {
                var returns = await ApplyTenantScope(
                        _context.SalesReturnHeaders.AsNoTracking(),
                        tenantId)
                    .Include(r => r.SalesHeader)
                    .Where(r =>
                        r.ReturnDate >= startDate &&
                        r.ReturnDate < endDate)
                    .Select(r => new ReturnsVoidReportViewModel
                    {
                        ReportType = "RETURN",

                        ReferenceNumber = r.ReturnNumber,

                        OriginalReceiptNumber =
                            r.SalesHeader != null
                                ? r.SalesHeader.SalesNumber
                                : "N/A",

                        TransactionDate = r.ReturnDate,

                        Reason = string.IsNullOrWhiteSpace(r.Reason)
                            ? "Returned item"
                            : r.Reason,

                        Amount = r.RefundAmount,

                        ProcessedBy = r.CreatedBy ?? "Unknown"
                    })
                    .ToListAsync();

                rows.AddRange(returns);
            }

            // =====================================================
            // VOIDS
            // =====================================================

            if (string.IsNullOrWhiteSpace(reportType) ||
                reportType == "VOID")
            {
                var voids = await ApplyTenantScope(
                        _context.SalesHeaders.AsNoTracking(),
                        tenantId)
                    .Where(s =>
                        s.Status == "Voided" &&
                        s.SalesDate >= startDate &&
                        s.SalesDate < endDate)
                    .Select(s => new ReturnsVoidReportViewModel
                    {
                        ReportType = "VOID",

                        ReferenceNumber = s.SalesNumber,

                        OriginalReceiptNumber = s.SalesNumber,

                        TransactionDate = s.SalesDate,

                        Reason = "Voided sale",

                        Amount = s.TotalAmount,

                        ProcessedBy = s.CashierName ?? "Unknown"
                    })
                    .ToListAsync();

                rows.AddRange(voids);
            }

            // =====================================================
            // SEARCH
            // =====================================================

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLower();

                rows = rows.Where(r =>
                    r.ReferenceNumber.ToLower().Contains(term) ||
                    r.OriginalReceiptNumber.ToLower().Contains(term) ||
                    r.Reason.ToLower().Contains(term) ||
                    r.ProcessedBy.ToLower().Contains(term))
                    .ToList();
            }

            rows = rows
                .OrderByDescending(r => r.TransactionDate)
                .ToList();

            var totalRecords = rows.Count;

            pageNumber = PagedResult<object>.ValidatePageNumber(
                pageNumber,
                (int)Math.Ceiling(totalRecords / (double)pageSize));

            var pagedRows = rows
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            ViewBag.DateFrom = from;
            ViewBag.DateTo = to;
            ViewBag.ReportType = reportType;

            return View(new PagedResult<ReturnsVoidReportViewModel>
            {
                Items = pagedRows,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalRecords = totalRecords,
                SearchTerm = searchTerm
            });
        }

        public async Task<IActionResult> ProfitReport(
    DateTime? dateFrom,
    DateTime? dateTo,
    string? searchTerm = null,
    int? branchId = null,
    int pageNumber = 1,
    int pageSize = 10)
        {
            var tenantId = await GetReportTenantIdAsync();

            pageSize = PagedResult<object>.ValidatePageSize(pageSize);

            var from = dateFrom?.Date ?? DateTime.Today;
            var to = dateTo?.Date ?? DateTime.Today;

            var startDate = from;
            var endDate = to.AddDays(1);

            // Branch resolution
            var isGlobal = _branchService.IsGlobalUser(User);
            var allBranches = await _branchService.GetAllActiveBranchesAsync();
            var currentBranch = await _branchService.GetCurrentBranchAsync(User);
            int? effectiveBranchId = isGlobal
                ? (branchId.HasValue && branchId > 0 ? branchId : null)
                : currentBranch?.Id;

            ViewBag.AllBranches = allBranches;
            ViewBag.IsGlobalUser = isGlobal;
            ViewBag.BranchId = effectiveBranchId;

            var query = ApplyTenantScope(
                    _context.SalesDetails.AsNoTracking(),
                    tenantId)
                .Include(d => d.SalesHeader)
                .Include(d => d.Item)
                .Where(d =>
                    d.SalesHeader != null &&
                    d.SalesHeader.Status == "Completed" &&
                    d.SalesHeader.SalesDate >= startDate &&
                    d.SalesHeader.SalesDate < endDate)
                .AsQueryable();

            if (effectiveBranchId.HasValue)
                query = query.Where(d => d.SalesHeader!.BranchId == effectiveBranchId);

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLower();

                query = query.Where(d =>
                    d.SalesHeader!.SalesNumber.ToLower().Contains(term) ||
                    (d.Item != null && d.Item.ItemName.ToLower().Contains(term)) ||
                    (d.Item != null && d.Item.ItemCode.ToLower().Contains(term)));
            }

            var totalRecords = await query.CountAsync();

            pageNumber = PagedResult<object>.ValidatePageNumber(
                pageNumber,
                (int)Math.Ceiling(totalRecords / (double)pageSize));

            var rows = await query
                .OrderByDescending(d => d.SalesHeader!.SalesDate)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .Select(d => new ProfitReportViewModel
                {
                    SalesNumber = d.SalesHeader!.SalesNumber,
                    SalesDate = d.SalesHeader.SalesDate,

                    ItemCode = d.Item != null ? d.Item.ItemCode : "N/A",
                    ItemName = d.Item != null ? d.Item.ItemName : "N/A",

                    QuantitySold = d.Quantity,
                    SellingPrice = d.UnitPrice,
                    CostPrice = d.Item != null ? d.Item.CostPrice : 0,

                    SalesAmount = d.LineTotal,
                    CostAmount = d.Item != null ? d.Quantity * d.Item.CostPrice : 0,

                    GrossProfit = d.Item != null
                        ? d.LineTotal - (d.Quantity * d.Item.CostPrice)
                        : d.LineTotal,

                    ProfitMarginPercent = d.LineTotal > 0 && d.Item != null
                        ? ((d.LineTotal - (d.Quantity * d.Item.CostPrice)) / d.LineTotal) * 100
                        : 0
                })
                .ToListAsync();

            ViewBag.DateFrom = from;
            ViewBag.DateTo = to;

            return View(new PagedResult<ProfitReportViewModel>
            {
                Items = rows,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalRecords = totalRecords,
                SearchTerm = searchTerm
            });
        }

        public async Task<IActionResult> Analytics(
    DateTime? dateFrom,
    DateTime? dateTo)
        {
            var tenantId = await GetReportTenantIdAsync();

            var from = dateFrom?.Date ?? DateTime.Today.AddDays(-30);
            var to = dateTo?.Date ?? DateTime.Today;

            var startDate = from;
            var endDate = to.AddDays(1);

            // ============================================
            // SALES DATA
            // ============================================

            var sales = await ApplyTenantScope(
                    _context.SalesHeaders.AsNoTracking(),
                    tenantId)
                .Where(s =>
                    s.Status == "Completed" &&
                    s.SalesDate >= startDate &&
                    s.SalesDate < endDate)
                .ToListAsync();

            // ============================================
            // DAILY SALES
            // ============================================

            var dailySales = sales
                .GroupBy(s => s.SalesDate.Date)
                .Select(g => new
                {
                    Date = g.Key.ToString("MMM dd"),
                    Total = g.Sum(x => x.TotalAmount)
                })
                .OrderBy(x => x.Date)
                .ToList();

            // ============================================
            // PAYMENT METHODS
            // ============================================

            var paymentMethods = sales
                .GroupBy(s => s.PaymentMethod)
                .Select(g => new
                {
                    Method = g.Key,
                    Total = g.Sum(x => x.TotalAmount)
                })
                .ToList();

            // ============================================
            // TOP SELLING ITEMS
            // ============================================

            var topItems = await ApplyTenantScope(
                    _context.SalesDetails.AsNoTracking(),
                    tenantId)
            .Include(x => x.Item)
            .Include(x => x.SalesHeader)
            .Where(x =>
                x.SalesHeader != null &&
                x.SalesHeader.Status == "Completed" &&
                x.SalesHeader.SalesDate >= startDate &&
                x.SalesHeader.SalesDate < endDate)
            .GroupBy(x => x.Item != null ? x.Item.ItemName : "N/A")
            .Select(g => new
            {
                ItemName = g.Key,
                Qty = g.Sum(x => x.Quantity),
                Total = g.Sum(x => x.LineTotal)
            })
            .OrderByDescending(x => x.Qty)
            .Take(10)
            .ToListAsync();

            // ============================================
            // KPI
            // ============================================

            ViewBag.TotalSales =
                sales.Sum(x => x.TotalAmount);

            ViewBag.TotalTransactions =
                sales.Count;

            ViewBag.AverageSale =
                sales.Any()
                    ? sales.Average(x => x.TotalAmount)
                    : 0;

            ViewBag.DateFrom = from;
            ViewBag.DateTo = to;

            // ============================================
            // CHART DATA
            // ============================================

            ViewBag.DailyLabels =
                dailySales.Select(x => x.Date).ToList();

            ViewBag.DailyTotals =
                dailySales.Select(x => x.Total).ToList();

            ViewBag.PaymentLabels =
                paymentMethods.Select(x => x.Method).ToList();

            ViewBag.PaymentTotals =
                paymentMethods.Select(x => x.Total).ToList();

            ViewBag.TopItems = topItems;

            return View();
        }

        /// <summary>
        /// Legacy route — redirects to the Phase 4.6 split reports.
        /// FastMovingItems and SlowMovingItems are the canonical replacements.
        /// Kept so that any bookmarked /Reports/FastSlowMoving URLs remain functional.
        /// </summary>
        public IActionResult FastSlowMoving(
            DateTime? dateFrom,
            DateTime? dateTo,
            string movementType = "FAST",
            int? branchId = null,
            int pageNumber = 1,
            int pageSize = 10)
        {
            if (movementType?.ToUpperInvariant() == "SLOW")
                return RedirectToAction(nameof(SlowMovingItems),
                    new { branchId, thresholdDays = 90 });

            return RedirectToAction(nameof(FastMovingItems),
                new { branchId, dateFrom, dateTo });
        }

        public async Task<IActionResult> CashierPerformance(
    DateTime? dateFrom,
    DateTime? dateTo,
    string? searchTerm = null,
    int pageNumber = 1,
    int pageSize = 10)
        {
            var tenantId = await GetReportTenantIdAsync();

            pageSize = PagedResult<object>.ValidatePageSize(pageSize);

            var from = dateFrom?.Date ?? DateTime.Today;
            var to = dateTo?.Date ?? DateTime.Today;

            var startDate = from;
            var endDate = to.AddDays(1);

            var query = ApplyTenantScope(
                    _context.SalesHeaders.AsNoTracking(),
                    tenantId)
                .Where(s =>
                    s.SalesDate >= startDate &&
                    s.SalesDate < endDate)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLower();

                query = query.Where(s =>
                    (s.CashierName ?? "Unknown").ToLower().Contains(term));
            }

            var groupedQuery = query
                .GroupBy(s => s.CashierName ?? "Unknown")
                .Select(g => new CashierPerformanceReportViewModel
                {
                    CashierName = g.Key,

                    TransactionCount = g.Count(x => x.Status == "Completed"),

                    GrossSales = g
                        .Where(x => x.Status == "Completed")
                        .Sum(x => x.SubTotal),

                    TotalDiscounts = g
                        .Where(x => x.Status == "Completed")
                        .Sum(x => x.DiscountAmount),

                    NetSales = g
                        .Where(x => x.Status == "Completed")
                        .Sum(x => x.TotalAmount),

                    CashSales = g
                        .Where(x => x.Status == "Completed" &&
                                    x.PaymentMethod == "Cash")
                        .Sum(x => x.TotalAmount),

                    GCashSales = g
                        .Where(x => x.Status == "Completed" &&
                                    x.PaymentMethod == "GCash")
                        .Sum(x => x.TotalAmount),

                    CreditSales = g
                        .Where(x => x.Status == "Completed" &&
                                    x.PaymentMethod == "Credit")
                        .Sum(x => x.TotalAmount),

                    VoidCount = g.Count(x => x.Status == "Voided"),

                    VoidAmount = g
                        .Where(x => x.Status == "Voided")
                        .Sum(x => x.TotalAmount),

                    AverageSale = g.Any(x => x.Status == "Completed")
                        ? g.Where(x => x.Status == "Completed")
                           .Average(x => x.TotalAmount)
                        : 0
                });

            var totalRecords = await groupedQuery.CountAsync();

            pageNumber = PagedResult<object>.ValidatePageNumber(
                pageNumber,
                (int)Math.Ceiling(totalRecords / (double)pageSize));

            var rows = await groupedQuery
                .OrderByDescending(x => x.NetSales)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.DateFrom = from;
            ViewBag.DateTo = to;

            return View(new PagedResult<CashierPerformanceReportViewModel>
            {
                Items = rows,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalRecords = totalRecords,
                SearchTerm = searchTerm
            });
        }

        public async Task<IActionResult> AgingReceivables(
    string? searchTerm = null,
    int pageNumber = 1,
    int pageSize = 10)
        {
            var tenantId = await GetReportTenantIdAsync();

            pageSize = PagedResult<object>.ValidatePageSize(pageSize);

            var today = DateTime.Today;

            var query = ApplyCustomerTenantScope(
                    _context.Customers.AsNoTracking(),
                    tenantId)
                .Where(c => c.IsActive)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLower();

                query = query.Where(c =>
                    c.CustomerName.ToLower().Contains(term) ||
                    (c.ContactNumber != null && c.ContactNumber.ToLower().Contains(term)));
            }

            var rowsQuery = query
                .Select(c => new AgingReceivableReportViewModel
                {
                    CustomerName = c.CustomerName,

                    TotalBalance = ApplyTenantScope(_context.CustomerLedgers, tenantId)
                        .Where(l => l.CustomerId == c.Id)
                        .OrderByDescending(l => l.Id)
                        .Select(l => l.RunningBalance)
                        .FirstOrDefault(),

                    CurrentBalance = ApplyTenantScope(_context.CustomerLedgers, tenantId)
                        .Where(l =>
                            l.CustomerId == c.Id &&
                            l.TransactionType == "CHARGE" &&
                            EF.Functions.DateDiffDay(l.TransactionDate, today) <= 30)
                        .Sum(l => l.DebitAmount),

                    Days30 = ApplyTenantScope(_context.CustomerLedgers, tenantId)
                        .Where(l =>
                            l.CustomerId == c.Id &&
                            l.TransactionType == "CHARGE" &&
                            EF.Functions.DateDiffDay(l.TransactionDate, today) >= 31 &&
                            EF.Functions.DateDiffDay(l.TransactionDate, today) <= 60)
                        .Sum(l => l.DebitAmount),

                    Days60 = ApplyTenantScope(_context.CustomerLedgers, tenantId)
                        .Where(l =>
                            l.CustomerId == c.Id &&
                            l.TransactionType == "CHARGE" &&
                            EF.Functions.DateDiffDay(l.TransactionDate, today) >= 61 &&
                            EF.Functions.DateDiffDay(l.TransactionDate, today) <= 90)
                        .Sum(l => l.DebitAmount),

                    Over90Days = ApplyTenantScope(_context.CustomerLedgers, tenantId)
                        .Where(l =>
                            l.CustomerId == c.Id &&
                            l.TransactionType == "CHARGE" &&
                            EF.Functions.DateDiffDay(l.TransactionDate, today) > 90)
                        .Sum(l => l.DebitAmount),

                    LastTransactionDate = ApplyTenantScope(_context.CustomerLedgers, tenantId)
                        .Where(l => l.CustomerId == c.Id)
                        .OrderByDescending(l => l.Id)
                        .Select(l => (DateTime?)l.TransactionDate)
                        .FirstOrDefault()
                })
                .Where(x => x.TotalBalance > 0);

            var totalRecords = await rowsQuery.CountAsync();

            pageNumber = PagedResult<object>.ValidatePageNumber(
                pageNumber,
                (int)Math.Ceiling(totalRecords / (double)pageSize));

            var rows = await rowsQuery
                .OrderByDescending(x => x.TotalBalance)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            foreach (var row in rows)
            {
                row.Status = row.Over90Days > 0
                    ? "Critical"
                    : row.Days60 > 0
                        ? "Overdue"
                        : row.Days30 > 0
                            ? "Due Soon"
                            : "Current";
            }

            ViewBag.TotalBalance = rows.Sum(x => x.TotalBalance);
            ViewBag.CurrentBalance = rows.Sum(x => x.CurrentBalance);
            ViewBag.OverdueBalance = rows.Sum(x => x.Days30 + x.Days60 + x.Over90Days);
            ViewBag.CriticalBalance = rows.Sum(x => x.Over90Days);

            return View(new PagedResult<AgingReceivableReportViewModel>
            {
                Items = rows,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalRecords = totalRecords,
                SearchTerm = searchTerm
            });
        }

        public async Task<IActionResult> SupplierPayables(
    DateTime? dateFrom,
    DateTime? dateTo,
    string? searchTerm = null,
    string? paymentStatus = null,
    int pageNumber = 1,
    int pageSize = 10)
        {
            var tenantId = await GetReportTenantIdAsync();

            pageSize = PagedResult<object>.ValidatePageSize(pageSize);

            var from = dateFrom?.Date ?? DateTime.Today.AddMonths(-1);
            var to = dateTo?.Date ?? DateTime.Today;

            var startDate = from;
            var endDate = to.AddDays(1);
            var today = DateTime.Today;

            var query = ApplyTenantScope(
                    _context.StockInHeaders.AsNoTracking(),
                    tenantId)
                .Include(s => s.Supplier)
                .Where(s =>
                    s.DateReceived >= startDate &&
                    s.DateReceived < endDate)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLower();

                query = query.Where(s =>
                    s.StockInNumber.ToLower().Contains(term) ||
                    (s.InvoiceNumber != null && s.InvoiceNumber.ToLower().Contains(term)) ||
                    (s.Supplier != null && s.Supplier.SupplierName.ToLower().Contains(term)));
            }

            if (!string.IsNullOrWhiteSpace(paymentStatus))
            {
                query = query.Where(s => s.PaymentStatus == paymentStatus);
            }

            var rowsQuery = query
                .Select(s => new SupplierPayableReportViewModel
                {
                    StockInId = s.Id,
                    StockInNumber = s.StockInNumber,
                    SupplierName = s.Supplier != null
                        ? s.Supplier.SupplierName
                        : "N/A",
                    InvoiceNumber = s.InvoiceNumber,
                    DateReceived = s.DateReceived,
                    DueDate = s.DueDate,
                    TotalCost = s.TotalCost,
                    AmountPaid = s.AmountPaid,
                    BalanceDue = s.BalanceDue,
                    PaymentStatus = s.PaymentStatus,
                    PaymentMethod = s.PaymentMethod,
                    AgingDays = s.DueDate.HasValue
                        ? EF.Functions.DateDiffDay(s.DueDate.Value, today)
                        : 0
                });

            var totalRecords = await rowsQuery.CountAsync();

            pageNumber = PagedResult<object>.ValidatePageNumber(
                pageNumber,
                (int)Math.Ceiling(totalRecords / (double)pageSize));

            var rows = await rowsQuery
                .OrderByDescending(x => x.BalanceDue)
                .ThenBy(x => x.DueDate)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.DateFrom = from;
            ViewBag.DateTo = to;
            ViewBag.PaymentStatus = paymentStatus;

            ViewBag.TotalPayables = rows.Sum(x => x.TotalCost);
            ViewBag.TotalPaid = rows.Sum(x => x.AmountPaid);
            ViewBag.TotalBalance = rows.Sum(x => x.BalanceDue);
            ViewBag.OverdueBalance = rows
                .Where(x => x.BalanceDue > 0 && x.AgingDays > 0)
                .Sum(x => x.BalanceDue);

            return View(new PagedResult<SupplierPayableReportViewModel>
            {
                Items = rows,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalRecords = totalRecords,
                SearchTerm = searchTerm
            });
        }

        public async Task<IActionResult> SupplierPaymentHistory(
    DateTime? dateFrom,
    DateTime? dateTo,
    string? searchTerm = null,
    string? paymentMethod = null,
    int pageNumber = 1,
    int pageSize = 10)
        {
            var tenantId = await GetReportTenantIdAsync();

            pageSize = PagedResult<object>.ValidatePageSize(pageSize);

            var from = dateFrom?.Date ?? DateTime.Today.AddMonths(-1);
            var to = dateTo?.Date ?? DateTime.Today;

            var startDate = from;
            var endDate = to.AddDays(1);

            var query = ApplyTenantScope(
                    _context.SupplierPayments.AsNoTracking(),
                    tenantId)
                .Include(p => p.Supplier)
                .Include(p => p.StockInHeader)
                .Where(p =>
                    p.PaymentDate >= startDate &&
                    p.PaymentDate < endDate)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLower();

                query = query.Where(p =>
                    (p.StockInHeader != null && p.StockInHeader.StockInNumber.ToLower().Contains(term)) ||
                    (p.Supplier != null && p.Supplier.SupplierName.ToLower().Contains(term)) ||
                    (p.ReferenceNumber != null && p.ReferenceNumber.ToLower().Contains(term)) ||
                    (p.CreatedBy != null && p.CreatedBy.ToLower().Contains(term)));
            }

            if (!string.IsNullOrWhiteSpace(paymentMethod))
            {
                query = query.Where(p => p.PaymentMethod == paymentMethod);
            }

            var totalRecords = await query.CountAsync();

            pageNumber = PagedResult<object>.ValidatePageNumber(
                pageNumber,
                (int)Math.Ceiling(totalRecords / (double)pageSize));

            var rows = await query
                .OrderByDescending(p => p.PaymentDate)
                .ThenByDescending(p => p.Id)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .Select(p => new SupplierPaymentHistoryReportViewModel
                {
                    PaymentDate = p.PaymentDate,
                    StockInNumber = p.StockInHeader != null
                        ? p.StockInHeader.StockInNumber
                        : "N/A",
                    SupplierName = p.Supplier != null
                        ? p.Supplier.SupplierName
                        : "N/A",
                    AmountPaid = p.AmountPaid,
                    PaymentMethod = p.PaymentMethod,
                    ReferenceNumber = p.ReferenceNumber,
                    Remarks = p.Remarks,
                    CreatedBy = p.CreatedBy
                })
                .ToListAsync();

            ViewBag.DateFrom = from;
            ViewBag.DateTo = to;
            ViewBag.PaymentMethod = paymentMethod;

            ViewBag.TotalPaid = await query.SumAsync(p => p.AmountPaid);

            return View(new PagedResult<SupplierPaymentHistoryReportViewModel>
            {
                Items = rows,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalRecords = totalRecords,
                SearchTerm = searchTerm
            });
        }

        public async Task<IActionResult> ExpenseVsProfit(
    DateTime? dateFrom,
    DateTime? dateTo,
    int? branchId = null)
        {
            var tenantId = await GetReportTenantIdAsync();

            var from = dateFrom?.Date ?? DateTime.Today;
            var to = dateTo?.Date ?? DateTime.Today;

            var startDate = from;
            var endDate = to.AddDays(1);

            // Branch resolution
            var isGlobal = _branchService.IsGlobalUser(User);
            var allBranches = await _branchService.GetAllActiveBranchesAsync();
            var currentBranch = await _branchService.GetCurrentBranchAsync(User);
            int? effectiveBranchId = isGlobal
                ? (branchId.HasValue && branchId > 0 ? branchId : null)
                : currentBranch?.Id;

            string? effectiveBranchName = effectiveBranchId.HasValue
                ? allBranches.FirstOrDefault(b => b.Id == effectiveBranchId)?.Name
                : null;

            ViewBag.AllBranches = allBranches;
            ViewBag.IsGlobalUser = isGlobal;

            var completedSales = ApplyTenantScope(
                    _context.SalesHeaders.AsNoTracking(),
                    tenantId)
                .Where(s =>
                    s.Status == "Completed" &&
                    s.SalesDate >= startDate &&
                    s.SalesDate < endDate);

            if (effectiveBranchId.HasValue)
                completedSales = completedSales.Where(s => s.BranchId == effectiveBranchId);

            var salesDetails = ApplyTenantScope(
                    _context.SalesDetails.AsNoTracking(),
                    tenantId)
                .Include(d => d.SalesHeader)
                .Include(d => d.Item)
                .Where(d =>
                    d.SalesHeader != null &&
                    d.SalesHeader.Status == "Completed" &&
                    d.SalesHeader.SalesDate >= startDate &&
                    d.SalesHeader.SalesDate < endDate);

            if (effectiveBranchId.HasValue)
                salesDetails = salesDetails.Where(d => d.SalesHeader!.BranchId == effectiveBranchId);

            var expensesQuery = ApplyTenantScope(
                    _context.Expenses.AsNoTracking(),
                    tenantId)
                .Where(e =>
                    e.ExpenseDate >= startDate &&
                    e.ExpenseDate < endDate);

            if (effectiveBranchId.HasValue)
                expensesQuery = expensesQuery.Where(e => e.BranchId == effectiveBranchId);

            var grossSales = await completedSales.SumAsync(s => s.TotalAmount);

            var costOfGoods = await salesDetails
                .SumAsync(d =>
                    d.Item != null
                        ? d.Quantity * d.Item.CostPrice
                        : 0);

            var grossProfit = grossSales - costOfGoods;
            var operatingExpenses = await expensesQuery.SumAsync(e => e.Amount);
            var netProfit = grossProfit - operatingExpenses;

            var model = new ExpenseVsProfitReportViewModel
            {
                BranchId = effectiveBranchId,
                BranchName = effectiveBranchName,
                GrossSales = grossSales,
                CostOfGoods = costOfGoods,
                GrossProfit = grossProfit,
                OperatingExpenses = operatingExpenses,
                NetProfit = netProfit,
                ProfitMarginPercent = grossSales > 0
                    ? (netProfit / grossSales) * 100
                    : 0
            };

            ViewBag.DateFrom = from;
            ViewBag.DateTo = to;

            return View(model);
        }

        public async Task<IActionResult> VatTaxSummary(
    DateTime? dateFrom,
    DateTime? dateTo)
        {
            var tenantId = await GetReportTenantIdAsync();

            var from = dateFrom?.Date ?? DateTime.Today;
            var to = dateTo?.Date ?? DateTime.Today;

            var startDate = from;
            var endDate = to.AddDays(1);

            var settings = await _context.SystemSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.TenantId == tenantId)
                ?? new SystemSetting();

            var taxMode = settings.TaxMode;

            var sales = await ApplyTenantScope(
                    _context.SalesHeaders.AsNoTracking(),
                    tenantId)
                .Where(s =>
                    s.Status == "Completed" &&
                    s.SalesDate >= startDate &&
                    s.SalesDate < endDate)
                .ToListAsync();

            var grossSales = sales.Sum(s => s.TotalAmount);
            var vatAmount = (taxMode == "VAT" || taxMode == "MANUAL")
                ? sales.Sum(s => s.VatAmount)
                : 0;

            var netOfVatSales = (taxMode == "VAT" || taxMode == "MANUAL")
                ? grossSales - vatAmount
                : grossSales;

            var model = new VatTaxSummaryViewModel
            {
                GrossSales = grossSales,
                VatSales = taxMode == "VAT" ? grossSales : 0,
                VatAmount = vatAmount,
                VatExemptSales = taxMode == "NONVAT" ? grossSales : 0,
                ZeroRatedSales = 0,
                NetOfVatSales = netOfVatSales
            };

            ViewBag.DateFrom = from;
            ViewBag.DateTo = to;
            ViewBag.TaxMode = taxMode;

            return View(model);
        }

        public async Task<IActionResult> ExportVatTaxSummaryPdf(
            DateTime? dateFrom,
            DateTime? dateTo)
                {
                    var tenantId = await GetReportTenantIdAsync();

                    var from = dateFrom?.Date ?? DateTime.Today;
                    var to = dateTo?.Date ?? DateTime.Today;

                    var startDate = from;
                    var endDate = to.AddDays(1);

                    var settings = await _context.SystemSettings
                        .AsNoTracking()
                        .FirstOrDefaultAsync(s => s.TenantId == tenantId)
                        ?? new SystemSetting();

                    var taxMode = settings?.TaxMode ?? "VAT";

                    var sales = await ApplyTenantScope(
                            _context.SalesHeaders.AsNoTracking(),
                            tenantId)
                        .Where(s =>
                            s.Status == "Completed" &&
                            s.SalesDate >= startDate &&
                            s.SalesDate < endDate)
                        .ToListAsync();

                    var grossSales = sales.Sum(s => s.TotalAmount);

                    var vatAmount = taxMode == "VAT"
                        ? sales.Sum(s => s.VatAmount)
                        : 0;

                    var netSales = grossSales - vatAmount;

                    var headers = new List<string>
            {
                "Field",
                "Value"
            };

                    var rows = new List<List<string>>
            {
                new() { "Gross Sales", grossSales.ToString("N2") },
                new() { "VAT Amount", vatAmount.ToString("N2") },
                new() { "Net Sales", netSales.ToString("N2") },
                new() { "Tax Mode", taxMode }
            };

            var pdfBytes = _reportPdfService.GenerateSimpleReportPdf(
                "Tax Summary Report",
                        $"{from:MMM dd, yyyy} - {to:MMM dd, yyyy}",
                        headers,
                        rows);

                    return File(
                        pdfBytes,
                        "application/pdf",
                        $"TaxSummary_{DateTime.Now:yyyyMMddHHmmss}.pdf");
        }

        public async Task<IActionResult> ExportSalesDetailPdf(
            DateTime? dateFrom,
            DateTime? dateTo,
            string? searchTerm = null,
            string? paymentMethod = null,
            string? status = null)
                {
                    var tenantId = await GetReportTenantIdAsync();

                    var from = dateFrom?.Date ?? DateTime.Today;
                    var to = dateTo?.Date ?? DateTime.Today;

                    var startDate = from;
                    var endDate = to.AddDays(1);

                    var query = ApplyTenantScope(
                            _context.SalesHeaders.AsNoTracking(),
                            tenantId)
                        .Include(s => s.Customer)
                        .Where(s =>
                            s.SalesDate >= startDate &&
                            s.SalesDate < endDate)
                        .AsQueryable();

                    if (!string.IsNullOrWhiteSpace(searchTerm))
                    {
                        var term = searchTerm.Trim().ToLower();

                        query = query.Where(s =>
                            s.SalesNumber.ToLower().Contains(term) ||
                            (s.Customer != null &&
                             s.Customer.CustomerName.ToLower().Contains(term)) ||
                            (s.CashierName != null &&
                             s.CashierName.ToLower().Contains(term)));
                    }

                    if (!string.IsNullOrWhiteSpace(paymentMethod))
                    {
                        query = query.Where(s =>
                            s.PaymentMethod == paymentMethod);
                    }

                    if (!string.IsNullOrWhiteSpace(status))
                    {
                        query = query.Where(s =>
                            s.Status == status);
                    }

                    var sales = await query
                        .OrderByDescending(s => s.SalesDate)
                        .ToListAsync();

                    var headers = new List<string>
            {
                "Receipt #",
                "Date",
                "Customer",
                "Payment",
                "Total",
                "Status"
            };

                    var rows = sales
                        .Select(s => new List<string>
                        {
                    s.SalesNumber,
                    s.SalesDate.ToString("MMM dd, yyyy"),
                    s.Customer != null
                        ? s.Customer.CustomerName
                        : "Walk-in Customer",
                    s.PaymentMethod,
                    s.TotalAmount.ToString("N2"),
                    s.Status
                        })
                        .ToList();

                    var pdfBytes =
                        _reportPdfService.GenerateSimpleReportPdf(
                            "Sales Detail Report",
                            $"{from:MMM dd, yyyy} - {to:MMM dd, yyyy}",
                            headers,
                            rows);

                    return File(
                        pdfBytes,
                        "application/pdf",
                        $"SalesDetail_{DateTime.Now:yyyyMMddHHmmss}.pdf");
        }
        public async Task<IActionResult> ExportProfitReportPdf(
    DateTime? dateFrom,
    DateTime? dateTo,
    string? searchTerm = null)
        {
            var tenantId = await GetReportTenantIdAsync();

            var from = dateFrom?.Date ?? DateTime.Today;
            var to = dateTo?.Date ?? DateTime.Today;

            var startDate = from;
            var endDate = to.AddDays(1);

            var query = ApplyTenantScope(
                    _context.SalesDetails.AsNoTracking(),
                    tenantId)
                .Include(d => d.SalesHeader)
                .Include(d => d.Item)
                .Where(d =>
                    d.SalesHeader != null &&
                    d.SalesHeader.Status == "Completed" &&
                    d.SalesHeader.SalesDate >= startDate &&
                    d.SalesHeader.SalesDate < endDate)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLower();

                query = query.Where(d =>
                    d.SalesHeader!.SalesNumber.ToLower().Contains(term) ||
                    (d.Item != null && d.Item.ItemName.ToLower().Contains(term)) ||
                    (d.Item != null && d.Item.ItemCode.ToLower().Contains(term)));
            }

            var rowsData = await query
                .OrderByDescending(d => d.SalesHeader!.SalesDate)
                .ToListAsync();

            var headers = new List<string>
    {
        "Receipt #",
        "Date",
        "Item",
        "Sales",
        "Cost",
        "Profit",
        "Margin"
    };

            var rows = rowsData.Select(d =>
            {
                var salesAmount = d.LineTotal;
                var costAmount = d.Item != null ? d.Quantity * d.Item.CostPrice : 0;
                var grossProfit = salesAmount - costAmount;
                var margin = salesAmount > 0 ? (grossProfit / salesAmount) * 100 : 0;

                return new List<string>
        {
            d.SalesHeader?.SalesNumber ?? "N/A",
            d.SalesHeader?.SalesDate.ToString("MMM dd, yyyy") ?? "N/A",
            d.Item?.ItemName ?? "N/A",
            salesAmount.ToString("N2"),
            costAmount.ToString("N2"),
            grossProfit.ToString("N2"),
            margin.ToString("N2") + "%"
        };
            }).ToList();

            var pdfBytes = _reportPdfService.GenerateSimpleReportPdf(
                "Profit Report",
                $"{from:MMM dd, yyyy} - {to:MMM dd, yyyy}",
                headers,
                rows);

            return File(
                pdfBytes,
                "application/pdf",
                $"ProfitReport_{DateTime.Now:yyyyMMddHHmmss}.pdf");
        }

        public async Task<IActionResult> ExportCashierPerformancePdf(
    DateTime? dateFrom,
    DateTime? dateTo,
    string? searchTerm = null)
        {
            var tenantId = await GetReportTenantIdAsync();

            var from = dateFrom?.Date ?? DateTime.Today;
            var to = dateTo?.Date ?? DateTime.Today;

            var startDate = from;
            var endDate = to.AddDays(1);

            var query = ApplyTenantScope(
                    _context.SalesHeaders.AsNoTracking(),
                    tenantId)
                .Where(s =>
                    s.SalesDate >= startDate &&
                    s.SalesDate < endDate);

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLower();

                query = query.Where(s =>
                    (s.CashierName ?? "Unknown").ToLower().Contains(term));
            }

            var rowsData = await query
                .GroupBy(s => s.CashierName ?? "Unknown")
                .Select(g => new
                {
                    CashierName = g.Key,
                    Transactions = g.Count(x => x.Status == "Completed"),
                    GrossSales = g.Where(x => x.Status == "Completed").Sum(x => x.SubTotal),
                    Discounts = g.Where(x => x.Status == "Completed").Sum(x => x.DiscountAmount),
                    NetSales = g.Where(x => x.Status == "Completed").Sum(x => x.TotalAmount),
                    Cash = g.Where(x => x.Status == "Completed" && x.PaymentMethod == "Cash").Sum(x => x.TotalAmount),
                    GCash = g.Where(x => x.Status == "Completed" && x.PaymentMethod == "GCash").Sum(x => x.TotalAmount),
                    Credit = g.Where(x => x.Status == "Completed" && x.PaymentMethod == "Credit").Sum(x => x.TotalAmount),
                    Voids = g.Count(x => x.Status == "Voided")
                })
                .OrderByDescending(x => x.NetSales)
                .ToListAsync();

            var headers = new List<string>
    {
        "Cashier",
        "Txns",
        "Gross",
        "Discount",
        "Net",
        "Cash",
        "GCash",
        "Credit",
        "Voids"
    };

            var rows = rowsData.Select(x => new List<string>
    {
        x.CashierName,
        x.Transactions.ToString(),
        x.GrossSales.ToString("N2"),
        x.Discounts.ToString("N2"),
        x.NetSales.ToString("N2"),
        x.Cash.ToString("N2"),
        x.GCash.ToString("N2"),
        x.Credit.ToString("N2"),
        x.Voids.ToString()
    }).ToList();

            var pdfBytes = _reportPdfService.GenerateSimpleReportPdf(
                "Cashier Performance Report",
                $"{from:MMM dd, yyyy} - {to:MMM dd, yyyy}",
                headers,
                rows);

            return File(
                pdfBytes,
                "application/pdf",
                $"CashierPerformance_{DateTime.Now:yyyyMMddHHmmss}.pdf");
        }

        public async Task<IActionResult> ExportSupplierPayablesPdf(
    DateTime? dateFrom,
    DateTime? dateTo,
    string? searchTerm = null,
    string? paymentStatus = null)
        {
            var tenantId = await GetReportTenantIdAsync();

            var from = dateFrom?.Date ?? DateTime.Today.AddMonths(-1);
            var to = dateTo?.Date ?? DateTime.Today;

            var startDate = from;
            var endDate = to.AddDays(1);
            var today = DateTime.Today;

            var query = ApplyTenantScope(
                    _context.StockInHeaders.AsNoTracking(),
                    tenantId)
                .Include(s => s.Supplier)
                .Where(s =>
                    s.DateReceived >= startDate &&
                    s.DateReceived < endDate);

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLower();

                query = query.Where(s =>
                    s.StockInNumber.ToLower().Contains(term) ||
                    (s.InvoiceNumber != null && s.InvoiceNumber.ToLower().Contains(term)) ||
                    (s.Supplier != null && s.Supplier.SupplierName.ToLower().Contains(term)));
            }

            if (!string.IsNullOrWhiteSpace(paymentStatus))
            {
                query = query.Where(s => s.PaymentStatus == paymentStatus);
            }

            var rowsData = await query
                .OrderByDescending(s => s.BalanceDue)
                .ThenBy(s => s.DueDate)
                .Select(s => new
                {
                    StockInNumber = s.StockInNumber,
                    SupplierName = s.Supplier != null ? s.Supplier.SupplierName : "N/A",
                    InvoiceNumber = s.InvoiceNumber ?? "N/A",
                    DateReceived = s.DateReceived,
                    DueDate = s.DueDate,
                    TotalCost = s.TotalCost,
                    AmountPaid = s.AmountPaid,
                    BalanceDue = s.BalanceDue,
                    PaymentStatus = s.PaymentStatus,
                    AgingDays = s.DueDate.HasValue
                        ? EF.Functions.DateDiffDay(s.DueDate.Value, today)
                        : 0
                })
                .ToListAsync();

            var headers = new List<string>
    {
        "Stock-In #",
        "Supplier",
        "Invoice",
        "Date",
        "Due",
        "Total",
        "Paid",
        "Balance",
        "Status",
        "Aging"
    };

            var rows = rowsData.Select(x => new List<string>
    {
        x.StockInNumber,
        x.SupplierName,
        x.InvoiceNumber,
        x.DateReceived.ToString("MMM dd, yyyy"),
        x.DueDate.HasValue ? x.DueDate.Value.ToString("MMM dd, yyyy") : "-",
        x.TotalCost.ToString("N2"),
        x.AmountPaid.ToString("N2"),
        x.BalanceDue.ToString("N2"),
        x.PaymentStatus,
        x.BalanceDue <= 0
            ? "Cleared"
            : x.AgingDays > 0
                ? $"{x.AgingDays} days overdue"
                : "Current"
    }).ToList();

            var pdfBytes = _reportPdfService.GenerateSimpleReportPdf(
                "Supplier Payables Report",
                $"{from:MMM dd, yyyy} - {to:MMM dd, yyyy}",
                headers,
                rows);

            return File(
                pdfBytes,
                "application/pdf",
                $"SupplierPayables_{DateTime.Now:yyyyMMddHHmmss}.pdf");
        }

        public async Task<IActionResult> ExportSupplierPaymentHistoryPdf(
    DateTime? dateFrom,
    DateTime? dateTo,
    string? searchTerm = null,
    string? paymentMethod = null)
        {
            var tenantId = await GetReportTenantIdAsync();

            var from = dateFrom?.Date ?? DateTime.Today.AddMonths(-1);
            var to = dateTo?.Date ?? DateTime.Today;

            var startDate = from;
            var endDate = to.AddDays(1);

            var query = ApplyTenantScope(
                    _context.SupplierPayments.AsNoTracking(),
                    tenantId)
                .Include(p => p.Supplier)
                .Include(p => p.StockInHeader)
                .Where(p =>
                    p.PaymentDate >= startDate &&
                    p.PaymentDate < endDate)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLower();

                query = query.Where(p =>
                    (p.StockInHeader != null && p.StockInHeader.StockInNumber.ToLower().Contains(term)) ||
                    (p.Supplier != null && p.Supplier.SupplierName.ToLower().Contains(term)) ||
                    (p.ReferenceNumber != null && p.ReferenceNumber.ToLower().Contains(term)) ||
                    (p.CreatedBy != null && p.CreatedBy.ToLower().Contains(term)));
            }

            if (!string.IsNullOrWhiteSpace(paymentMethod))
            {
                query = query.Where(p => p.PaymentMethod == paymentMethod);
            }

            var rowsData = await query
                .OrderByDescending(p => p.PaymentDate)
                .ThenByDescending(p => p.Id)
                .Select(p => new
                {
                    PaymentDate = p.PaymentDate,
                    StockInNumber = p.StockInHeader != null ? p.StockInHeader.StockInNumber : "N/A",
                    SupplierName = p.Supplier != null ? p.Supplier.SupplierName : "N/A",
                    AmountPaid = p.AmountPaid,
                    PaymentMethod = p.PaymentMethod,
                    ReferenceNumber = p.ReferenceNumber,
                    Remarks = p.Remarks,
                    CreatedBy = p.CreatedBy
                })
                .ToListAsync();

            var headers = new List<string>
    {
        "Date",
        "Stock-In #",
        "Supplier",
        "Amount Paid",
        "Method",
        "Reference #",
        "Notes",
        "Processed By"
    };

            var rows = rowsData.Select(x => new List<string>
    {
        x.PaymentDate.ToString("MMM dd, yyyy"),
        x.StockInNumber,
        x.SupplierName,
        x.AmountPaid.ToString("N2"),
        x.PaymentMethod ?? "-",
        string.IsNullOrWhiteSpace(x.ReferenceNumber) ? "-" : x.ReferenceNumber,
        string.IsNullOrWhiteSpace(x.Remarks) ? "-" : x.Remarks,
        x.CreatedBy ?? "-"
    }).ToList();

            var pdfBytes = _reportPdfService.GenerateSimpleReportPdf(
                "Supplier Payment History",
                $"{from:MMM dd, yyyy} - {to:MMM dd, yyyy}",
                headers,
                rows);

            return File(
                pdfBytes,
                "application/pdf",
                $"SupplierPaymentHistory_{DateTime.Now:yyyyMMddHHmmss}.pdf");
        }

        public async Task<IActionResult> ExportExpenseVsProfitPdf(
    DateTime? dateFrom,
    DateTime? dateTo)
        {
            var tenantId = await GetReportTenantIdAsync();

            var from = dateFrom?.Date ?? DateTime.Today;
            var to = dateTo?.Date ?? DateTime.Today;

            var startDate = from;
            var endDate = to.AddDays(1);

            var completedSales = ApplyTenantScope(
                    _context.SalesHeaders.AsNoTracking(),
                    tenantId)
                .Where(s =>
                    s.Status == "Completed" &&
                    s.SalesDate >= startDate &&
                    s.SalesDate < endDate);

            var salesDetails = ApplyTenantScope(
                    _context.SalesDetails.AsNoTracking(),
                    tenantId)
                .Include(d => d.SalesHeader)
                .Include(d => d.Item)
                .Where(d =>
                    d.SalesHeader != null &&
                    d.SalesHeader.Status == "Completed" &&
                    d.SalesHeader.SalesDate >= startDate &&
                    d.SalesHeader.SalesDate < endDate);

            var grossSales = await completedSales
                .SumAsync(s => s.TotalAmount);

            var costOfGoods = await salesDetails
                .SumAsync(d =>
                    d.Item != null
                        ? d.Quantity * d.Item.CostPrice
                        : 0);

            var grossProfit = grossSales - costOfGoods;

            var operatingExpenses = await ApplyTenantScope(
                    _context.Expenses.AsNoTracking(),
                    tenantId)
                .Where(e =>
                    e.ExpenseDate >= startDate &&
                    e.ExpenseDate < endDate)
                .SumAsync(e => e.Amount);

            var netProfit = grossProfit - operatingExpenses;

            var profitMargin = grossSales > 0
                ? (netProfit / grossSales) * 100
                : 0;

            var headers = new List<string>
    {
        "Metric",
        "Value"
    };

            var rows = new List<List<string>>
    {
        new() { "Gross Sales", grossSales.ToString("N2") },
        new() { "Cost of Goods", costOfGoods.ToString("N2") },
        new() { "Gross Profit", grossProfit.ToString("N2") },
        new() { "Operating Expenses", operatingExpenses.ToString("N2") },
        new() { "Net Profit", netProfit.ToString("N2") },
        new() { "Profit Margin", profitMargin.ToString("N2") + "%" }
    };

            var pdfBytes = _reportPdfService.GenerateSimpleReportPdf(
                "Expense vs Profit Report",
                $"{from:MMM dd, yyyy} - {to:MMM dd, yyyy}",
                headers,
                rows);

            return File(
                pdfBytes,
                "application/pdf",
                $"ExpenseVsProfit_{DateTime.Now:yyyyMMddHHmmss}.pdf");
        }

        // =====================================================
        // INVENTORY VALUATION (STOCK STATUS REPORT)
        // Branch-aware paged list with Excel export.
        // For WAC category-grouped PDF see InventoryValuationReport.
        // =====================================================

        [PermissionAuthorize("InventoryValuation", "View")]
        public async Task<IActionResult> InventoryValuation(
            string? searchTerm = null,
            string? stockStatus = null,
            int? branchId = null,
            int pageNumber = 1,
            int pageSize = 25)
        {
            var tenantId = await GetReportTenantIdAsync();

            pageSize = PagedResult<object>.ValidatePageSize(pageSize);

            var isGlobal = _branchService.IsGlobalUser(User);
            var allBranches = await _branchService.GetAllActiveBranchesAsync();
            var currentBranch = await _branchService.GetCurrentBranchAsync(User);
            int? effectiveBranchId = isGlobal
                ? (branchId.HasValue && branchId > 0 ? branchId : null)
                : currentBranch?.Id;

            string? effectiveBranchName = effectiveBranchId.HasValue
                ? allBranches.FirstOrDefault(b => b.Id == effectiveBranchId)?.Name
                : null;

            ViewBag.AllBranches = allBranches;
            ViewBag.IsGlobalUser = isGlobal;
            ViewBag.BranchId = effectiveBranchId;
            ViewBag.StockStatus = stockStatus;

            List<InventoryValuationViewModel> rows;

            if (effectiveBranchId.HasValue)
            {
                // Branch-aware: use BranchProductStocks
                var branchStockQuery = ApplyTenantScope(
                        _context.BranchProductStocks.AsNoTracking(),
                        tenantId)
                    .Include(s => s.Product)
                        .ThenInclude(p => p!.Category)
                    .Include(s => s.Product)
                        .ThenInclude(p => p!.Unit)
                    .Where(s => s.BranchId == effectiveBranchId && s.Product != null && s.Product.Status == "Active");

                if (!string.IsNullOrWhiteSpace(searchTerm))
                {
                    var term = searchTerm.Trim().ToLower();
                    branchStockQuery = branchStockQuery.Where(s =>
                        s.Product!.ItemCode.ToLower().Contains(term) ||
                        s.Product!.ItemName.ToLower().Contains(term) ||
                        (s.Product.Category != null && s.Product.Category.CategoryName.ToLower().Contains(term)));
                }

                if (!string.IsNullOrWhiteSpace(stockStatus))
                {
                    if (stockStatus == "out")
                        branchStockQuery = branchStockQuery.Where(s => s.Quantity <= 0);
                    else if (stockStatus == "low")
                        branchStockQuery = branchStockQuery.Where(s => s.Quantity > 0 && s.Quantity <= (s.ReorderLevel ?? s.Product!.ReorderLevel));
                    else if (stockStatus == "normal")
                        branchStockQuery = branchStockQuery.Where(s => s.Quantity > (s.ReorderLevel ?? s.Product!.ReorderLevel));
                }

                var totalRecords = await branchStockQuery.CountAsync();
                pageNumber = PagedResult<object>.ValidatePageNumber(pageNumber, (int)Math.Ceiling(totalRecords / (double)pageSize));

                var branchData = await branchStockQuery
                    .OrderBy(s => s.Product!.ItemName)
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                rows = branchData.Select(s => new InventoryValuationViewModel
                {
                    ItemId = s.ProductId,
                    ItemCode = s.Product?.ItemCode ?? "",
                    ItemName = s.Product?.ItemName ?? "",
                    CategoryName = s.Product?.Category?.CategoryName ?? "N/A",
                    UnitName = s.Product?.Unit?.ShortName ?? "",
                    Quantity = s.Quantity,
                    DamagedQuantity = s.DamagedStock,
                    PhysicalQuantity = s.Quantity + s.DamagedStock,
                    CostPrice = s.Product?.CostPrice ?? 0,
                    SellingPrice = s.Product?.SellingPrice ?? 0,
                    CostValue = s.Quantity * (s.Product?.CostPrice ?? 0),
                    DamagedCostValue = s.DamagedStock * (s.Product?.CostPrice ?? 0),
                    SellingValue = s.Quantity * (s.Product?.SellingPrice ?? 0),
                    ReorderLevel = s.ReorderLevel ?? s.Product?.ReorderLevel ?? 0,
                    StockStatus = s.Quantity <= 0 ? "Out of Stock"
                        : s.Quantity <= (s.ReorderLevel ?? s.Product?.ReorderLevel ?? 0) ? "Low Stock"
                        : "Normal",
                    BranchId = effectiveBranchId,
                    BranchName = effectiveBranchName
                }).ToList();

                var allCostValue = await ApplyTenantScope(
                        _context.BranchProductStocks.AsNoTracking(),
                        tenantId)
                    .Where(s => s.BranchId == effectiveBranchId && s.Product != null)
                    .SumAsync(s => s.Quantity * s.Product!.CostPrice);

                var allSellingValue = await ApplyTenantScope(
                        _context.BranchProductStocks.AsNoTracking(),
                        tenantId)
                    .Where(s => s.BranchId == effectiveBranchId && s.Product != null)
                    .SumAsync(s => s.Quantity * s.Product!.SellingPrice);

                ViewBag.TotalCostValue = allCostValue;
                ViewBag.TotalSellingValue = allSellingValue;
                ViewBag.TotalRecordsAll = totalRecords;

                return View(new PagedResult<InventoryValuationViewModel>
                {
                    Items = rows,
                    PageNumber = pageNumber,
                    PageSize = pageSize,
                    TotalRecords = totalRecords,
                    SearchTerm = searchTerm
                });
            }
            else
            {
                // Global fallback: CurrentStock on Items
                var itemQuery = ApplyTenantScope(
                        _context.Items.AsNoTracking(),
                        tenantId)
                    .Include(i => i.Category)
                    .Include(i => i.Unit)
                    .Where(i => i.Status == "Active");

                if (!string.IsNullOrWhiteSpace(searchTerm))
                {
                    var term = searchTerm.Trim().ToLower();
                    itemQuery = itemQuery.Where(i =>
                        i.ItemCode.ToLower().Contains(term) ||
                        i.ItemName.ToLower().Contains(term) ||
                        (i.Category != null && i.Category.CategoryName.ToLower().Contains(term)));
                }

                if (!string.IsNullOrWhiteSpace(stockStatus))
                {
                    if (stockStatus == "out")
                        itemQuery = itemQuery.Where(i => i.CurrentStock <= 0);
                    else if (stockStatus == "low")
                        itemQuery = itemQuery.Where(i => i.CurrentStock > 0 && i.CurrentStock <= i.ReorderLevel);
                    else if (stockStatus == "normal")
                        itemQuery = itemQuery.Where(i => i.CurrentStock > i.ReorderLevel);
                }

                var totalRecords = await itemQuery.CountAsync();
                pageNumber = PagedResult<object>.ValidatePageNumber(pageNumber, (int)Math.Ceiling(totalRecords / (double)pageSize));

                rows = await itemQuery
                    .OrderBy(i => i.ItemName)
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .Select(i => new InventoryValuationViewModel
                    {
                        ItemId = i.Id,
                        ItemCode = i.ItemCode,
                        ItemName = i.ItemName,
                        CategoryName = i.Category != null ? i.Category.CategoryName : "N/A",
                        UnitName = i.Unit != null ? i.Unit.ShortName : "",
                        Quantity = i.CurrentStock,
                        DamagedQuantity = i.DamagedStock,
                        PhysicalQuantity = i.CurrentStock + i.DamagedStock,
                        CostPrice = i.CostPrice,
                        SellingPrice = i.SellingPrice,
                        CostValue = i.CurrentStock * i.CostPrice,
                        DamagedCostValue = i.DamagedStock * i.CostPrice,
                        SellingValue = i.CurrentStock * i.SellingPrice,
                        ReorderLevel = i.ReorderLevel,
                        StockStatus = i.CurrentStock <= 0 ? "Out of Stock"
                            : i.CurrentStock <= i.ReorderLevel ? "Low Stock"
                            : "Normal"
                    })
                    .ToListAsync();

                ViewBag.TotalCostValue = await ApplyTenantScope(
                        _context.Items.AsNoTracking(),
                        tenantId)
                    .Where(i => i.Status == "Active")
                    .SumAsync(i => i.CurrentStock * i.CostPrice);

                ViewBag.TotalSellingValue = await ApplyTenantScope(
                        _context.Items.AsNoTracking(),
                        tenantId)
                    .Where(i => i.Status == "Active")
                    .SumAsync(i => i.CurrentStock * i.SellingPrice);

                return View(new PagedResult<InventoryValuationViewModel>
                {
                    Items = rows,
                    PageNumber = pageNumber,
                    PageSize = pageSize,
                    TotalRecords = totalRecords,
                    SearchTerm = searchTerm
                });
            }
        }

        [PermissionAuthorize("InventoryValuation", "Export")]
        public async Task<IActionResult> ExportInventoryValuationExcel(
            string? stockStatus = null,
            int? branchId = null)
        {
            var tenantId = await GetReportTenantIdAsync();

            var isGlobal = _branchService.IsGlobalUser(User);
            var allBranches = await _branchService.GetAllActiveBranchesAsync();
            var currentBranch = await _branchService.GetCurrentBranchAsync(User);
            int? effectiveBranchId = isGlobal
                ? (branchId.HasValue && branchId > 0 ? branchId : null)
                : currentBranch?.Id;

            string branchLabel = effectiveBranchId.HasValue
                ? allBranches.FirstOrDefault(b => b.Id == effectiveBranchId)?.Name ?? "Branch"
                : "All Branches";

            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("Inventory Valuation");

            ws.Cell(1, 1).Value = "Inventory Valuation Report";
            ws.Range(1, 1, 1, 8).Merge();
            ws.Range(1, 1, 1, 8).Style.Font.Bold = true;
            ws.Range(1, 1, 1, 8).Style.Font.FontSize = 16;

            ws.Cell(2, 1).Value = $"Branch: {branchLabel}  |  Generated: {DateTime.Now:MMM dd, yyyy hh:mm tt}";
            ws.Range(2, 1, 2, 8).Merge();

            ws.Cell(4, 1).Value = "Code";
            ws.Cell(4, 2).Value = "Product Name";
            ws.Cell(4, 3).Value = "Category";
            ws.Cell(4, 4).Value = "Unit";
            ws.Cell(4, 5).Value = "Quantity";
            ws.Cell(4, 6).Value = "Cost Price";
            ws.Cell(4, 7).Value = "Cost Value";
            ws.Cell(4, 8).Value = "Selling Value";

            var header = ws.Range(4, 1, 4, 8);
            header.Style.Font.Bold = true;
            header.Style.Fill.BackgroundColor = XLColor.LightBlue;

            var row = 5;

            if (effectiveBranchId.HasValue)
            {
                var branchStocks = await ApplyTenantScope(
                        _context.BranchProductStocks.AsNoTracking(),
                        tenantId)
                    .Include(s => s.Product).ThenInclude(p => p!.Category)
                    .Include(s => s.Product).ThenInclude(p => p!.Unit)
                    .Where(s => s.BranchId == effectiveBranchId && s.Product != null && s.Product.Status == "Active")
                    .OrderBy(s => s.Product!.ItemName)
                    .ToListAsync();

                foreach (var s in branchStocks)
                {
                    ws.Cell(row, 1).Value = s.Product?.ItemCode ?? "";
                    ws.Cell(row, 2).Value = s.Product?.ItemName ?? "";
                    ws.Cell(row, 3).Value = s.Product?.Category?.CategoryName ?? "N/A";
                    ws.Cell(row, 4).Value = s.Product?.Unit?.ShortName ?? "";
                    ws.Cell(row, 5).Value = s.Quantity;
                    ws.Cell(row, 6).Value = s.Product?.CostPrice ?? 0;
                    ws.Cell(row, 7).Value = s.Quantity * (s.Product?.CostPrice ?? 0);
                    ws.Cell(row, 8).Value = s.Quantity * (s.Product?.SellingPrice ?? 0);
                    row++;
                }
            }
            else
            {
                var items = await ApplyTenantScope(
                        _context.Items.AsNoTracking(),
                        tenantId)
                    .Include(i => i.Category)
                    .Include(i => i.Unit)
                    .Where(i => i.Status == "Active")
                    .OrderBy(i => i.ItemName)
                    .ToListAsync();

                foreach (var item in items)
                {
                    ws.Cell(row, 1).Value = item.ItemCode;
                    ws.Cell(row, 2).Value = item.ItemName;
                    ws.Cell(row, 3).Value = item.Category?.CategoryName ?? "N/A";
                    ws.Cell(row, 4).Value = item.Unit?.ShortName ?? "";
                    ws.Cell(row, 5).Value = item.CurrentStock;
                    ws.Cell(row, 6).Value = item.CostPrice;
                    ws.Cell(row, 7).Value = item.CurrentStock * item.CostPrice;
                    ws.Cell(row, 8).Value = item.CurrentStock * item.SellingPrice;
                    row++;
                }
            }

            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);

            return File(
                stream.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"InventoryValuation_{DateTime.Now:yyyyMMdd}.xlsx");
        }

        // =====================================================
        // TRANSFER ANALYTICS
        // =====================================================

        public async Task<IActionResult> TransferAnalytics(
            DateTime? dateFrom,
            DateTime? dateTo,
            string? status = null,
            int? branchId = null,
            int pageNumber = 1,
            int pageSize = 25)
        {
            var tenantId = await GetReportTenantIdAsync();

            pageSize = PagedResult<object>.ValidatePageSize(pageSize);

            var from = dateFrom?.Date ?? DateTime.Today.AddDays(-30);
            var to = dateTo?.Date ?? DateTime.Today;
            var startDate = from;
            var endDate = to.AddDays(1);

            var isGlobal = _branchService.IsGlobalUser(User);
            var allBranches = await _branchService.GetAllActiveBranchesAsync();
            var currentBranch = await _branchService.GetCurrentBranchAsync(User);
            int? effectiveBranchId = isGlobal
                ? (branchId.HasValue && branchId > 0 ? branchId : null)
                : currentBranch?.Id;

            ViewBag.AllBranches = allBranches;
            ViewBag.IsGlobalUser = isGlobal;
            ViewBag.BranchId = effectiveBranchId;
            ViewBag.Status = status;
            ViewBag.DateFrom = from;
            ViewBag.DateTo = to;

            var query = ApplyTenantScope(
                    _context.BranchTransfers.AsNoTracking(),
                    tenantId)
                .Include(t => t.FromBranch)
                .Include(t => t.ToBranch)
                .Include(t => t.BranchTransferItems)
                .Where(t => t.CreatedAtUtc >= startDate.ToUniversalTime() && t.CreatedAtUtc < endDate.ToUniversalTime());

            if (effectiveBranchId.HasValue)
                query = query.Where(t => t.FromBranchId == effectiveBranchId || t.ToBranchId == effectiveBranchId);

            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(t => t.Status == status);

            var totalRecords = await query.CountAsync();
            pageNumber = PagedResult<object>.ValidatePageNumber(pageNumber, (int)Math.Ceiling(totalRecords / (double)pageSize));

            var transfers = await query
                .OrderByDescending(t => t.CreatedAtUtc)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var rows = transfers.Select(t => new TransferAnalyticsViewModel
            {
                TransferNumber = t.TransferNumber,
                FromBranch = t.FromBranch?.Name ?? "N/A",
                ToBranch = t.ToBranch?.Name ?? "N/A",
                Status = t.Status,
                CreatedAt = t.CreatedAtUtc.ToLocalTime(),
                CompletedAt = t.CompletedAtUtc?.ToLocalTime(),
                ItemCount = t.BranchTransferItems.Count,
                TotalQuantity = t.BranchTransferItems.Sum(i => i.Quantity),
                CreatedBy = t.CreatedByUserName ?? "Unknown"
            }).ToList();

            // KPI summary
            var allForPeriod = await ApplyTenantScope(
                    _context.BranchTransfers.AsNoTracking(),
                    tenantId)
                .Include(t => t.BranchTransferItems)
                .Where(t => t.CreatedAtUtc >= startDate.ToUniversalTime() && t.CreatedAtUtc < endDate.ToUniversalTime())
                .ToListAsync();

            if (effectiveBranchId.HasValue)
                allForPeriod = allForPeriod.Where(t => t.FromBranchId == effectiveBranchId || t.ToBranchId == effectiveBranchId).ToList();

            ViewBag.TotalTransfers = allForPeriod.Count;
            ViewBag.CompletedTransfers = allForPeriod.Count(t => t.Status == "Completed");
            ViewBag.PendingTransfers = allForPeriod.Count(t => t.Status is "Pending" or "Draft" or "Approved");
            ViewBag.TotalQuantity = allForPeriod.Sum(t => t.BranchTransferItems.Sum(i => i.Quantity));

            // Top transferred products
            var topProducts = await ApplyTenantScope(
                    _context.BranchTransferItems.AsNoTracking(),
                    tenantId)
                .Include(i => i.Product)
                .Include(i => i.BranchTransfer)
                .Where(i =>
                    i.BranchTransfer != null &&
                    i.BranchTransfer.CreatedAtUtc >= startDate.ToUniversalTime() &&
                    i.BranchTransfer.CreatedAtUtc < endDate.ToUniversalTime())
                .GroupBy(i => new { i.ProductId, ItemCode = i.Product != null ? i.Product.ItemCode : "N/A", ItemName = i.Product != null ? i.Product.ItemName : "N/A" })
                .Select(g => new TransferProductRowViewModel
                {
                    ItemCode = g.Key.ItemCode,
                    ItemName = g.Key.ItemName,
                    TotalQuantityTransferred = g.Sum(x => x.Quantity),
                    TransferCount = g.Count()
                })
                .OrderByDescending(x => x.TotalQuantityTransferred)
                .Take(10)
                .ToListAsync();

            ViewBag.TopProducts = topProducts;

            return View(new PagedResult<TransferAnalyticsViewModel>
            {
                Items = rows,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalRecords = totalRecords
            });
        }
    }
}

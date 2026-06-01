using HardwareManagementSystem.Data;
using HardwareManagementSystem.Models;
using HardwareManagementSystem.Services;
using HardwareManagementSystem.Services.TenantDatabases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Text.Json;

namespace HardwareManagementSystem.Controllers
{
    [Authorize]
    [PermissionAuthorize("POS", "View")]
    public class POSController : OperationalDbController
    {
        private readonly AuditService _auditService;
        private readonly NotificationService _notificationService;
        private readonly IMemoryCache _cache;
        private readonly BranchService _branchService;
        private readonly ITenantContext _tenantContext;
        private readonly TenantGuard _tenantGuard;

        public POSController(
            ITenantOperationalContextProvider ctxProvider,
            AuditService auditService,
            NotificationService notificationService,
            IMemoryCache cache,
            BranchService branchService,
            ITenantContext tenantContext,
            TenantGuard tenantGuard)
            : base(ctxProvider)
        {
            _auditService = auditService;
            _notificationService = notificationService;
            _cache = cache;
            _branchService = branchService;
            _tenantContext = tenantContext;
            _tenantGuard = tenantGuard;
        }

        public async Task<IActionResult> Index()
        {
            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            var customersQuery = _context.Customers
                .AsNoTracking()
                .Where(c => c.IsActive);

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
            {
                customersQuery = customersQuery.Where(c => c.TenantId == tenantId || c.TenantId == null);
            }

            ViewBag.Customers = await customersQuery
                .OrderBy(c => c.CustomerName)
                .ToListAsync();

            var currentBranch = await _branchService.GetCurrentBranchAsync(User);

            if (currentBranch != null && !await _tenantGuard.CanAccessTenantAsync(currentBranch.TenantId))
            {
                return Forbid();
            }

            ViewBag.CurrentBranch = currentBranch;

            List<Item> items;

            if (currentBranch != null)
            {
                var branchStockQuery = _context.BranchProductStocks
                    .AsNoTracking()
                    .Where(s => s.BranchId == currentBranch.Id && s.Quantity > 0);

                if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
                {
                    branchStockQuery = branchStockQuery.Where(s => s.TenantId == tenantId || s.TenantId == null);
                }

                var branchStockMap = await branchStockQuery
                    .ToDictionaryAsync(s => s.ProductId, s => s.Quantity);

                var activeIds = branchStockMap.Keys.ToList();

                var itemsQuery = _context.Items
                    .AsNoTracking()
                    .Include(i => i.Category)
                    .Include(i => i.Unit)
                    .Where(i => i.Status == "Active" && activeIds.Contains(i.Id));

                if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
                {
                    itemsQuery = itemsQuery.Where(i => i.TenantId == tenantId || i.TenantId == null);
                }

                items = await itemsQuery
                    .OrderBy(i => i.ItemName)
                    .ToListAsync();

                ViewBag.BranchStockMap = branchStockMap;
            }
            else
            {
                var itemsQuery = _context.Items
                    .AsNoTracking()
                    .Include(i => i.Category)
                    .Include(i => i.Unit)
                    .Where(i => i.Status == "Active" && i.CurrentStock > 0);

                if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
                {
                    itemsQuery = itemsQuery.Where(i => i.TenantId == tenantId || i.TenantId == null);
                }

                items = await itemsQuery
                    .OrderBy(i => i.ItemName)
                    .ToListAsync();
            }

            var settings = await _context.SystemSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.TenantId == tenantId)
                ?? new SystemSetting();

            ViewBag.TaxMode = settings.TaxMode;
            ViewBag.DefaultVatPercent = settings.DefaultVatPercent;

            return View(items);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermissionAuthorize("POS", "Create")]
        public async Task<IActionResult> Checkout(
            int? customerId,
            string paymentMethod,
            string? referenceNumber,
            decimal discountAmount,
            string? discountType,
            decimal discountValue,
            string? discountReason,
            decimal vatAmount,
            decimal subTotal,
            decimal totalAmount,
            decimal amountReceived,
            decimal changeAmount,
            string cartJson,
            string? checkoutToken)
        {
            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            if (string.IsNullOrWhiteSpace(cartJson))
            {
                TempData["ErrorMessage"] = "Cart is empty.";
                return RedirectToAction(nameof(Index));
            }

            List<CartItemDto>? cartItems;

            try
            {
                cartItems = JsonSerializer.Deserialize<List<CartItemDto>>(
                    cartJson,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
            }
            catch
            {
                TempData["ErrorMessage"] = "Invalid cart data.";
                return RedirectToAction(nameof(Index));
            }

            if (cartItems == null || !cartItems.Any())
            {
                TempData["ErrorMessage"] = "Cart is empty.";
                return RedirectToAction(nameof(Index));
            }

            var cashierIdForToken =
                User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anon";

            if (!string.IsNullOrWhiteSpace(checkoutToken))
            {
                var cacheKey = $"checkout:{cashierIdForToken}:{checkoutToken}";

                if (_cache.TryGetValue(cacheKey, out _))
                {
                    TempData["ErrorMessage"] = "This transaction was already submitted. Please start a new sale.";
                    return RedirectToAction(nameof(Index));
                }

                _cache.Set(cacheKey, true, TimeSpan.FromMinutes(5));
            }

            if (string.IsNullOrWhiteSpace(paymentMethod))
            {
                TempData["ErrorMessage"] = "Payment method is required.";
                return RedirectToAction(nameof(Index));
            }

            if (discountAmount < 0 || discountValue < 0)
            {
                TempData["ErrorMessage"] = "Discount cannot be negative.";
                return RedirectToAction(nameof(Index));
            }

            if (paymentMethod == "Credit" && customerId == null)
            {
                TempData["ErrorMessage"] = "Customer is required for credit sales.";
                return RedirectToAction(nameof(Index));
            }

            if (paymentMethod == "Credit" && amountReceived > 0)
            {
                TempData["ErrorMessage"] = "Amount received must be zero for credit sales.";
                return RedirectToAction(nameof(Index));
            }

            if (customerId.HasValue)
            {
                var customerQuery = _context.Customers
                    .Where(c => c.Id == customerId.Value && c.IsActive);

                if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
                {
                    customerQuery = customerQuery.Where(c => c.TenantId == tenantId || c.TenantId == null);
                }

                var customer = await customerQuery.FirstOrDefaultAsync();

                if (customer == null)
                {
                    TempData["ErrorMessage"] = "Customer not found or inactive.";
                    return RedirectToAction(nameof(Index));
                }

                if (!await _tenantGuard.CanAccessTenantAsync(customer.TenantId))
                {
                    return Forbid();
                }

                if (!customer.TenantId.HasValue && tenantId.HasValue)
                {
                    customer.TenantId = tenantId;
                }
            }

            if (paymentMethod == "Credit")
            {
                amountReceived = 0;
                changeAmount = 0;
                referenceNumber = null;
            }

            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var salesNumber = await GenerateSalesNumberAsync(tenantId);

                var cashierId =
                    User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

                var cashierName =
                    User.Identity?.Name ?? "Unknown";

                var currentBranch = await _branchService.GetCurrentBranchAsync(User);

                if (currentBranch != null && !await _tenantGuard.CanAccessTenantAsync(currentBranch.TenantId))
                {
                    await transaction.RollbackAsync();
                    return Forbid();
                }

                var settings = await _context.SystemSettings
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.TenantId == tenantId)
                    ?? new SystemSetting();

                var taxMode = settings.TaxMode;
                var defaultVatPercent = settings.DefaultVatPercent;

                var salesHeader = new SalesHeader
                {
                    TenantId = tenantId,
                    SalesNumber = salesNumber,
                    SalesDate = DateTime.Now,
                    CustomerId = customerId,
                    CashierId = cashierId,
                    CashierName = cashierName,
                    BranchId = currentBranch?.Id,
                    PaymentMethod = paymentMethod,
                    ReferenceNumber = referenceNumber,
                    SubTotal = 0,
                    DiscountAmount = discountAmount,
                    DiscountType = discountType,
                    DiscountValue = discountValue,
                    DiscountReason = discountReason,
                    VatAmount = 0,
                    TotalAmount = 0,
                    AmountReceived = amountReceived,
                    ChangeAmount = changeAmount,
                    Status = "Completed",
                    CreatedAt = DateTime.Now
                };

                if (cartItems.Any(c => c.ItemId <= 0 || c.Quantity <= 0))
                {
                    TempData["ErrorMessage"] = "Invalid cart item.";
                    await transaction.RollbackAsync();
                    return RedirectToAction(nameof(Index));
                }

                var cartItemIds = cartItems.Select(c => c.ItemId).ToList();

                var itemsQuery = _context.Items
                    .Where(i => cartItemIds.Contains(i.Id) && i.Status == "Active");

                if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
                {
                    itemsQuery = itemsQuery.Where(i => i.TenantId == tenantId || i.TenantId == null);
                }

                var itemsLookup = await itemsQuery.ToDictionaryAsync(i => i.Id);

                foreach (var cartItem in cartItems)
                {
                    if (cartItem.Quantity <= 0)
                    {
                        TempData["ErrorMessage"] = "Item quantity must be greater than zero.";
                        await transaction.RollbackAsync();
                        return RedirectToAction(nameof(Index));
                    }

                    if (!itemsLookup.TryGetValue(cartItem.ItemId, out var item))
                    {
                        TempData["ErrorMessage"] =
                            "One or more items are no longer available. Please refresh the cart.";

                        await transaction.RollbackAsync();
                        return RedirectToAction(nameof(Index));
                    }

                    if (!await _tenantGuard.CanAccessTenantAsync(item.TenantId))
                    {
                        await transaction.RollbackAsync();
                        return Forbid();
                    }

                    var freshStockQuery = _context.Items
                        .Where(i => i.Id == item.Id)
                        .Select(i => new { i.CurrentStock, i.Status, i.TenantId });

                    if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
                    {
                        freshStockQuery = freshStockQuery.Where(i => i.TenantId == tenantId || i.TenantId == null);
                    }

                    var freshStock = await freshStockQuery.FirstOrDefaultAsync();

                    if (freshStock == null || freshStock.Status != "Active")
                    {
                        TempData["ErrorMessage"] =
                            $"{item.ItemName} is no longer available. Please refresh the cart.";

                        await transaction.RollbackAsync();
                        return RedirectToAction(nameof(Index));
                    }

                    // In branch mode the authoritative stock is BranchProductStock,
                    // not Item.CurrentStock, so skip the global check (H-5).
                    if (currentBranch == null && freshStock.CurrentStock < cartItem.Quantity)
                    {
                        TempData["ErrorMessage"] =
                            $"Stock for {item.ItemName} changed during checkout. " +
                            $"Available: {freshStock.CurrentStock:N0}. Please refresh cart.";

                        await _auditService.LogAsync(
                            User,
                            "POS",
                            "CHECKOUT_STOCK_CONFLICT",
                            $"Stock conflict on checkout. Item: {item.ItemName}, Requested: {cartItem.Quantity}, Available: {freshStock.CurrentStock}",
                            "Item",
                            item.Id.ToString(),
                            HttpContext.Connection.RemoteIpAddress?.ToString());

                        await transaction.RollbackAsync();
                        return RedirectToAction(nameof(Index));
                    }

                    var actualPrice = item.SellingPrice;
                    var lineTotal = actualPrice * cartItem.Quantity;

                    salesHeader.SalesDetails.Add(new SalesDetail
                    {
                        ItemId = item.Id,
                        Quantity = cartItem.Quantity,
                        UnitPrice = actualPrice,
                        LineTotal = lineTotal
                    });

                    if (currentBranch != null)
                    {
                        var branchQty = await _branchService.GetBranchStockAsync(currentBranch.Id, item.Id);

                        if (branchQty < cartItem.Quantity)
                        {
                            TempData["ErrorMessage"] =
                                $"Insufficient branch stock for {item.ItemName}. " +
                                $"Branch available: {branchQty:N0}. Please refresh cart.";

                            await _auditService.LogAsync(
                                User,
                                "POS",
                                "CHECKOUT_BRANCH_STOCK_CONFLICT",
                                $"Branch stock conflict on checkout. Item: {item.ItemName}, Branch: {currentBranch.Name}, Requested: {cartItem.Quantity}, Branch Available: {branchQty}",
                                "Item",
                                item.Id.ToString(),
                                HttpContext.Connection.RemoteIpAddress?.ToString());

                            await transaction.RollbackAsync();
                            return RedirectToAction(nameof(Index));
                        }

                        await _branchService.DeductStockAsync(currentBranch.Id, item.Id, cartItem.Quantity);
                    }
                    else
                    {
                        item.CurrentStock -= cartItem.Quantity;
                    }

                    if (!item.TenantId.HasValue && tenantId.HasValue)
                    {
                        item.TenantId = tenantId;
                    }
                }

                var serverSubTotal = salesHeader.SalesDetails.Sum(d => d.LineTotal);
                var serverTotalBeforeVat = serverSubTotal - discountAmount;

                if (serverTotalBeforeVat < 0)
                {
                    serverTotalBeforeVat = 0;
                }

                if (taxMode == "VAT")
                {
                    vatAmount = serverTotalBeforeVat * (defaultVatPercent / 100);
                }
                else
                {
                    vatAmount = 0;
                }

                salesHeader.SubTotal = serverSubTotal;
                salesHeader.VatAmount = vatAmount;
                salesHeader.TotalAmount = serverTotalBeforeVat + vatAmount;

                totalAmount = salesHeader.TotalAmount;

                if (discountAmount > serverSubTotal)
                {
                    TempData["ErrorMessage"] = "Discount cannot be greater than subtotal.";
                    await transaction.RollbackAsync();
                    return RedirectToAction(nameof(Index));
                }

                if (paymentMethod == "Cash" && amountReceived < totalAmount)
                {
                    TempData["ErrorMessage"] = "Amount received is less than total amount.";
                    await transaction.RollbackAsync();
                    return RedirectToAction(nameof(Index));
                }

                _context.SalesHeaders.Add(salesHeader);

                if (paymentMethod == "Credit" && customerId.HasValue)
                {
                    var previousBalance = await _context.CustomerLedgers
                        .Where(l => l.CustomerId == customerId.Value)
                        .OrderByDescending(l => l.Id)
                        .Select(l => l.RunningBalance)
                        .FirstOrDefaultAsync();

                    var newBalance = previousBalance + totalAmount;

                    _context.CustomerLedgers.Add(new CustomerLedger
                    {
                        CustomerId = customerId.Value,
                        TransactionType = "CHARGE",
                        ReferenceNumber = salesNumber,
                        DebitAmount = totalAmount,
                        CreditAmount = 0,
                        RunningBalance = newBalance,
                        Remarks = $"Credit sale from receipt {salesNumber}",
                        TransactionDate = DateTime.Now,
                        CreatedBy = cashierName,
                        CreatedAt = DateTime.Now
                    });
                }

                await _context.SaveChangesAsync();

                await transaction.CommitAsync();

                await _auditService.LogAsync(
                    User,
                    "POS",
                    "SALE CREATED",
                    $"Sale completed. Receipt: {salesNumber}, Total: {totalAmount:N2}",
                    "SalesHeader",
                    salesHeader.Id.ToString(),
                    HttpContext.Connection.RemoteIpAddress?.ToString()
                );

                if (discountAmount > 0)
                {
                    await _auditService.LogAsync(
                        User,
                        "POS",
                        "DISCOUNT APPLIED",
                        $"Discount applied on receipt {salesNumber}. Type: {discountType}, Value: {discountValue:N2}, Amount: {discountAmount:N2}, Reason: {discountReason}",
                        "SalesHeader",
                        salesHeader.Id.ToString(),
                        HttpContext.Connection.RemoteIpAddress?.ToString()
                    );

                    await _notificationService.CreateDiscountNotificationAsync(
                        salesNumber,
                        discountAmount);
                }

                TempData["SuccessMessage"] =
                    $"Sale completed successfully. Receipt: {salesNumber}";

                return RedirectToAction(
                    "Receipt",
                    "Sales",
                    new { id = salesHeader.Id });
            }
            catch
            {
                await transaction.RollbackAsync();

                TempData["ErrorMessage"] =
                    "Unable to complete transaction.";

                return RedirectToAction(nameof(Index));
            }
        }

        private async Task<string> GenerateSalesNumberAsync(int? tenantId)
        {
            var prefix = $"POS-{DateTime.Now:yyyyMMdd}-";

            var query = _context.SalesHeaders.Where(s => s.SalesNumber.StartsWith(prefix));
            if (tenantId.HasValue)
                query = query.Where(s => s.TenantId == tenantId);

            var countToday = await query.CountAsync();
            return $"{prefix}{(countToday + 1):0000}";
        }

        private class CartItemDto
        {
            public int ItemId { get; set; }

            public string ItemName { get; set; } = string.Empty;

            public string UnitName { get; set; } = string.Empty;

            public decimal Quantity { get; set; }

            public decimal UnitPrice { get; set; }
        }
    }
}
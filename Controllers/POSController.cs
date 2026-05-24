using HardwareManagementSystem.Data;
using HardwareManagementSystem.Models;
using HardwareManagementSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Text.Json;

namespace HardwareManagementSystem.Controllers
{
    [Authorize]
    [PermissionAuthorize("POS", "View")]
    public class POSController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly AuditService _auditService;
        private readonly NotificationService _notificationService;
        private readonly IMemoryCache _cache;

        public POSController(
            ApplicationDbContext context,
            AuditService auditService,
            NotificationService notificationService,
            IMemoryCache cache)
        {
            _context = context;
            _auditService = auditService;
            _notificationService = notificationService;
            _cache = cache;
        }

        public async Task<IActionResult> Index()
        {
            ViewBag.Customers = await _context.Customers
                .AsNoTracking()
                .Where(c => c.IsActive)
                .OrderBy(c => c.CustomerName)
                .ToListAsync();

            var items = await _context.Items
                .AsNoTracking()
                .Include(i => i.Category)
                .Include(i => i.Unit)
                .Where(i => i.Status == "Active" && i.CurrentStock > 0)
                .OrderBy(i => i.ItemName)
                .ToListAsync();

            var settings = await _context.SystemSettings
            .AsNoTracking()
            .FirstOrDefaultAsync();

                    ViewBag.TaxMode = settings?.TaxMode ?? "VAT";

                    ViewBag.DefaultVatPercent =
                        settings?.DefaultVatPercent ?? 0;

            return View(items);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
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

            // ============================================
            // DUPLICATE CHECKOUT PROTECTION
            // ============================================

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

            if (paymentMethod == "Credit")
            {
                amountReceived = 0;
                changeAmount = 0;
                referenceNumber = null;
            }

            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var salesNumber = await GenerateSalesNumberAsync();

                var cashierId =
                    User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

                var cashierName =
                    User.Identity?.Name ?? "Unknown";

                // ============================================
                // SERVER-SIDE TAXMODE RECALCULATION
                // ============================================

                var settings = await _context.SystemSettings
                    .AsNoTracking()
                    .FirstOrDefaultAsync();

                var taxMode = settings?.TaxMode ?? "VAT";
                var defaultVatPercent = settings?.DefaultVatPercent ?? 0;

                // serverSubTotal, vatAmount, and totalAmount are recalculated server-side
                // after SalesDetails are built from DB-confirmed prices (see below).
                decimal serverTotalBeforeVat = 0;

                var salesHeader = new SalesHeader
                {
                    SalesNumber = salesNumber,
                    SalesDate = DateTime.Now,
                    CustomerId = customerId,
                    CashierId = cashierId,
                    CashierName = cashierName,
                    PaymentMethod = paymentMethod,
                    ReferenceNumber = referenceNumber,
                    SubTotal = 0,          // set after foreach
                    DiscountAmount = discountAmount,
                    DiscountType = discountType,
                    DiscountValue = discountValue,
                    DiscountReason = discountReason,
                    VatAmount = 0,         // set after foreach
                    TotalAmount = 0,       // set after foreach
                    AmountReceived = amountReceived,
                    ChangeAmount = changeAmount,
                    Status = "Completed",
                    CreatedAt = DateTime.Now
                };

                if (cartItems.Any(c => c.ItemId <= 0 || c.Quantity <= 0))
                {
                    TempData["ErrorMessage"] = "Invalid cart item.";
                    return RedirectToAction(nameof(Index));
                }

                var cartItemIds =
                    cartItems.Select(c => c.ItemId).ToList();

                // FIX 2: Re-fetch items inside the transaction with tracking
                // to get fresh stock values for concurrency protection.
                var itemsLookup = await _context.Items
                    .Where(i =>
                        cartItemIds.Contains(i.Id) &&
                        i.Status == "Active")
                    .ToDictionaryAsync(i => i.Id);

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

                    // FIX 2: Re-read current stock directly from DB inside transaction
                    var freshStock = await _context.Items
                        .Where(i => i.Id == item.Id)
                        .Select(i => new { i.CurrentStock, i.Status })
                        .FirstOrDefaultAsync();

                    if (freshStock == null || freshStock.Status != "Active")
                    {
                        TempData["ErrorMessage"] =
                            $"{item.ItemName} is no longer available. Please refresh the cart.";

                        await transaction.RollbackAsync();
                        return RedirectToAction(nameof(Index));
                    }

                    if (freshStock.CurrentStock < cartItem.Quantity)
                    {
                        TempData["ErrorMessage"] =
                            $"Stock for {item.ItemName} changed during checkout. " +
                            $"Available: {freshStock.CurrentStock:N0}. Please refresh cart.";

                        await _auditService.LogAsync(
                            User,
                            "POS",
                            "CHECKOUT_STOCK_CONFLICT",
                            $"Stock conflict on checkout. Item: {item.ItemName}, " +
                            $"Requested: {cartItem.Quantity}, Available: {freshStock.CurrentStock}",
                            "Item",
                            item.Id.ToString(),
                            HttpContext.Connection.RemoteIpAddress?.ToString());

                        await transaction.RollbackAsync();
                        return RedirectToAction(nameof(Index));
                    }

                    // FIX 1: Use DB SellingPrice — ignore browser-submitted UnitPrice
                    var actualPrice = item.SellingPrice;
                    var lineTotal = actualPrice * cartItem.Quantity;

                    salesHeader.SalesDetails.Add(new SalesDetail
                    {
                        ItemId = item.Id,
                        Quantity = cartItem.Quantity,
                        UnitPrice = actualPrice,
                        LineTotal = lineTotal
                    });

                    item.CurrentStock -= cartItem.Quantity;
                }

                // Recalculate subtotal from DB-confirmed line totals (override browser value)
                var serverSubTotal = salesHeader.SalesDetails.Sum(d => d.LineTotal);

                serverTotalBeforeVat = serverSubTotal - discountAmount;

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

                // Server-side guards using recalculated values
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

                    var newBalance =
                        previousBalance + totalAmount;

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

                    await _notificationService
                        .CreateDiscountNotificationAsync(
                            salesNumber,
                            discountAmount
                        );
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

        private async Task<string> GenerateSalesNumberAsync()
        {
            var today = DateTime.Now;
            var prefix = $"POS-{today:yyyyMMdd}-";

            var countToday = await _context.SalesHeaders
                .CountAsync(s => s.SalesNumber.StartsWith(prefix));

            return $"{prefix}{(countToday + 1).ToString("0000")}";
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
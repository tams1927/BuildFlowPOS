using HardwareManagementSystem.Data;
using HardwareManagementSystem.Models;
using HardwareManagementSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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

        public POSController(ApplicationDbContext context, AuditService auditService, NotificationService notificationService)
        {
            _context = context;
            _auditService = auditService;
            _notificationService = notificationService;
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
    string cartJson)
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

            if (string.IsNullOrWhiteSpace(paymentMethod))
            {
                TempData["ErrorMessage"] = "Payment method is required.";
                return RedirectToAction(nameof(Index));
            }

            if (totalAmount <= 0)
            {
                TempData["ErrorMessage"] = "Invalid transaction total.";
                return RedirectToAction(nameof(Index));
            }

            if (discountAmount < 0 || discountValue < 0)
            {
                TempData["ErrorMessage"] = "Discount cannot be negative.";
                return RedirectToAction(nameof(Index));
            }

            if (discountAmount > subTotal)
            {
                TempData["ErrorMessage"] = "Discount cannot be greater than subtotal.";
                return RedirectToAction(nameof(Index));
            }

            if (paymentMethod == "Cash" && amountReceived < totalAmount)
            {
                TempData["ErrorMessage"] = "Amount received is less than total amount.";
                return RedirectToAction(nameof(Index));
            }

            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var salesNumber = await GenerateSalesNumberAsync();

                var cashierId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                var cashierName = User.Identity?.Name ?? "Unknown";

                var salesHeader = new SalesHeader
                {
                    SalesNumber = salesNumber,
                    SalesDate = DateTime.Now,
                    CustomerId = customerId,
                    CashierId = cashierId,
                    CashierName = cashierName,
                    PaymentMethod = paymentMethod,
                    ReferenceNumber = referenceNumber,
                    SubTotal = subTotal,
                    DiscountAmount = discountAmount,
                    DiscountType = discountType,
                    DiscountValue = discountValue,
                    DiscountReason = discountReason,
                    VatAmount = vatAmount,
                    TotalAmount = totalAmount,
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

                var cartItemIds = cartItems.Select(c => c.ItemId).ToList();
                var itemsLookup = await _context.Items
                    .Where(i => cartItemIds.Contains(i.Id) && i.Status == "Active")
                    .ToDictionaryAsync(i => i.Id);

                foreach (var cartItem in cartItems)
                {
                    if (!itemsLookup.TryGetValue(cartItem.ItemId, out var item))
                    {
                        TempData["ErrorMessage"] = "One or more items no longer exist.";
                        return RedirectToAction(nameof(Index));
                    }

                    if (item.CurrentStock < cartItem.Quantity)
                    {
                        TempData["ErrorMessage"] = $"Insufficient stock for {item.ItemName}.";
                        return RedirectToAction(nameof(Index));
                    }

                    var lineTotal = cartItem.Quantity * cartItem.UnitPrice;

                    salesHeader.SalesDetails.Add(new SalesDetail
                    {
                        ItemId = item.Id,
                        Quantity = cartItem.Quantity,
                        UnitPrice = cartItem.UnitPrice,
                        LineTotal = lineTotal
                    });

                    item.CurrentStock -= cartItem.Quantity;
                }

                _context.SalesHeaders.Add(salesHeader);

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

                    await _notificationService.CreateDiscountNotificationAsync(salesNumber, discountAmount);
                }

                TempData["SuccessMessage"] = $"Sale completed successfully. Receipt: {salesNumber}";

                return RedirectToAction(nameof(Index));
            }
            catch
            {
                await transaction.RollbackAsync();

                TempData["ErrorMessage"] = "Unable to complete transaction.";
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
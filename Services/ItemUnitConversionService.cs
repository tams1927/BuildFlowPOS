using HardwareManagementSystem.Data;
using HardwareManagementSystem.Models;
using HardwareManagementSystem.Services.TenantDatabases;
using Microsoft.EntityFrameworkCore;

namespace HardwareManagementSystem.Services
{
    /// <summary>Result of purchase-cost calculation (user enters cost per received/ordered unit).</summary>
    public readonly record struct PurchaseCostResult(
        decimal CostPerReceivedUnit,
        decimal CostPerBaseUnit,
        decimal TotalCost);

    public sealed class ItemUnitConversionService
    {
        /// <summary>
        /// User enters cost per received/ordered unit. Total = receivedQty × costPerReceivedUnit.
        /// Base unit cost = costPerReceivedUnit ÷ conversionQuantity.
        /// </summary>
        public static PurchaseCostResult ComputePurchaseCost(
            decimal receivedOrOrderedQty,
            decimal costPerReceivedUnit,
            decimal conversionQuantity)
        {
            var factor = conversionQuantity > 0 ? conversionQuantity : 1m;
            var costPerBase = factor > 0 ? costPerReceivedUnit / factor : costPerReceivedUnit;
            var total = receivedOrOrderedQty * costPerReceivedUnit;
            return new PurchaseCostResult(costPerReceivedUnit, costPerBase, total);
        }

        private readonly ITenantOperationalContextProvider _ctxProvider;

        public ItemUnitConversionService(ITenantOperationalContextProvider ctxProvider)
        {
            _ctxProvider = ctxProvider;
        }

        public int GetBaseUnitId(Item item) =>
            item.BaseUnitId > 0 ? item.BaseUnitId : item.UnitId;

        public async Task<ItemUnitConversion?> GetConversionAsync(int itemId, int unitId)
        {
            var db = await _ctxProvider.GetContextAsync();
            var conv = await db.ItemUnitConversions
                .AsNoTracking()
                .Include(c => c.Unit)
                .FirstOrDefaultAsync(c =>
                    c.ItemId == itemId &&
                    c.UnitId == unitId &&
                    c.IsActive);

            if (conv != null)
                return conv;

            var item = await db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId);
            if (item == null)
                return null;

            var baseId = GetBaseUnitId(item);
            if (unitId == baseId)
            {
                return new ItemUnitConversion
                {
                    ItemId = itemId,
                    UnitId = unitId,
                    ConversionQuantity = 1,
                    IsActive = true,
                    Unit = await db.Units.AsNoTracking().FirstOrDefaultAsync(u => u.Id == unitId)
                };
            }

            return null;
        }

        public async Task<decimal> GetFactorAsync(int itemId, int unitId)
        {
            var conv = await GetConversionAsync(itemId, unitId);
            if (conv == null || conv.ConversionQuantity <= 0)
                throw new InvalidOperationException("No unit conversion defined for this item and unit.");
            return conv.ConversionQuantity;
        }

        public async Task<decimal> ToBaseQuantityAsync(int itemId, int unitId, decimal receivedQuantity)
        {
            var factor = await GetFactorAsync(itemId, unitId);
            return receivedQuantity * factor;
        }

        public async Task<List<ItemUnitConversion>> GetActiveConversionsAsync(int itemId)
        {
            var db = await _ctxProvider.GetContextAsync();
            return await db.ItemUnitConversions
                .AsNoTracking()
                .Include(c => c.Unit)
                .Where(c => c.ItemId == itemId && c.IsActive)
                .OrderByDescending(c => c.IsDefaultPurchaseUnit)
                .ThenBy(c => c.Unit!.UnitName)
                .ToListAsync();
        }

        public async Task EnsureBaseConversionAsync(Item item, int? tenantId)
        {
            var db = await _ctxProvider.GetContextAsync();
            var baseId = GetBaseUnitId(item);
            if (baseId <= 0)
                baseId = item.UnitId;

            var exists = await db.ItemUnitConversions
                .AnyAsync(c => c.ItemId == item.Id && c.UnitId == baseId);

            if (!exists)
            {
                db.ItemUnitConversions.Add(new ItemUnitConversion
                {
                    TenantId = tenantId ?? item.TenantId,
                    ItemId = item.Id,
                    UnitId = baseId,
                    ConversionQuantity = 1,
                    IsDefaultPurchaseUnit = true,
                    IsActive = true,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                });
                await db.SaveChangesAsync();
            }
        }

        public async Task<ItemUnitConversion?> GetDefaultPurchaseUnitAsync(int itemId)
        {
            var db = await _ctxProvider.GetContextAsync();
            return await db.ItemUnitConversions
                .AsNoTracking()
                .Include(c => c.Unit)
                .Where(c => c.ItemId == itemId && c.IsActive && c.IsDefaultPurchaseUnit)
                .FirstOrDefaultAsync();
        }

        /// <summary>Optional equivalent in default purchase unit (e.g. 250 kg → 10 sacks).</summary>
        public async Task<string?> FormatPurchaseEquivalentAsync(Item item, decimal baseQty)
        {
            var def = await GetDefaultPurchaseUnitAsync(item.Id);
            if (def == null || def.ConversionQuantity <= 0)
                return null;

            var baseId = GetBaseUnitId(item);
            if (def.UnitId == baseId)
                return null;

            var equiv = baseQty / def.ConversionQuantity;
            var unitName = def.Unit?.ShortName ?? def.Unit?.UnitName ?? "unit";
            return $"{equiv:0.###} {unitName}";
        }
    }
}

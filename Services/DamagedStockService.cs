using HardwareManagementSystem.Data;
using HardwareManagementSystem.Services.TenantDatabases;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace HardwareManagementSystem.Services
{
    /// <summary>Manages sellable vs damaged stock buckets (branch-aware).</summary>
    public class DamagedStockService
    {
        private readonly ITenantOperationalContextProvider _operationalContextProvider;
        private readonly TenantGuard _tenantGuard;
        private readonly ITenantContext _tenantContext;

        public DamagedStockService(
            ITenantOperationalContextProvider operationalContextProvider,
            TenantGuard tenantGuard,
            ITenantContext tenantContext)
        {
            _operationalContextProvider = operationalContextProvider;
            _tenantGuard = tenantGuard;
            _tenantContext = tenantContext;
        }

        public async Task<decimal> GetSellableStockAsync(int? branchId, int productId)
        {
            var db = await _operationalContextProvider.GetContextAsync();

            if (branchId.HasValue)
            {
                var bps = await db.BranchProductStocks.AsNoTracking()
                    .FirstOrDefaultAsync(s => s.BranchId == branchId.Value && s.ProductId == productId);
                return bps?.Quantity ?? 0;
            }

            var item = await db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == productId);
            return item?.CurrentStock ?? 0;
        }

        public async Task<decimal> GetDamagedStockAsync(int? branchId, int productId)
        {
            var db = await _operationalContextProvider.GetContextAsync();

            if (branchId.HasValue)
            {
                var bps = await db.BranchProductStocks.AsNoTracking()
                    .FirstOrDefaultAsync(s => s.BranchId == branchId.Value && s.ProductId == productId);
                return bps?.DamagedStock ?? 0;
            }

            var item = await db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == productId);
            return item?.DamagedStock ?? 0;
        }

        public Task MoveSellableToDamagedAsync(int? branchId, int productId, decimal baseQuantity)
            => MutateAsync(branchId, productId, baseQuantity, MoveSellableToDamagedMutation);

        public Task RecoverDamagedAsync(int? branchId, int productId, decimal baseQuantity)
            => MutateAsync(branchId, productId, baseQuantity, RecoverMutation);

        public Task DisposeDamagedAsync(int? branchId, int productId, decimal baseQuantity)
            => MutateAsync(branchId, productId, baseQuantity, DisposeMutation);

        public Task DeductDamagedAsync(int? branchId, int productId, decimal baseQuantity)
            => MutateAsync(branchId, productId, baseQuantity, DeductDamagedMutation);

        public Task AddSellableAsync(int? branchId, int productId, decimal baseQuantity)
            => MutateAsync(branchId, productId, baseQuantity, AddSellableMutation);

        public Task DeductSellableAsync(int? branchId, int productId, decimal baseQuantity)
            => MutateAsync(branchId, productId, baseQuantity, DeductSellableMutation);

        private static void MoveSellableToDamagedMutation(
            Models.Item item, Models.BranchProductStock? bps, decimal qty)
        {
            if (bps != null)
            {
                if (bps.Quantity < qty)
                    throw new InvalidOperationException(
                        $"Insufficient sellable stock. Available: {bps.Quantity:0.###}, Requested: {qty:0.###}.");
                bps.Quantity -= qty;
                bps.DamagedStock += qty;
                bps.UpdatedAtUtc = DateTime.UtcNow;
            }
            else
            {
                if (item.CurrentStock < qty)
                    throw new InvalidOperationException(
                        $"Insufficient sellable stock. Available: {item.CurrentStock:0.###}, Requested: {qty:0.###}.");
                item.CurrentStock -= qty;
            }

            item.DamagedStock += qty;
        }

        private static void RecoverMutation(
            Models.Item item, Models.BranchProductStock? bps, decimal qty)
        {
            if (item.DamagedStock < qty)
                throw new InvalidOperationException(
                    $"Insufficient damaged stock. Available: {item.DamagedStock:0.###}, Requested: {qty:0.###}.");

            item.DamagedStock -= qty;

            if (bps != null)
            {
                if (bps.DamagedStock < qty)
                    throw new InvalidOperationException(
                        $"Insufficient branch damaged stock. Available: {bps.DamagedStock:0.###}.");
                bps.DamagedStock -= qty;
                bps.Quantity += qty;
                bps.UpdatedAtUtc = DateTime.UtcNow;
            }
            else
            {
                item.CurrentStock += qty;
            }
        }

        private static void DisposeMutation(
            Models.Item item, Models.BranchProductStock? bps, decimal qty)
        {
            if (item.DamagedStock < qty)
                throw new InvalidOperationException(
                    $"Insufficient damaged stock. Available: {item.DamagedStock:0.###}.");

            item.DamagedStock -= qty;

            if (bps != null)
            {
                if (bps.DamagedStock < qty)
                    throw new InvalidOperationException(
                        $"Insufficient branch damaged stock. Available: {bps.DamagedStock:0.###}.");
                bps.DamagedStock -= qty;
                bps.UpdatedAtUtc = DateTime.UtcNow;
            }
        }

        private static void DeductDamagedMutation(
            Models.Item item, Models.BranchProductStock? bps, decimal qty)
            => DisposeMutation(item, bps, qty);

        private static void AddSellableMutation(
            Models.Item item, Models.BranchProductStock? bps, decimal qty)
        {
            item.CurrentStock += qty;
            if (bps != null)
            {
                bps.Quantity += qty;
                bps.UpdatedAtUtc = DateTime.UtcNow;
            }
        }

        private static void DeductSellableMutation(
            Models.Item item, Models.BranchProductStock? bps, decimal qty)
        {
            if (bps != null)
            {
                if (bps.Quantity < qty)
                    throw new InvalidOperationException(
                        $"Insufficient sellable stock. Available: {bps.Quantity:0.###}.");
                bps.Quantity -= qty;
                bps.UpdatedAtUtc = DateTime.UtcNow;
            }
            else if (item.CurrentStock < qty)
            {
                throw new InvalidOperationException(
                    $"Insufficient sellable stock. Available: {item.CurrentStock:0.###}.");
            }

            item.CurrentStock -= qty;
            if (item.CurrentStock < 0)
                item.CurrentStock = 0;
        }

        private async Task MutateAsync(
            int? branchId,
            int productId,
            decimal quantity,
            Action<Models.Item, Models.BranchProductStock?, decimal> mutation)
        {
            if (quantity <= 0)
                throw new InvalidOperationException("Quantity must be greater than zero.");

            await ExecuteMutationAsync(async db =>
            {
                var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

                var item = await db.Items.FirstOrDefaultAsync(i => i.Id == productId);
                if (item == null)
                    throw new InvalidOperationException("Product not found.");

                if (!await _tenantGuard.CanAccessTenantAsync(item.TenantId))
                    throw new UnauthorizedAccessException("Cross-tenant product access denied.");

                Models.BranchProductStock? bps = null;

                if (branchId.HasValue)
                {
                    var branch = await db.Branches.FirstOrDefaultAsync(b => b.Id == branchId.Value);
                    if (branch == null)
                        throw new InvalidOperationException("Branch not found.");

                    if (!await _tenantGuard.CanAccessTenantAsync(branch.TenantId))
                        throw new UnauthorizedAccessException("Cross-tenant branch access denied.");

                    bps = await db.BranchProductStocks.FirstOrDefaultAsync(s =>
                        s.BranchId == branchId.Value && s.ProductId == productId);

                    if (bps == null)
                    {
                        bps = new Models.BranchProductStock
                        {
                            TenantId = tenantId,
                            BranchId = branchId.Value,
                            ProductId = productId,
                            Quantity = 0,
                            DamagedStock = 0,
                            UpdatedAtUtc = DateTime.UtcNow
                        };
                        db.BranchProductStocks.Add(bps);
                    }
                }

                if (!item.TenantId.HasValue && tenantId.HasValue)
                    item.TenantId = tenantId;

                mutation(item, bps, quantity);

                if (item.CurrentStock < 0)
                    item.CurrentStock = 0;
                if (item.DamagedStock < 0)
                    item.DamagedStock = 0;

                if (bps != null)
                {
                    if (bps.Quantity < 0)
                        bps.Quantity = 0;
                    if (bps.DamagedStock < 0)
                        bps.DamagedStock = 0;
                }
            });
        }

        private async Task ExecuteMutationAsync(Func<ITenantOperationalDbContext, Task> mutation)
        {
            var db = await _operationalContextProvider.GetContextAsync();
            var ownsTransaction = db.Database.CurrentTransaction == null;
            IDbContextTransaction? transaction = null;

            if (ownsTransaction)
                transaction = await db.Database.BeginTransactionAsync();

            try
            {
                await mutation(db);
                await db.SaveChangesAsync();

                if (ownsTransaction && transaction != null)
                    await transaction.CommitAsync();
            }
            catch
            {
                if (ownsTransaction && transaction != null)
                    await transaction.RollbackAsync();
                throw;
            }
            finally
            {
                if (ownsTransaction && transaction != null)
                    await transaction.DisposeAsync();
            }
        }
    }
}

using HardwareManagementSystem.Data;
using HardwareManagementSystem.Models;
using HardwareManagementSystem.Services.TenantDatabases;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Security.Claims;

namespace HardwareManagementSystem.Services
{
    /// <summary>
    /// Provides branch resolution for the current user session.
    /// Admin / TenantAdmin roles can view all branches and switch freely.
    /// BranchManager / Cashier / InventoryStaff are locked to their assigned branch.
    /// Tenant guardrails are applied during Phase 3.1.
    /// </summary>
    public class BranchService
    {
        // Phase 5.0D.2 — branches, user-branch assignments and per-branch stock are all
        // tenant-owned operational data. They route to the tenant's dedicated database when
        // routing is active (shared otherwise). The provider is scoped and caches a single
        // context per request, so BranchService and the calling controller share the same
        // context instance — keeping transactions and change-tracking consistent.
        private readonly ITenantOperationalContextProvider _operationalContextProvider;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly TenantGuard _tenantGuard;
        private readonly ITenantContext _tenantContext;

        private const string SessionKey = "SelectedBranchId";

        // TenantAdmin can access all branches within their tenant without explicit assignments.
        // SuperAdmin is handled separately (bypasses all tenant filters).
        private static readonly string[] GlobalRoles = ["TenantAdmin"];

        public BranchService(
            ITenantOperationalContextProvider operationalContextProvider,
            IHttpContextAccessor httpContextAccessor,
            TenantGuard tenantGuard,
            ITenantContext tenantContext)
        {
            _operationalContextProvider = operationalContextProvider;
            _httpContextAccessor = httpContextAccessor;
            _tenantGuard = tenantGuard;
            _tenantContext = tenantContext;
        }

        public bool IsGlobalUser(ClaimsPrincipal user)
            => GlobalRoles.Any(r => user.IsInRole(r));

        public async Task<List<Branch>> GetAllActiveBranchesAsync()
        {
            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            var db = await _operationalContextProvider.GetContextAsync();
            var query = db.Branches
                .AsNoTracking()
                .Where(b => b.IsActive);

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
            {
                query = query.Where(b => b.TenantId == tenantId);
            }

            return await query
                .OrderBy(b => b.IsMainBranch ? 0 : 1)
                .ThenBy(b => b.Name)
                .ToListAsync();
        }

        public async Task<List<Branch>> GetAssignedBranchesAsync(string userId)
        {
            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            var db = await _operationalContextProvider.GetContextAsync();
            var query = db.UserBranches
                .AsNoTracking()
                .Where(ub => ub.UserId == userId)
                .Include(ub => ub.Branch)
                .Where(ub => ub.Branch != null && ub.Branch.IsActive)
                .Select(ub => ub.Branch!);

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
            {
                query = query.Where(b => b.TenantId == tenantId);
            }

            return await query
                .OrderBy(b => b.Name)
                .ToListAsync();
        }

        public async Task<Branch?> GetCurrentBranchAsync(ClaimsPrincipal user)
        {
            var session = _httpContextAccessor.HttpContext?.Session;

            if (session == null)
            {
                return await GetMainBranchAsync();
            }

            var db = await _operationalContextProvider.GetContextAsync();
            var storedId = session.GetInt32(SessionKey);

            if (storedId.HasValue)
            {
                var sessionBranch = await db.Branches
                    .AsNoTracking()
                    .FirstOrDefaultAsync(b =>
                        b.Id == storedId.Value &&
                        b.IsActive);

                if (sessionBranch != null &&
                    await _tenantGuard.CanAccessTenantAsync(sessionBranch.TenantId))
                {
                    if (IsGlobalUser(user))
                    {
                        return sessionBranch;
                    }

                    var userId =
                        user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";

                    var isAssigned = await db.UserBranches
                        .AnyAsync(ub =>
                            ub.UserId == userId &&
                            ub.BranchId == storedId.Value);

                    if (isAssigned)
                    {
                        return sessionBranch;
                    }
                }
            }

            if (!IsGlobalUser(user))
            {
                var userId =
                    user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";

                var assigned = await GetAssignedBranchesAsync(userId);
                var first = assigned.FirstOrDefault();

                if (first != null)
                {
                    session.SetInt32(SessionKey, first.Id);
                    return first;
                }
            }

            return await GetMainBranchAsync();
        }

        public async Task<int?> GetCurrentBranchIdAsync(ClaimsPrincipal user)
            => (await GetCurrentBranchAsync(user))?.Id;

        public async Task<bool> SetCurrentBranchAsync(ClaimsPrincipal user, int branchId)
        {
            var db = await _operationalContextProvider.GetContextAsync();
            var branch = await db.Branches
                .AsNoTracking()
                .FirstOrDefaultAsync(b =>
                    b.Id == branchId &&
                    b.IsActive);

            if (branch == null)
                return false;

            if (!await _tenantGuard.CanAccessTenantAsync(branch.TenantId))
                return false;

            if (!IsGlobalUser(user))
            {
                var userId =
                    user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";

                var isAssigned = await db.UserBranches
                    .AnyAsync(ub =>
                        ub.UserId == userId &&
                        ub.BranchId == branchId);

                if (!isAssigned)
                    return false;
            }

            _httpContextAccessor.HttpContext?.Session.SetInt32(SessionKey, branchId);

            return true;
        }

        public async Task<Branch?> GetMainBranchAsync()
        {
            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            var db = await _operationalContextProvider.GetContextAsync();
            var query = db.Branches
                .AsNoTracking()
                .Where(b => b.IsActive);

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
            {
                query = query.Where(b => b.TenantId == tenantId);
            }

            return await query
                .OrderBy(b => b.IsMainBranch ? 0 : 1)
                .ThenBy(b => b.Id)
                .FirstOrDefaultAsync();
        }

        public async Task<decimal> GetBranchStockAsync(int branchId, int productId)
        {
            var db = await _operationalContextProvider.GetContextAsync();
            var branch = await db.Branches
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == branchId);

            if (branch == null)
                return 0;

            if (!await _tenantGuard.CanAccessTenantAsync(branch.TenantId))
                return 0;

            var query = db.BranchProductStocks
                .AsNoTracking()
                .Where(s =>
                    s.BranchId == branchId &&
                    s.ProductId == productId);

            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
            {
                query = query.Where(s => s.TenantId == tenantId);
            }

            var bps = await query.FirstOrDefaultAsync();

            return bps?.Quantity ?? 0;
        }

        public async Task AddStockAsync(int branchId, int productId, decimal quantity)
        {
            if (quantity <= 0)
                throw new InvalidOperationException(
                    "Stock addition quantity must be greater than zero.");

            await ExecuteStockMutationAsync(async db =>
            {
                var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

                var branch = await db.Branches
                    .FirstOrDefaultAsync(b => b.Id == branchId);

                if (branch == null)
                    throw new InvalidOperationException("Branch not found.");

                if (!await _tenantGuard.CanAccessTenantAsync(branch.TenantId))
                    throw new UnauthorizedAccessException(
                        "Cross-tenant branch access denied.");

                var item = await db.Items
                    .FirstOrDefaultAsync(i => i.Id == productId);

                if (item == null)
                    throw new InvalidOperationException("Product not found.");

                if (!await _tenantGuard.CanAccessTenantAsync(item.TenantId))
                    throw new UnauthorizedAccessException(
                        "Cross-tenant product access denied.");

                var bps = await db.BranchProductStocks
                    .FirstOrDefaultAsync(s =>
                        s.BranchId == branchId &&
                        s.ProductId == productId);

                if (bps == null)
                {
                    bps = new BranchProductStock
                    {
                        TenantId = tenantId,
                        BranchId = branchId,
                        ProductId = productId,
                        Quantity = quantity,
                        UpdatedAtUtc = DateTime.UtcNow
                    };

                    db.BranchProductStocks.Add(bps);
                }
                else
                {
                    if (!await _tenantGuard.CanAccessTenantAsync(bps.TenantId))
                    {
                        throw new UnauthorizedAccessException(
                            "Cross-tenant branch stock access denied.");
                    }

                    if (!bps.TenantId.HasValue && tenantId.HasValue)
                        bps.TenantId = tenantId;

                    bps.Quantity += quantity;
                    bps.UpdatedAtUtc = DateTime.UtcNow;
                }

                if (!branch.TenantId.HasValue && tenantId.HasValue)
                    branch.TenantId = tenantId;

                if (!item.TenantId.HasValue && tenantId.HasValue)
                    item.TenantId = tenantId;

                item.CurrentStock += quantity;

                if (item.CurrentStock < 0)
                    item.CurrentStock = 0;
            });
        }

        public async Task DeductStockAsync(int branchId, int productId, decimal quantity)
        {
            if (quantity <= 0)
                throw new InvalidOperationException(
                    "Stock deduction quantity must be greater than zero.");

            await ExecuteStockMutationAsync(async db =>
            {
                var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

                var branch = await db.Branches
                    .FirstOrDefaultAsync(b => b.Id == branchId);

                if (branch == null)
                    throw new InvalidOperationException("Branch not found.");

                if (!await _tenantGuard.CanAccessTenantAsync(branch.TenantId))
                    throw new UnauthorizedAccessException(
                        "Cross-tenant branch access denied.");

                var item = await db.Items
                    .FirstOrDefaultAsync(i => i.Id == productId);

                if (item == null)
                    throw new InvalidOperationException("Product not found.");

                if (!await _tenantGuard.CanAccessTenantAsync(item.TenantId))
                    throw new UnauthorizedAccessException(
                        "Cross-tenant product access denied.");

                var bps = await db.BranchProductStocks
                    .FirstOrDefaultAsync(s =>
                        s.BranchId == branchId &&
                        s.ProductId == productId);

                if (bps == null)
                {
                    throw new InvalidOperationException(
                        $"No branch stock record exists for product #{productId}.");
                }

                if (!await _tenantGuard.CanAccessTenantAsync(bps.TenantId))
                {
                    throw new UnauthorizedAccessException(
                        "Cross-tenant branch stock access denied.");
                }

                if (bps.Quantity < quantity)
                {
                    throw new InvalidOperationException(
                        $"Insufficient branch stock for product #{productId}. " +
                        $"Available: {bps.Quantity:0.###}, Requested: {quantity:0.###}.");
                }

                if (!bps.TenantId.HasValue && tenantId.HasValue)
                    bps.TenantId = tenantId;

                bps.Quantity -= quantity;
                bps.UpdatedAtUtc = DateTime.UtcNow;

                if (bps.Quantity < 0)
                    bps.Quantity = 0;

                if (!branch.TenantId.HasValue && tenantId.HasValue)
                    branch.TenantId = tenantId;

                if (!item.TenantId.HasValue && tenantId.HasValue)
                    item.TenantId = tenantId;

                item.CurrentStock -= quantity;

                if (item.CurrentStock < 0)
                    item.CurrentStock = 0;
            });
        }

        private async Task ExecuteStockMutationAsync(Func<ITenantOperationalDbContext, Task> mutation)
        {
            // The provider is scoped and caches one context per request, so this is the same
            // instance the controller (and its own ambient transaction) is using.
            var db = await _operationalContextProvider.GetContextAsync();

            var ownsTransaction = db.Database.CurrentTransaction == null;
            IDbContextTransaction? transaction = null;

            if (ownsTransaction)
            {
                transaction = await db.Database.BeginTransactionAsync();
            }

            try
            {
                await mutation(db);
                await db.SaveChangesAsync();

                if (ownsTransaction && transaction != null)
                {
                    await transaction.CommitAsync();
                }
            }
            catch
            {
                if (ownsTransaction && transaction != null)
                {
                    await transaction.RollbackAsync();
                }

                throw;
            }
            finally
            {
                if (ownsTransaction && transaction != null)
                {
                    await transaction.DisposeAsync();
                }
            }
        }
    }
}

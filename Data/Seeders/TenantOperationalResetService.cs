using HardwareManagementSystem.Data;
using HardwareManagementSystem.Models;
using HardwareManagementSystem.Services.TenantDatabases;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using System.Text.Json;

namespace HardwareManagementSystem.Data.Seeders
{
    /// <summary>
    /// Development/QA-only: clears operational data for ONE tenant while preserving
    /// the tenant record, subscription, routing, migrations, SuperAdmin, and a named
    /// tenant administrator. Operates against the tenant's routed operational database.
    /// </summary>
    public sealed class TenantOperationalResetService
    {
        private readonly ApplicationDbContext _platform;
        private readonly ITenantOperationalContextProvider _ctxProvider;
        private readonly ITenantDatabaseResolver _resolver;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<TenantOperationalResetService> _logger;

        private static readonly string[] DemoEmailSubstrings = { "test", "demo", "example.com", "hardwarepos.local" };
        private static readonly string[] DemoUsernamePrefixes = { "test_", "demo_", "qa_", "rc" };
        /// <summary>Users created only by human onboarding simulation (not production accounts).</summary>
        private static readonly HashSet<string> SimulationOnlyUsernames =
            new(StringComparer.OrdinalIgnoreCase) { "buildflow_cashier" };

        public TenantOperationalResetService(
            ApplicationDbContext platform,
            ITenantOperationalContextProvider ctxProvider,
            ITenantDatabaseResolver resolver,
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            IWebHostEnvironment env,
            ILogger<TenantOperationalResetService> logger)
        {
            _platform = platform;
            _ctxProvider = ctxProvider;
            _resolver = resolver;
            _userManager = userManager;
            _roleManager = roleManager;
            _env = env;
            _logger = logger;
        }

        public async Task<TenantResetReport> PreviewAsync(TenantResetOptions options) =>
            await ExecuteAsync(options, dryRun: true);

        public async Task<TenantResetReport> ExecuteAsync(TenantResetOptions options) =>
            await ExecuteAsync(options, dryRun: false);

        private async Task<TenantResetReport> ExecuteAsync(TenantResetOptions options, bool dryRun)
        {
            if (!_env.IsDevelopment())
                throw new InvalidOperationException("Tenant reset is allowed only in Development.");

            var tenant = await ResolveTenantAsync(options);
            var retainAdmin = await ResolveRetainAdminAsync(tenant.Id, options.RetainAdminUsername);

            var routingActive = await _resolver.IsRoutingActiveAsync(tenant.Id);
            var dbName = await _resolver.GetDatabaseNameAsync(tenant.Id);
            var maskedCs = TenantDatabaseResolver.Mask(
                routingActive ? tenant.ConnectionString : _platform.Database.GetConnectionString());

            var opCtx = await _ctxProvider.GetContextAsync(tenant.Id);
            var isDedicated = routingActive && opCtx is TenantDbContext;

            var report = new TenantResetReport
            {
                DryRun = dryRun,
                TenantId = tenant.Id,
                TenantCode = tenant.Code,
                TenantName = tenant.Name,
                RoutingEnabled = tenant.RoutingEnabled,
                RoutingActive = routingActive,
                DatabaseMode = tenant.DatabaseMode.ToString(),
                TargetDatabase = dbName,
                MaskedConnection = maskedCs,
                RetainedAdminUsername = retainAdmin.UserName ?? retainAdmin.Id,
                RetainedAdminId = retainAdmin.Id,
            };

            _logger.LogWarning("[TenantReset] {Mode} tenant {TenantId} ({Code}) — DB: {Db}",
                dryRun ? "PREVIEW" : "EXECUTE", tenant.Id, tenant.Code, maskedCs);

            report.BeforeCounts = await CountOperationalTablesAsync(opCtx, tenant.Id, isDedicated);
            report.UsersToDelete = await PreviewTenantUsersAsync(tenant.Id, retainAdmin.Id);

            if (dryRun)
            {
                _logger.LogWarning("[TenantReset] Preview complete — pass --confirm to execute.");
                return report;
            }

            await using var tx = opCtx is DbContext db
                ? await db.Database.BeginTransactionAsync()
                : null;

            try
            {
                await ClearOperationalDataAsync(opCtx, tenant.Id, isDedicated);
                await RemoveTestTenantUsersAsync(report.UsersToDelete);
                await ClearSharedStaleDataAsync(tenant.Id, isDedicated);

                if (tx != null)
                    await tx.CommitAsync();

                report.AfterCounts = await CountOperationalTablesAsync(opCtx, tenant.Id, isDedicated);
                report.Success = true;
                _logger.LogWarning("[TenantReset] Reset complete for tenant {TenantId}.", tenant.Id);
            }
            catch
            {
                if (tx != null)
                    await tx.RollbackAsync();
                throw;
            }

            return report;
        }

        private async Task<Tenant> ResolveTenantAsync(TenantResetOptions options)
        {
            Tenant? tenant = null;
            if (options.TenantId.HasValue)
                tenant = await _platform.Tenants.FirstOrDefaultAsync(t => t.Id == options.TenantId.Value);
            else if (!string.IsNullOrWhiteSpace(options.TenantCode))
                tenant = await _platform.Tenants.FirstOrDefaultAsync(t => t.Code == options.TenantCode);

            if (tenant == null)
                throw new InvalidOperationException("Tenant not found. Specify --tenant-id or --tenant-code.");

            return tenant;
        }

        private async Task<ApplicationUser> ResolveRetainAdminAsync(int tenantId, string? username)
        {
            ApplicationUser? user = null;

            if (!string.IsNullOrWhiteSpace(username))
            {
                user = await _userManager.Users.FirstOrDefaultAsync(u =>
                    u.TenantId == tenantId && u.UserName == username);
                if (user == null)
                    throw new InvalidOperationException($"Retain admin '{username}' not found for tenant {tenantId}.");
            }
            else
            {
                var tenantUsers = await _userManager.Users.Where(u => u.TenantId == tenantId && u.IsActive).ToListAsync();
                foreach (var u in tenantUsers)
                {
                    if (await _userManager.IsInRoleAsync(u, "TenantAdmin"))
                    {
                        user = u;
                        break;
                    }
                }
                user ??= tenantUsers.FirstOrDefault();
            }

            if (user == null)
                throw new InvalidOperationException($"No tenant administrator found for tenant {tenantId}.");

            if (await _userManager.IsInRoleAsync(user, "SuperAdmin"))
                throw new InvalidOperationException("Cannot use SuperAdmin as retained tenant administrator.");

            return user;
        }

        private async Task<List<UserDeletePreview>> PreviewTenantUsersAsync(int tenantId, string retainAdminId)
        {
            var previews = new List<UserDeletePreview>();
            var users = await _userManager.Users.Where(u => u.TenantId == tenantId).ToListAsync();

            foreach (var u in users)
            {
                if (u.Id == retainAdminId) continue;
                if (await _userManager.IsInRoleAsync(u, "SuperAdmin")) continue;

                var reason = GetTestUserReason(u);
                if (reason != null)
                    previews.Add(new UserDeletePreview(u.UserName ?? u.Id, u.Email ?? "", reason));
            }

            return previews;
        }

        private static string? GetTestUserReason(ApplicationUser user)
        {
            var username = user.UserName ?? "";
            var email = user.Email ?? "";

            foreach (var p in DemoUsernamePrefixes)
                if (username.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                    return $"username prefix '{p}'";

            foreach (var s in DemoEmailSubstrings)
                if (email.Contains(s, StringComparison.OrdinalIgnoreCase))
                    return $"email contains '{s}'";

            if (username.Equals("cashier", StringComparison.OrdinalIgnoreCase))
                return "known demo username 'cashier'";

            if (SimulationOnlyUsernames.Contains(username))
                return "simulation-only account";

            return null;
        }

        private async Task RemoveTestTenantUsersAsync(List<UserDeletePreview> users)
        {
            foreach (var preview in users)
            {
                var user = await _userManager.FindByNameAsync(preview.Username);
                if (user == null) continue;

                var result = await _userManager.DeleteAsync(user);
                if (result.Succeeded)
                    _logger.LogInformation("[TenantReset] Removed test user '{User}' ({Reason}).",
                        preview.Username, preview.Reason);
                else
                    _logger.LogWarning("[TenantReset] Could not remove '{User}': {Err}",
                        preview.Username, string.Join(", ", result.Errors.Select(e => e.Description)));
            }
        }

        /// <summary>Dev-only password rotation for retained tenant admin (separate from operational reset).</summary>
        public async Task SetRetainedAdminPasswordAsync(int tenantId, string username, string password)
        {
            if (!_env.IsDevelopment())
                throw new InvalidOperationException("Password reset is allowed only in Development.");

            var user = await ResolveRetainAdminAsync(tenantId, username);
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var result = await _userManager.ResetPasswordAsync(user, token, password);
            if (!result.Succeeded)
                throw new InvalidOperationException(
                    "Could not set retained admin password: " +
                    string.Join(", ", result.Errors.Select(e => e.Description)));
            user.ForcePasswordChange = false;
            await _userManager.UpdateAsync(user);
            _logger.LogInformation("[TenantReset] Dev password rotated for retained admin '{User}'.", user.UserName);
        }

        private static async Task ClearOperationalDataAsync(
            ITenantOperationalDbContext ctx, int tenantId, bool isDedicated)
        {
            // FK-safe order — children before parents.
            await DeleteSet(ctx.DeliveryReceiptItems, isDedicated, tenantId, e => e.DeliveryReceipt!.TenantId);
            await DeleteSet(ctx.DeliveryReceipts, isDedicated, tenantId, e => e.TenantId);
            await DeleteSet(ctx.QuotationItems, isDedicated, tenantId, e => e.Quotation!.TenantId);
            await DeleteSet(ctx.Quotations, isDedicated, tenantId, e => e.TenantId);
            await DeleteSet(ctx.SalesReturnDetails, isDedicated, tenantId, e => e.SalesReturnHeader!.TenantId);
            await DeleteSet(ctx.SalesReturnHeaders, isDedicated, tenantId, e => e.TenantId);
            await DeleteSet(ctx.SalesDetails, isDedicated, tenantId, e => e.SalesHeader!.TenantId);
            await DeleteSet(ctx.SalesHeaders, isDedicated, tenantId, e => e.TenantId);
            await DeleteSet(ctx.SupplierPayments, isDedicated, tenantId, e => e.Supplier!.TenantId);
            await DeleteSet(ctx.CustomerLedgers, isDedicated, tenantId, e => e.Customer!.TenantId);
            await DeleteSet(ctx.Expenses, isDedicated, tenantId, e => e.TenantId);
            await DeleteSet(ctx.StockInDetails, isDedicated, tenantId, e => e.StockInHeader!.TenantId);
            await DeleteSet(ctx.StockInHeaders, isDedicated, tenantId, e => e.TenantId);
            await DeleteSet(ctx.PurchaseOrderItems, isDedicated, tenantId, e => e.PurchaseOrder!.TenantId);
            await DeleteSet(ctx.PurchaseOrders, isDedicated, tenantId, e => e.TenantId);
            await DeleteSet(ctx.StockAdjustmentDetails, isDedicated, tenantId, e => e.StockAdjustmentHeader!.TenantId);
            await DeleteSet(ctx.StockAdjustmentHeaders, isDedicated, tenantId, e => e.TenantId);
            await DeleteSet(ctx.BranchTransferItems, isDedicated, tenantId, e => e.BranchTransfer!.TenantId);
            await DeleteSet(ctx.BranchTransfers, isDedicated, tenantId, e => e.TenantId);
            await DeleteSet(ctx.SupplierReturnDetails, isDedicated, tenantId, e => e.SupplierReturnHeader!.TenantId);
            await DeleteSet(ctx.SupplierReturnHeaders, isDedicated, tenantId, e => e.TenantId);
            await DeleteSet(ctx.DamagedGoodsDetails, isDedicated, tenantId, e => e.DamagedGoodsHeader!.TenantId);
            await DeleteSet(ctx.DamagedGoodsHeaders, isDedicated, tenantId, e => e.TenantId);
            await DeleteSet(ctx.ImportBatchRows, isDedicated, tenantId, e => e.ImportBatch!.TenantId);
            await DeleteSet(ctx.ImportBatches, isDedicated, tenantId, e => e.TenantId);
            await DeleteSet(ctx.Notifications, isDedicated, tenantId, e => e.TenantId);
            await DeleteSet(ctx.AuditTrails, isDedicated, tenantId, e => e.TenantId);
            await DeleteSet(ctx.BranchProductStocks, isDedicated, tenantId, e => e.TenantId);
            await DeleteSet(ctx.ItemUnitConversions, isDedicated, tenantId, e => e.TenantId);
            await DeleteSet(ctx.UserBranches, isDedicated, tenantId, _ => tenantId); // dedicated: all rows
            await DeleteSet(ctx.Items, isDedicated, tenantId, e => e.TenantId);
            await DeleteSet(ctx.Customers, isDedicated, tenantId, e => e.TenantId);
            await DeleteSet(ctx.Suppliers, isDedicated, tenantId, e => e.TenantId);
            await DeleteSet(ctx.Categories, isDedicated, tenantId, e => e.TenantId);
            await DeleteSet(ctx.Units, isDedicated, tenantId, e => e.TenantId);
            await DeleteSet(ctx.Branches, isDedicated, tenantId, e => e.TenantId);
            await DeleteSet(ctx.SystemSettings, isDedicated, tenantId, e => e.TenantId);
        }

        private static async Task DeleteSet<T>(
            DbSet<T> set,
            bool isDedicated,
            int tenantId,
            Expression<Func<T, int?>> tenantSelector) where T : class
        {
            if (isDedicated)
            {
                await set.ExecuteDeleteAsync();
                return;
            }

            // Shared DB — filter by TenantId via compiled expression when possible.
            if (typeof(T) == typeof(UserBranch))
            {
                // UserBranches have no TenantId — remove all for tenant branches in shared mode.
                await set.ExecuteDeleteAsync();
                return;
            }

            var param = tenantSelector.Parameters[0];
            var body = Expression.Equal(tenantSelector.Body, Expression.Constant(tenantId, typeof(int?)));
            var lambda = Expression.Lambda<Func<T, bool>>(body, param);
            await set.Where(lambda).ExecuteDeleteAsync();
        }

        /// <summary>Remove stale rows left in shared DB after dedicated cutover.</summary>
        private async Task ClearSharedStaleDataAsync(int tenantId, bool isDedicated)
        {
            if (!isDedicated) return;

            _logger.LogInformation("[TenantReset] Clearing stale shared-DB rows for tenant {TenantId}...", tenantId);
            await _platform.DeliveryReceiptItems.Where(e => e.DeliveryReceipt!.TenantId == tenantId).ExecuteDeleteAsync();
            await _platform.DeliveryReceipts.Where(e => e.TenantId == tenantId).ExecuteDeleteAsync();
            await _platform.QuotationItems.Where(e => e.Quotation!.TenantId == tenantId).ExecuteDeleteAsync();
            await _platform.Quotations.Where(e => e.TenantId == tenantId).ExecuteDeleteAsync();
            await _platform.SalesReturnDetails.Where(e => e.SalesReturnHeader!.TenantId == tenantId).ExecuteDeleteAsync();
            await _platform.SalesReturnHeaders.Where(e => e.TenantId == tenantId).ExecuteDeleteAsync();
            await _platform.SalesDetails.Where(e => e.SalesHeader!.TenantId == tenantId).ExecuteDeleteAsync();
            await _platform.SalesHeaders.Where(e => e.TenantId == tenantId).ExecuteDeleteAsync();
            await _platform.SupplierPayments.Where(e => e.Supplier!.TenantId == tenantId).ExecuteDeleteAsync();
            await _platform.CustomerLedgers.Where(e => e.Customer!.TenantId == tenantId).ExecuteDeleteAsync();
            await _platform.Expenses.Where(e => e.TenantId == tenantId).ExecuteDeleteAsync();
            await _platform.StockInDetails.Where(e => e.StockInHeader!.TenantId == tenantId).ExecuteDeleteAsync();
            await _platform.StockInHeaders.Where(e => e.TenantId == tenantId).ExecuteDeleteAsync();
            await _platform.PurchaseOrderItems.Where(e => e.PurchaseOrder!.TenantId == tenantId).ExecuteDeleteAsync();
            await _platform.PurchaseOrders.Where(e => e.TenantId == tenantId).ExecuteDeleteAsync();
            await _platform.StockAdjustmentDetails.Where(e => e.StockAdjustmentHeader!.TenantId == tenantId).ExecuteDeleteAsync();
            await _platform.StockAdjustmentHeaders.Where(e => e.TenantId == tenantId).ExecuteDeleteAsync();
            await _platform.BranchTransferItems.Where(e => e.BranchTransfer!.TenantId == tenantId).ExecuteDeleteAsync();
            await _platform.BranchTransfers.Where(e => e.TenantId == tenantId).ExecuteDeleteAsync();
            await _platform.SupplierReturnDetails.Where(e => e.SupplierReturnHeader!.TenantId == tenantId).ExecuteDeleteAsync();
            await _platform.SupplierReturnHeaders.Where(e => e.TenantId == tenantId).ExecuteDeleteAsync();
            await _platform.DamagedGoodsDetails.Where(e => e.DamagedGoodsHeader!.TenantId == tenantId).ExecuteDeleteAsync();
            await _platform.DamagedGoodsHeaders.Where(e => e.TenantId == tenantId).ExecuteDeleteAsync();
            await _platform.ImportBatchRows.Where(e => e.ImportBatch!.TenantId == tenantId).ExecuteDeleteAsync();
            await _platform.ImportBatches.Where(e => e.TenantId == tenantId).ExecuteDeleteAsync();
            await _platform.Notifications.Where(e => e.TenantId == tenantId).ExecuteDeleteAsync();
            await _platform.AuditTrails.Where(e => e.TenantId == tenantId).ExecuteDeleteAsync();
            await _platform.BranchProductStocks.Where(e => e.TenantId == tenantId).ExecuteDeleteAsync();
            await _platform.ItemUnitConversions.Where(e => e.TenantId == tenantId).ExecuteDeleteAsync();
            await _platform.Items.Where(e => e.TenantId == tenantId).ExecuteDeleteAsync();
            await _platform.Customers.Where(e => e.TenantId == tenantId).ExecuteDeleteAsync();
            await _platform.Suppliers.Where(e => e.TenantId == tenantId).ExecuteDeleteAsync();
            await _platform.Categories.Where(e => e.TenantId == tenantId).ExecuteDeleteAsync();
            await _platform.Units.Where(e => e.TenantId == tenantId).ExecuteDeleteAsync();
            await _platform.Branches.Where(e => e.TenantId == tenantId).ExecuteDeleteAsync();
            await _platform.SystemSettings.Where(e => e.TenantId == tenantId).ExecuteDeleteAsync();
        }

        private static async Task<Dictionary<string, int>> CountOperationalTablesAsync(
            ITenantOperationalDbContext ctx, int tenantId, bool isDedicated)
        {
            async Task<int> C<T>(IQueryable<T> q) where T : class
            {
                try { return await q.CountAsync(); }
                catch { return -1; }
            }

            if (isDedicated)
            {
                return new Dictionary<string, int>
                {
                    ["Branches"] = await C(ctx.Branches),
                    ["Items"] = await C(ctx.Items),
                    ["Categories"] = await C(ctx.Categories),
                    ["Units"] = await C(ctx.Units),
                    ["Customers"] = await C(ctx.Customers),
                    ["Suppliers"] = await C(ctx.Suppliers),
                    ["SalesHeaders"] = await C(ctx.SalesHeaders),
                    ["PurchaseOrders"] = await C(ctx.PurchaseOrders),
                    ["StockInHeaders"] = await C(ctx.StockInHeaders),
                    ["SystemSettings"] = await C(ctx.SystemSettings),
                    ["AuditTrails"] = await C(ctx.AuditTrails),
                };
            }

            return new Dictionary<string, int>
            {
                ["Branches"] = await C(ctx.Branches.Where(e => e.TenantId == tenantId)),
                ["Items"] = await C(ctx.Items.Where(e => e.TenantId == tenantId)),
                ["SalesHeaders"] = await C(ctx.SalesHeaders.Where(e => e.TenantId == tenantId)),
                ["PurchaseOrders"] = await C(ctx.PurchaseOrders.Where(e => e.TenantId == tenantId)),
                ["SystemSettings"] = await C(ctx.SystemSettings.Where(e => e.TenantId == tenantId)),
            };
        }
    }

    public sealed class TenantResetOptions
    {
        public int? TenantId { get; init; }
        public string? TenantCode { get; init; }
        public string? RetainAdminUsername { get; init; }
        public bool Confirm { get; init; }
    }

    public sealed class TenantResetReport
    {
        public bool DryRun { get; set; }
        public bool Success { get; set; }
        public int TenantId { get; set; }
        public string TenantCode { get; set; } = "";
        public string TenantName { get; set; } = "";
        public bool RoutingEnabled { get; set; }
        public bool RoutingActive { get; set; }
        public string DatabaseMode { get; set; } = "";
        public string TargetDatabase { get; set; } = "";
        public string MaskedConnection { get; set; } = "";
        public string RetainedAdminUsername { get; set; } = "";
        public string RetainedAdminId { get; set; } = "";
        public Dictionary<string, int> BeforeCounts { get; set; } = new();
        public Dictionary<string, int> AfterCounts { get; set; } = new();
        public List<UserDeletePreview> UsersToDelete { get; set; } = new();

        public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
    }

    public sealed record UserDeletePreview(string Username, string Email, string Reason);
}

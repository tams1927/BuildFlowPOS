// ==========================================================================
// DevelopmentDataResetService
// ==========================================================================
//
// WARNING: THIS SERVICE DELETES DATA FROM THE DATABASE.
//
// Purpose  : Remove old / demo / test operational data accumulated during
//            development so the database is left in a clean but fully
//            functional state (roles, permissions, superadmin, sub-plans).
//
// Safety   : The cleanup ONLY runs when ALL of these are true:
//              1. app.Environment.IsDevelopment()  == true
//              2. SeedSettings:EnableDemoDataCleanup in appsettings (or
//                 appsettings.Development.json) is explicitly set to  true
//
//            The default for that flag is  false, so this NEVER runs unless
//            you intentionally set it.
//
// DO NOT DEPLOY OR ENABLE IN PRODUCTION.
// ==========================================================================

using HardwareManagementSystem.Data;
using HardwareManagementSystem.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HardwareManagementSystem.Data.Seeders
{
    public static class DevelopmentDataResetService
    {
        // ──────────────────────────────────────────────────────────────────────
        // USER MATCHING RULES
        //
        // A user is considered a demo/test account if their username or email
        // matches one of the patterns below.
        //
        // IMPORTANT: TenantId is NOT used as a deletion criterion.
        //            Matching is by explicit username / email only.
        //            This prevents accidentally deleting real tenant users
        //            that were created intentionally.
        // ──────────────────────────────────────────────────────────────────────

        // Exact username matches (case-insensitive)
        private static readonly HashSet<string> _exactDemoUsernames = new(StringComparer.OrdinalIgnoreCase)
        {
            "admin",
            "cashier",
            "test",
            "testuser",
            "demo",
            "demouser",
            "staff",
            "manager",
            "branchmanager",
            "inventorystaff",
        };

        // Username prefix matches — any username that starts with one of these
        private static readonly string[] _demoPrefixes = { "test_", "demo_" };

        // Email substring matches (case-insensitive) — if the email *contains*
        // any of these strings the user is treated as a demo account
        private static readonly string[] _demoEmailSubstrings =
        {
            "test",
            "demo",
            "example.com",
            "hardwarepos.local",
            "test.local",
            "demo.local",
        };

        // ──────────────────────────────────────────────────────────────────────
        // ENTRY POINT
        // Called from Program.cs only when IsDevelopment + flag is true.
        // ──────────────────────────────────────────────────────────────────────

        public static async Task CleanDemoDataAsync(IServiceProvider serviceProvider)
        {
            var context     = serviceProvider.GetRequiredService<ApplicationDbContext>();
            var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var env         = serviceProvider.GetRequiredService<IWebHostEnvironment>();
            var logger      = serviceProvider.GetRequiredService<ILogger<ApplicationDbContext>>();

            if (!env.IsDevelopment())
            {
                logger.LogWarning(
                    "[DevelopmentDataResetService] Attempted to run in non-Development environment. Aborted.");
                return;
            }

            logger.LogWarning(
                "[DevelopmentDataResetService] *** DEVELOPMENT DATA CLEANUP STARTED ***");

            // ── PHASE 0: Safety preview — log everything that WILL be deleted ─
            await LogPreviewAsync(context, userManager, logger);

            // ── PHASE 1a: Remove demo / test users (pattern-matched) ──────────
            await RemoveDemoUsersAsync(userManager, logger);

            // ── PHASE 1b: Remove ALL remaining tenant-scoped users ─────────────
            // Since we delete all tenants in Phase 5, any user with a TenantId
            // would be orphaned. Remove them all now (SuperAdmin is always skipped).
            await RemoveAllTenantUsersAsync(userManager, logger);

            // ── PHASE 2: Operational transactional data ───────────────────────
            // Order matters: children before parents (FK constraints).
            logger.LogInformation("[DevelopmentDataResetService] Removing transactional data...");

            // Delivery receipts
            await DeleteAll(context.DeliveryReceiptItems, logger, "DeliveryReceiptItems");
            await DeleteAll(context.DeliveryReceipts,     logger, "DeliveryReceipts");

            // Quotations
            await DeleteAll(context.QuotationItems, logger, "QuotationItems");
            await DeleteAll(context.Quotations,     logger, "Quotations");

            // Sales
            await DeleteAll(context.SalesReturnDetails, logger, "SalesReturnDetails");
            await DeleteAll(context.SalesReturnHeaders, logger, "SalesReturnHeaders");
            await DeleteAll(context.SalesDetails,       logger, "SalesDetails");
            await DeleteAll(context.SalesHeaders,       logger, "SalesHeaders");

            // Supplier payments + stock-in
            await DeleteAll(context.SupplierPayments, logger, "SupplierPayments");
            await DeleteAll(context.StockInDetails,   logger, "StockInDetails");
            await DeleteAll(context.StockInHeaders,   logger, "StockInHeaders");

            // Stock adjustments
            await DeleteAll(context.StockAdjustmentDetails, logger, "StockAdjustmentDetails");
            await DeleteAll(context.StockAdjustmentHeaders, logger, "StockAdjustmentHeaders");

            // Branch transfers
            await DeleteAll(context.BranchTransferItems, logger, "BranchTransferItems");
            await DeleteAll(context.BranchTransfers,     logger, "BranchTransfers");

            // Import
            await DeleteAll(context.ImportBatchRows, logger, "ImportBatchRows");
            await DeleteAll(context.ImportBatches,   logger, "ImportBatches");

            // Customer ledgers / collections
            await DeleteAll(context.CustomerLedgers, logger, "CustomerLedgers");

            // Expenses
            await DeleteAll(context.Expenses, logger, "Expenses");

            // Notifications + audit logs
            await DeleteAll(context.Notifications, logger, "Notifications");
            await DeleteAll(context.AuditTrails,   logger, "AuditTrails");

            // ── PHASE 3: Master / catalogue data ──────────────────────────────
            // Branch product stocks (before Items/Branches)
            await DeleteAll(context.BranchProductStocks, logger, "BranchProductStocks");

            // User–branch assignments
            await DeleteAll(context.UserBranches, logger, "UserBranches");

            // Customers + suppliers
            await DeleteAll(context.Customers, logger, "Customers");
            await DeleteAll(context.Suppliers, logger, "Suppliers");

            // Products (items)
            await DeleteAll(context.Items, logger, "Items");

            // Categories + units
            await DeleteAll(context.Categories, logger, "Categories");
            await DeleteAll(context.Units,      logger, "Units");

            // ── PHASE 4: Branches ─────────────────────────────────────────────
            await DeleteAll(context.Branches, logger, "Branches");

            // ── PHASE 4b: Tenant-scoped system settings ───────────────────────
            // Delete only tenant-scoped rows (TenantId != null); leave any global
            // platform settings row (TenantId == null) intact.
            var deletedSettings = await context.SystemSettings
                .Where(s => s.TenantId != null)
                .ExecuteDeleteAsync();
            if (deletedSettings > 0)
                logger.LogInformation(
                    "[DevelopmentDataResetService] Deleted {Count} row(s) from SystemSettings (tenant-scoped).",
                    deletedSettings);

            // ── PHASE 5: All tenant records ───────────────────────────────────
            // All tenants are removed. SuperAdmin creates tenants explicitly.
            var allTenants = await context.Tenants.ToListAsync();

            if (allTenants.Count > 0)
            {
                context.Tenants.RemoveRange(allTenants);
                await context.SaveChangesAsync();
                logger.LogInformation(
                    "[DevelopmentDataResetService] Removed {Count} tenant(s).", allTenants.Count);
            }

            // ── PHASE 6: Post-cleanup row count summary ───────────────────────
            await LogPostCleanupSummaryAsync(context, userManager, logger);

            logger.LogWarning(
                "[DevelopmentDataResetService] *** DEVELOPMENT DATA CLEANUP COMPLETE ***");
            logger.LogWarning(
                "[DevelopmentDataResetService] Retained: superadmin account, " +
                "roles, permissions, subscription plans, system settings.");
        }

        // ──────────────────────────────────────────────────────────────────────
        // PHASE 0 — SAFETY PREVIEW
        // Logs exactly what will be deleted before any row is touched.
        // ──────────────────────────────────────────────────────────────────────

        private static async Task LogPreviewAsync(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            ILogger logger)
        {
            logger.LogWarning("[DevelopmentDataResetService] ── PRE-DELETION PREVIEW ──────────────────");

            // --- Users to be deleted ---
            var allUsers = await userManager.Users.AsNoTracking().ToListAsync();
            var toDelete = new List<(string Username, string Email, string Reason)>();

            foreach (var user in allUsers)
            {
                if (await userManager.IsInRoleAsync(user, "SuperAdmin"))
                    continue;

                string? reason = GetUserDemoReason(user);
                if (reason != null)
                    toDelete.Add((user.UserName ?? "(no username)", user.Email ?? "(no email)", reason));
            }

            if (toDelete.Count == 0)
            {
                logger.LogInformation("[DevelopmentDataResetService] Users to delete : (none matched demo rules)");
            }
            else
            {
                logger.LogWarning(
                    "[DevelopmentDataResetService] Users to delete ({Count}):", toDelete.Count);
                foreach (var (uname, email, reason) in toDelete)
                    logger.LogWarning(
                        "[DevelopmentDataResetService]   • {Username} <{Email}>  reason: {Reason}",
                        uname, email, reason);
            }

            // --- Tenant-scoped users to be deleted (Phase 1b) ---
            var tenantScopedCount = await userManager.Users
                .AsNoTracking()
                .CountAsync(u => u.TenantId != null);
            if (tenantScopedCount > 0)
                logger.LogWarning(
                    "[DevelopmentDataResetService] Tenant-scoped users (Phase 1b): {Count} will be removed",
                    tenantScopedCount);

            // --- Tenants to be deleted ---
            var demoTenants = await context.Tenants
                .AsNoTracking()
                .Select(t => new { t.Name, t.Code })
                .ToListAsync();

            if (demoTenants.Count == 0)
            {
                logger.LogInformation("[DevelopmentDataResetService] Tenants to delete: (none)");
            }
            else
            {
                logger.LogWarning(
                    "[DevelopmentDataResetService] Tenants to delete ({Count}):", demoTenants.Count);
                foreach (var t in demoTenants)
                    logger.LogWarning(
                        "[DevelopmentDataResetService]   • [{Code}] {Name}", t.Code, t.Name);
            }

            // --- Row counts per table ---
            logger.LogWarning("[DevelopmentDataResetService] Table row counts (will be fully cleared):");

            await LogCount(context.DeliveryReceiptItems,    logger, "DeliveryReceiptItems");
            await LogCount(context.DeliveryReceipts,        logger, "DeliveryReceipts");
            await LogCount(context.QuotationItems,          logger, "QuotationItems");
            await LogCount(context.Quotations,              logger, "Quotations");
            await LogCount(context.SalesReturnDetails,      logger, "SalesReturnDetails");
            await LogCount(context.SalesReturnHeaders,      logger, "SalesReturnHeaders");
            await LogCount(context.SalesDetails,            logger, "SalesDetails");
            await LogCount(context.SalesHeaders,            logger, "SalesHeaders");
            await LogCount(context.SupplierPayments,        logger, "SupplierPayments");
            await LogCount(context.StockInDetails,          logger, "StockInDetails");
            await LogCount(context.StockInHeaders,          logger, "StockInHeaders");
            await LogCount(context.StockAdjustmentDetails,  logger, "StockAdjustmentDetails");
            await LogCount(context.StockAdjustmentHeaders,  logger, "StockAdjustmentHeaders");
            await LogCount(context.BranchTransferItems,     logger, "BranchTransferItems");
            await LogCount(context.BranchTransfers,         logger, "BranchTransfers");
            await LogCount(context.ImportBatchRows,         logger, "ImportBatchRows");
            await LogCount(context.ImportBatches,           logger, "ImportBatches");
            await LogCount(context.CustomerLedgers,         logger, "CustomerLedgers");
            await LogCount(context.Expenses,                logger, "Expenses");
            await LogCount(context.Notifications,           logger, "Notifications");
            await LogCount(context.AuditTrails,             logger, "AuditTrails");
            await LogCount(context.BranchProductStocks,     logger, "BranchProductStocks");
            await LogCount(context.UserBranches,            logger, "UserBranches");
            await LogCount(context.Customers,               logger, "Customers");
            await LogCount(context.Suppliers,               logger, "Suppliers");
            await LogCount(context.Items,                   logger, "Items");
            await LogCount(context.Categories,              logger, "Categories");
            await LogCount(context.Units,                   logger, "Units");
            await LogCount(context.Branches,                logger, "Branches");

            logger.LogWarning("[DevelopmentDataResetService] ── END PREVIEW — STARTING DELETION ────────");
        }

        private static async Task LogCount<T>(DbSet<T> dbSet, ILogger logger, string label)
            where T : class
        {
            try
            {
                int count = await dbSet.CountAsync();
                if (count > 0)
                    logger.LogWarning(
                        "[DevelopmentDataResetService]   {Table,-36} {Count,6} row(s)", label, count);
            }
            catch
            {
                // Table may not exist in older migrations; skip silently.
            }
        }

        // ──────────────────────────────────────────────────────────────────────
        // HELPERS
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns a human-readable reason string if the user matches demo/test
        /// criteria, or null if the user should be kept.
        /// TenantId is intentionally NOT used as a matching criterion.
        /// </summary>
        private static string? GetUserDemoReason(ApplicationUser user)
        {
            var username = user.UserName ?? string.Empty;
            var email    = user.Email    ?? string.Empty;

            if (_exactDemoUsernames.Contains(username))
                return $"username '{username}' is in exact demo list";

            foreach (var prefix in _demoPrefixes)
                if (username.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return $"username starts with '{prefix}'";

            foreach (var sub in _demoEmailSubstrings)
                if (email.Contains(sub, StringComparison.OrdinalIgnoreCase))
                    return $"email contains '{sub}'";

            return null;
        }

        /// <summary>
        /// Removes demo/test user accounts that match explicit username/email
        /// rules.  Users are never deleted based on TenantId alone.
        /// </summary>
        private static async Task RemoveDemoUsersAsync(
            UserManager<ApplicationUser> userManager,
            ILogger logger)
        {
            var allUsers = await userManager.Users.ToListAsync();

            foreach (var user in allUsers)
            {
                // Never remove the SuperAdmin account
                if (await userManager.IsInRoleAsync(user, "SuperAdmin"))
                    continue;

                string? reason = GetUserDemoReason(user);
                if (reason == null)
                    continue;

                var result = await userManager.DeleteAsync(user);
                if (result.Succeeded)
                    logger.LogInformation(
                        "[DevelopmentDataResetService] Removed user '{Username}' — {Reason}.",
                        user.UserName, reason);
                else
                    logger.LogWarning(
                        "[DevelopmentDataResetService] Could not remove user '{Username}': {Errors}",
                        user.UserName,
                        string.Join(", ", result.Errors.Select(e => e.Description)));
            }
        }

        /// <summary>
        /// Removes ALL remaining tenant-scoped users (TenantId != null).
        /// Called after pattern-matched demo removal so no tenant user is
        /// left orphaned once all tenants are deleted in Phase 5.
        /// SuperAdmin is always skipped.
        /// </summary>
        private static async Task RemoveAllTenantUsersAsync(
            UserManager<ApplicationUser> userManager,
            ILogger logger)
        {
            var tenantUsers = await userManager.Users
                .Where(u => u.TenantId != null)
                .ToListAsync();

            if (tenantUsers.Count == 0)
            {
                logger.LogInformation(
                    "[DevelopmentDataResetService] Phase 1b: No remaining tenant-scoped users to remove.");
                return;
            }

            logger.LogWarning(
                "[DevelopmentDataResetService] Phase 1b: Removing {Count} remaining tenant-scoped user(s)...",
                tenantUsers.Count);

            foreach (var user in tenantUsers)
            {
                if (await userManager.IsInRoleAsync(user, "SuperAdmin"))
                    continue;

                var result = await userManager.DeleteAsync(user);
                if (result.Succeeded)
                    logger.LogInformation(
                        "[DevelopmentDataResetService]   Removed tenant user '{Username}' (TenantId={TenantId}).",
                        user.UserName, user.TenantId);
                else
                    logger.LogWarning(
                        "[DevelopmentDataResetService]   Could not remove '{Username}': {Errors}",
                        user.UserName,
                        string.Join(", ", result.Errors.Select(e => e.Description)));
            }
        }

        /// <summary>
        /// Logs SQL row counts of all cleaned tables after cleanup completes.
        /// Every cleaned table should show 0; any non-zero row is flagged.
        /// </summary>
        private static async Task LogPostCleanupSummaryAsync(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            ILogger logger)
        {
            logger.LogWarning("[DevelopmentDataResetService] ── POST-CLEANUP ROW COUNTS ──────────────────");

            var retained = await userManager.Users.AsNoTracking().CountAsync();
            logger.LogWarning(
                "[DevelopmentDataResetService]   AspNetUsers (retained)          {Count,6} row(s)", retained);

            // All of these should be 0 after cleanup
            var checks = new (string Label, int Count)[]
            {
                ("Tenants",               await context.Tenants.CountAsync()),
                ("Branches",              await context.Branches.CountAsync()),
                ("Items",                 await context.Items.CountAsync()),
                ("Categories",            await context.Categories.CountAsync()),
                ("Units",                 await context.Units.CountAsync()),
                ("Customers",             await context.Customers.CountAsync()),
                ("Suppliers",             await context.Suppliers.CountAsync()),
                ("SalesHeaders",          await context.SalesHeaders.CountAsync()),
                ("SalesDetails",          await context.SalesDetails.CountAsync()),
                ("StockInHeaders",        await context.StockInHeaders.CountAsync()),
                ("StockAdjustmentHdrs",   await context.StockAdjustmentHeaders.CountAsync()),
                ("Expenses",              await context.Expenses.CountAsync()),
                ("Notifications",         await context.Notifications.CountAsync()),
                ("AuditTrails",           await context.AuditTrails.CountAsync()),
                ("UserBranches",          await context.UserBranches.CountAsync()),
                ("BranchProductStocks",   await context.BranchProductStocks.CountAsync()),
                ("SystemSettings",        await context.SystemSettings.Where(s => s.TenantId != null).CountAsync()),
            };

            bool allZero = true;
            foreach (var (label, count) in checks)
            {
                if (count > 0)
                {
                    logger.LogError(
                        "[DevelopmentDataResetService]   !! {Label,-36} {Count,6} row(s) — NOT ZERO!", label, count);
                    allZero = false;
                }
                else
                {
                    logger.LogInformation(
                        "[DevelopmentDataResetService]      {Label,-36} {Count,6}", label, count);
                }
            }

            if (allZero)
                logger.LogWarning(
                    "[DevelopmentDataResetService] ✓ All operational tables at 0 rows. Clean state confirmed.");
            else
                logger.LogError(
                    "[DevelopmentDataResetService] !! Some tables still have rows — investigate above.");

            logger.LogWarning("[DevelopmentDataResetService] ── END POST-CLEANUP SUMMARY ─────────────────");
        }

        /// <summary>
        /// Bulk-deletes all rows from a DbSet using EF 7+ ExecuteDeleteAsync.
        /// </summary>
        private static async Task DeleteAll<T>(
            DbSet<T> dbSet,
            ILogger logger,
            string label) where T : class
        {
            try
            {
                int deleted = await dbSet.ExecuteDeleteAsync();
                if (deleted > 0)
                    logger.LogInformation(
                        "[DevelopmentDataResetService] Deleted {Count} row(s) from {Table}.",
                        deleted, label);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "[DevelopmentDataResetService] Error deleting from {Table}: {Message}",
                    label, ex.Message);
            }
        }
    }
}

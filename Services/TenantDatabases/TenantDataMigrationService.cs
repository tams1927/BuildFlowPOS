using System.Data;
using HardwareManagementSystem.Data;
using HardwareManagementSystem.Models;
using HardwareManagementSystem.ViewModels;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace HardwareManagementSystem.Services.TenantDatabases
{
    /// <summary>
    /// Copies tenant operational rows from the shared <see cref="ApplicationDbContext"/>
    /// into the dedicated <see cref="TenantDbContext"/> database using SqlBulkCopy with
    /// KeepIdentity (primary keys, document numbers, dates and status are preserved
    /// exactly). The whole copy runs in one transaction; if post-copy row-count
    /// validation fails the transaction is rolled back and the tenant is NOT marked
    /// migrated.
    /// </summary>
    public sealed class TenantDataMigrationService : ITenantDataMigrationService
    {
        private readonly ApplicationDbContext _app;
        private readonly ITenantDbContextFactory _factory;
        private readonly ITenantDatabaseResolver _resolver;
        private readonly ILogger<TenantDataMigrationService> _logger;

        public TenantDataMigrationService(
            ApplicationDbContext app,
            ITenantDbContextFactory factory,
            ITenantDatabaseResolver resolver,
            ILogger<TenantDataMigrationService> logger)
        {
            _app      = app;
            _factory  = factory;
            _resolver = resolver;
            _logger   = logger;
        }

        public async Task<TenantDataMigrationResultVm> MigrateAsync(int tenantId)
        {
            var result = new TenantDataMigrationResultVm();

            // ── Load + validate tenant ───────────────────────────────────────
            var tenant = await _app.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId);
            if (tenant == null)
            {
                result.Message = "Tenant not found.";
                return result;
            }

            result.DatabaseName = tenant.DatabaseName ?? string.Empty;

            if (tenant.DatabaseMode != TenantDatabaseMode.Dedicated)
            {
                result.Message = "Tenant must be in Dedicated mode.";
                return result;
            }
            if (tenant.DatabaseProvisionedAtUtc == null || string.IsNullOrWhiteSpace(tenant.ConnectionString))
            {
                result.Message = "Dedicated database is not provisioned yet.";
                return result;
            }
            if (tenant.DataMigrated)
            {
                result.Message = "Data has already been migrated for this tenant.";
                return result;
            }

            // ── Pre-fetch parent IDs for child-table filtering ───────────────
            // All queries run against the SHARED database (ApplicationDbContext).
            var branchIds         = await IdsAsync(_app.Branches.Where(b => b.TenantId == tenantId).Select(b => b.Id));
            var customerIds       = await IdsAsync(_app.Customers.Where(c => c.TenantId == tenantId).Select(c => c.Id));
            var salesHeaderIds    = await IdsAsync(_app.SalesHeaders.Where(s => s.TenantId == tenantId).Select(s => s.Id));
            var salesReturnIds    = await IdsAsync(_app.SalesReturnHeaders.Where(s => s.TenantId == tenantId).Select(s => s.Id));
            var stockInIds        = await IdsAsync(_app.StockInHeaders.Where(s => s.TenantId == tenantId).Select(s => s.Id));
            var stockAdjIds       = await IdsAsync(_app.StockAdjustmentHeaders.Where(h => h.TenantId == tenantId).Select(h => h.Id));
            var branchTransferIds = await IdsAsync(_app.BranchTransfers.Where(t => t.TenantId == tenantId).Select(t => t.Id));
            var importBatchIds    = await IdsAsync(_app.ImportBatches.Where(i => i.TenantId == tenantId).Select(i => i.Id));
            var poIds             = await IdsAsync(_app.PurchaseOrders.Where(p => p.TenantId == tenantId).Select(p => p.Id));
            var quotationIds      = await IdsAsync(_app.Quotations.Where(q => q.TenantId == tenantId).Select(q => q.Id));
            var drIds             = await IdsAsync(_app.DeliveryReceipts.Where(d => d.TenantId == tenantId).Select(d => d.Id));
            var damagedGoodsIds   = await IdsAsync(_app.DamagedGoodsHeaders.Where(h => h.TenantId == tenantId).Select(h => h.Id));
            var supplierReturnIds = await IdsAsync(_app.SupplierReturnHeaders.Where(h => h.TenantId == tenantId).Select(h => h.Id));

            // ── Build the dependency-ordered copy plan ───────────────────────
            // ORDER IS CRITICAL: parents must appear before their children so that
            //   • INSERT phase resolves FKs correctly (parent row exists first), AND
            //   • DELETE phase (reverse order) removes children before parents so no
            //     FK constraint is violated during cleanup.
            //
            // Full dependency graph (→ means "FK points to"):
            //   SupplierPayments   → StockInHeaders, Suppliers
            //   SalesReturnHeaders → SalesHeaders
            //   SalesReturnDetails → SalesReturnHeaders, SalesDetails, Items
            //   StockAdjustmentDetails → StockAdjustmentHeaders, Items
            //   BranchTransferItems → BranchTransfers, Items
            //   ImportBatchRows    → ImportBatches
            var steps = new List<CopyStep>
            {
                // ── Reference / master data (no FK dependencies on each other) ──
                Step<SystemSetting>    ("SystemSettings",       _app.SystemSettings.Where(x => x.TenantId == tenantId)),
                Step<Branch>           ("Branches",             _app.Branches.Where(x => x.TenantId == tenantId)),
                Step<Category>         ("Categories",           _app.Categories.Where(x => x.TenantId == tenantId)),
                Step<Unit>             ("Units",                _app.Units.Where(x => x.TenantId == tenantId)),
                Step<Supplier>         ("Suppliers",            _app.Suppliers.Where(x => x.TenantId == tenantId)),
                Step<Customer>         ("Customers",            _app.Customers.Where(x => x.TenantId == tenantId)),
                Step<Item>             ("Items",                _app.Items.Where(x => x.TenantId == tenantId)),

                // ── Second-level: depend on master data ──
                Step<ItemUnitConversion>("ItemUnitConversions", _app.ItemUnitConversions.Where(x => x.TenantId == tenantId)),
                Step<BranchProductStock>("BranchProductStocks", _app.BranchProductStocks.Where(x => x.TenantId == tenantId)),
                Step<UserBranch>       ("UserBranches",         _app.UserBranches.Where(x => branchIds.Contains(x.BranchId))),
                Step<ImportBatch>      ("ImportBatches",        _app.ImportBatches.Where(x => x.TenantId == tenantId)),
                Step<ImportBatchRow>   ("ImportBatchRows",      _app.ImportBatchRows.Where(x => importBatchIds.Contains(x.ImportBatchId))),

                // ── Sales documents (headers then their detail lines and sub-documents) ──
                Step<SalesHeader>       ("SalesHeaders",        _app.SalesHeaders.Where(x => x.TenantId == tenantId)),
                Step<SalesDetail>       ("SalesDetails",        _app.SalesDetails.Where(x => salesHeaderIds.Contains(x.SalesHeaderId))),
                Step<SalesReturnHeader> ("SalesReturnHeaders",  _app.SalesReturnHeaders.Where(x => x.TenantId == tenantId)),
                // SalesReturnDetail → SalesReturnHeaders + SalesDetails + Items (all already loaded above)
                Step<SalesReturnDetail> ("SalesReturnDetails",  _app.SalesReturnDetails.Where(x => salesReturnIds.Contains(x.SalesReturnHeaderId))),
                Step<CustomerLedger>    ("CustomerLedgers",     _app.CustomerLedgers.Where(x => customerIds.Contains(x.CustomerId))),

                // ── Stock adjustments (depend on Branches + Items) ──
                Step<StockAdjustmentHeader>("StockAdjustmentHeaders", _app.StockAdjustmentHeaders.Where(x => x.TenantId == tenantId)),
                Step<StockAdjustmentDetail>("StockAdjustmentDetails", _app.StockAdjustmentDetails.Where(x => stockAdjIds.Contains(x.StockAdjustmentHeaderId))),

                // ── Branch transfers (depend on Branches + Items) ──
                Step<BranchTransfer>    ("BranchTransfers",     _app.BranchTransfers.Where(x => x.TenantId == tenantId)),
                Step<BranchTransferItem>("BranchTransferItems", _app.BranchTransferItems.Where(x => branchTransferIds.Contains(x.BranchTransferId))),

                // ── Purchasing / stock-in (depend on Suppliers) ──
                Step<StockInHeader>  ("StockInHeaders",         _app.StockInHeaders.Where(x => x.TenantId == tenantId)),
                Step<StockInDetail>  ("StockInDetails",         _app.StockInDetails.Where(x => stockInIds.Contains(x.StockInHeaderId))),
                // SupplierPayments → StockInHeaders + Suppliers (MUST come after both parents)
                Step<SupplierPayment>("SupplierPayments",        _app.SupplierPayments.Where(x => stockInIds.Contains(x.StockInHeaderId))),

                Step<Expense>         ("Expenses",              _app.Expenses.Where(x => x.TenantId == tenantId)),

                // ── Purchase orders ──
                Step<PurchaseOrder>    ("PurchaseOrders",       _app.PurchaseOrders.Where(x => x.TenantId == tenantId)),
                Step<PurchaseOrderItem>("PurchaseOrderItems",   _app.PurchaseOrderItems.Where(x => poIds.Contains(x.PurchaseOrderId))),

                // ── Quotations / delivery receipts ──
                Step<Quotation>         ("Quotations",          _app.Quotations.Where(x => x.TenantId == tenantId)),
                Step<QuotationItem>     ("QuotationItems",      _app.QuotationItems.Where(x => quotationIds.Contains(x.QuotationId))),
                Step<DeliveryReceipt>   ("DeliveryReceipts",    _app.DeliveryReceipts.Where(x => x.TenantId == tenantId)),
                Step<DeliveryReceiptItem>("DeliveryReceiptItems", _app.DeliveryReceiptItems.Where(x => drIds.Contains(x.DeliveryReceiptId))),

                // ── Damaged goods / supplier returns ──
                Step<DamagedGoodsHeader>  ("DamagedGoodsHeaders",    _app.DamagedGoodsHeaders.Where(x => x.TenantId == tenantId)),
                Step<DamagedGoodsDetail>  ("DamagedGoodsDetails",    _app.DamagedGoodsDetails.Where(x => damagedGoodsIds.Contains(x.DamagedGoodsHeaderId))),
                Step<SupplierReturnHeader>("SupplierReturnHeaders",   _app.SupplierReturnHeaders.Where(x => x.TenantId == tenantId)),
                Step<SupplierReturnDetail>("SupplierReturnDetails",   _app.SupplierReturnDetails.Where(x => supplierReturnIds.Contains(x.SupplierReturnHeaderId))),

                // ── Audit / notifications (no operational FK deps) ──
                Step<Notification>("Notifications", _app.Notifications.Where(x => x.TenantId == tenantId)),
                Step<AuditTrail>  ("AuditTrails",   _app.AuditTrails.Where(x => x.TenantId == tenantId)),
            };

            await using var dest = await _factory.CreateAsync(tenantId);
            await using var conn = new SqlConnection(tenant.ConnectionString);
            await conn.OpenAsync();
            await using var tx = (SqlTransaction)await conn.BeginTransactionAsync();

            try
            {
                _logger.LogInformation("Starting data migration for tenant {TenantId}.", tenantId);

                // ── Step 1: Detect whether the destination is a fresh (empty) database.
                // A newly-provisioned dedicated database has never had any data written
                // to it. For a fresh DB we skip DELETE entirely (INSERT-only migration).
                // If the DB has rows (e.g. from a previously failed partial run that was
                // not rolled back), we must clear it first in reverse FK order.
                var isDestinationFresh = await IsDestinationFreshAsync(conn, tx);
                _logger.LogInformation(
                    "Tenant {TenantId} destination is {Mode}.",
                    tenantId, isDestinationFresh ? "FRESH (INSERT-only)" : "DIRTY (DELETE + INSERT)");

                // ── Step 2: Load all source rows from the shared database.
                foreach (var step in steps)
                    step.Rows = await step.LoadAsync();

                // ── Step 3: Clear destination operational tables (only when dirty).
                // Iterate in REVERSE order so child rows are removed before their
                // parent rows, honouring every FK constraint.
                if (!isDestinationFresh)
                {
                    _logger.LogWarning(
                        "Tenant {TenantId}: destination has existing data — clearing {Count} tables before re-populating.",
                        tenantId, steps.Count);

                    for (var i = steps.Count - 1; i >= 0; i--)
                        await ExecuteAsync(conn, tx, $"DELETE FROM [{steps[i].TableName}]");
                }

                // ── Step 4: Bulk copy (parents → children), preserving identity PKs.
                foreach (var step in steps)
                    await step.CopyAsync(dest, conn, tx);

                // ── Step 5: Validate row counts before committing.
                foreach (var step in steps)
                {
                    var dedicatedCount = await CountAsync(conn, tx, step.TableName);
                    result.Tables.Add(new TableCountComparison
                    {
                        TableName      = step.TableName,
                        SharedCount    = step.Rows!.Count,
                        DedicatedCount = dedicatedCount
                    });
                }

                if (!result.AllMatched)
                {
                    await tx.RollbackAsync();
                    var mismatched = string.Join(", ",
                        result.Tables.Where(t => !t.Match)
                                     .Select(t => $"{t.TableName} ({t.SharedCount}≠{t.DedicatedCount})"));
                    result.Success = false;
                    result.Message = $"Validation failed — row counts differ: {mismatched}. Migration rolled back.";
                    _logger.LogWarning("Data migration validation failed for tenant {TenantId}: {Detail}",
                        tenantId, mismatched);
                    return result;
                }

                await tx.CommitAsync();

                // ── Step 6: Mark migrated (only after a clean, validated commit).
                var migratedAt = DateTime.UtcNow;
                tenant.DataMigrated      = true;
                tenant.DataMigratedAtUtc = migratedAt;
                await _app.SaveChangesAsync();
                _resolver.Invalidate(tenantId);

                result.Success       = true;
                result.MigratedAtUtc = migratedAt;
                result.Message       = $"Migrated {result.TotalRowsCopied} rows across {result.Tables.Count} tables. All counts validated.";
                _logger.LogInformation("Data migration completed for tenant {TenantId}: {Rows} rows across {Tables} tables.",
                    tenantId, result.TotalRowsCopied, result.Tables.Count);
                return result;
            }
            catch (Exception ex)
            {
                try { await tx.RollbackAsync(); } catch { /* ignore secondary rollback errors */ }
                result.Success = false;
                result.Message = $"Migration failed: {ex.Message}";
                _logger.LogError(ex, "Data migration failed for tenant {TenantId}.", tenantId);
                return result;
            }
        }

        /// <summary>
        /// Returns true when the destination dedicated database contains no operational
        /// data — i.e. it was freshly provisioned and has never been migrated before.
        /// Checks two key tables (Branches and StockInHeaders); if both are empty the
        /// entire database is considered clean and the DELETE phase can be skipped.
        /// </summary>
        private static async Task<bool> IsDestinationFreshAsync(SqlConnection conn, SqlTransaction tx)
        {
            var branches   = await CountAsync(conn, tx, "Branches");
            var stockIns   = await CountAsync(conn, tx, "StockInHeaders");
            return branches == 0 && stockIns == 0;
        }

        // ──────────────────────────────────────────────────────────────────
        // Helpers
        // ──────────────────────────────────────────────────────────────────

        private static async Task<List<int>> IdsAsync(IQueryable<int> query) =>
            await query.ToListAsync();

        private static CopyStep Step<T>(string tableName, IQueryable<T> query) where T : class =>
            new CopyStep(
                tableName,
                async () => (await query.AsNoTracking().ToListAsync()).Cast<object>().ToList(),
                typeof(T));

        private static async Task ExecuteAsync(SqlConnection conn, SqlTransaction tx, string sql)
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task<int> CountAsync(SqlConnection conn, SqlTransaction tx, string table)
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"SELECT COUNT(*) FROM [{table}]";
            var scalar = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(scalar);
        }

        /// <summary>One table's copy unit: loader + bulk-copy with KeepIdentity.</summary>
        private sealed class CopyStep
        {
            private readonly Func<Task<List<object>>> _loader;
            private readonly Type _clrType;

            public string TableName { get; }
            public List<object>? Rows { get; set; }

            public CopyStep(string tableName, Func<Task<List<object>>> loader, Type clrType)
            {
                TableName = tableName;
                _loader   = loader;
                _clrType  = clrType;
            }

            public Task<List<object>> LoadAsync() => _loader();

            public async Task CopyAsync(TenantDbContext dest, SqlConnection conn, SqlTransaction tx)
            {
                var rows = Rows ?? new List<object>();
                if (rows.Count == 0) return;

                var entityType = dest.Model.FindEntityType(_clrType)
                    ?? throw new InvalidOperationException($"No mapped entity for {_clrType.Name}.");
                var store = StoreObjectIdentifier.Create(entityType, StoreObjectType.Table)
                    ?? throw new InvalidOperationException($"No table mapping for {_clrType.Name}.");

                var props = entityType.GetProperties()
                    .Where(p => !p.IsShadowProperty())
                    .ToList();

                var table = new DataTable();
                var cols = new List<(IProperty prop, string col, Type underlying, bool isEnum)>();

                foreach (var p in props)
                {
                    var col = p.GetColumnName(store);
                    if (string.IsNullOrEmpty(col)) continue;

                    var clr      = Nullable.GetUnderlyingType(p.ClrType) ?? p.ClrType;
                    var isEnum   = clr.IsEnum;
                    var dtType   = isEnum ? Enum.GetUnderlyingType(clr) : clr;
                    table.Columns.Add(col, dtType);
                    cols.Add((p, col, clr, isEnum));
                }

                foreach (var row in rows)
                {
                    var dr = table.NewRow();
                    foreach (var (prop, col, underlying, isEnum) in cols)
                    {
                        var raw = prop.PropertyInfo?.GetValue(row) ?? prop.FieldInfo?.GetValue(row);
                        dr[col] = raw == null
                            ? DBNull.Value
                            : isEnum ? Convert.ChangeType(raw, Enum.GetUnderlyingType(underlying)) : raw;
                    }
                    table.Rows.Add(dr);
                }

                using var bulk = new SqlBulkCopy(conn, SqlBulkCopyOptions.KeepIdentity, tx)
                {
                    DestinationTableName = $"[{TableName}]",
                    BulkCopyTimeout      = 600
                };
                foreach (DataColumn c in table.Columns)
                    bulk.ColumnMappings.Add(c.ColumnName, c.ColumnName);

                await bulk.WriteToServerAsync(table);
            }
        }
    }
}

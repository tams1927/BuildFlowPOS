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

            // ── Build the dependency-ordered copy plan ───────────────────────
            // Parent tables first so foreign keys resolve during bulk insert.
            var branchIds       = await IdsAsync(_app.Branches.Where(b => b.TenantId == tenantId).Select(b => b.Id));
            var customerIds     = await IdsAsync(_app.Customers.Where(c => c.TenantId == tenantId).Select(c => c.Id));
            var salesHeaderIds  = await IdsAsync(_app.SalesHeaders.Where(s => s.TenantId == tenantId).Select(s => s.Id));
            var stockInIds      = await IdsAsync(_app.StockInHeaders.Where(s => s.TenantId == tenantId).Select(s => s.Id));
            var poIds           = await IdsAsync(_app.PurchaseOrders.Where(p => p.TenantId == tenantId).Select(p => p.Id));
            var quotationIds    = await IdsAsync(_app.Quotations.Where(q => q.TenantId == tenantId).Select(q => q.Id));
            var drIds           = await IdsAsync(_app.DeliveryReceipts.Where(d => d.TenantId == tenantId).Select(d => d.Id));

            // Each step: (table name, ordered loader). Order matters (parents → children).
            var steps = new List<CopyStep>
            {
                Step<SystemSetting>("SystemSettings",        _app.SystemSettings.Where(x => x.TenantId == tenantId)),
                Step<Branch>("Branches",                     _app.Branches.Where(x => x.TenantId == tenantId)),
                Step<Category>("Categories",                 _app.Categories.Where(x => x.TenantId == tenantId)),
                Step<Unit>("Units",                          _app.Units.Where(x => x.TenantId == tenantId)),
                Step<Supplier>("Suppliers",                  _app.Suppliers.Where(x => x.TenantId == tenantId)),
                Step<Customer>("Customers",                  _app.Customers.Where(x => x.TenantId == tenantId)),
                Step<Item>("Items",                          _app.Items.Where(x => x.TenantId == tenantId)),
                Step<BranchProductStock>("BranchProductStocks", _app.BranchProductStocks.Where(x => x.TenantId == tenantId)),
                Step<UserBranch>("UserBranches",             _app.UserBranches.Where(x => branchIds.Contains(x.BranchId))),
                Step<SalesHeader>("SalesHeaders",            _app.SalesHeaders.Where(x => x.TenantId == tenantId)),
                Step<SalesDetail>("SalesDetails",            _app.SalesDetails.Where(x => salesHeaderIds.Contains(x.SalesHeaderId))),
                Step<CustomerLedger>("CustomerLedgers",      _app.CustomerLedgers.Where(x => customerIds.Contains(x.CustomerId))),
                Step<StockInHeader>("StockInHeaders",        _app.StockInHeaders.Where(x => x.TenantId == tenantId)),
                Step<StockInDetail>("StockInDetails",        _app.StockInDetails.Where(x => stockInIds.Contains(x.StockInHeaderId))),
                Step<Expense>("Expenses",                    _app.Expenses.Where(x => x.TenantId == tenantId)),
                Step<PurchaseOrder>("PurchaseOrders",        _app.PurchaseOrders.Where(x => x.TenantId == tenantId)),
                Step<PurchaseOrderItem>("PurchaseOrderItems", _app.PurchaseOrderItems.Where(x => poIds.Contains(x.PurchaseOrderId))),
                Step<Quotation>("Quotations",                _app.Quotations.Where(x => x.TenantId == tenantId)),
                Step<QuotationItem>("QuotationItems",        _app.QuotationItems.Where(x => quotationIds.Contains(x.QuotationId))),
                Step<DeliveryReceipt>("DeliveryReceipts",    _app.DeliveryReceipts.Where(x => x.TenantId == tenantId)),
                Step<DeliveryReceiptItem>("DeliveryReceiptItems", _app.DeliveryReceiptItems.Where(x => drIds.Contains(x.DeliveryReceiptId))),
                Step<Notification>("Notifications",          _app.Notifications.Where(x => x.TenantId == tenantId)),
                Step<AuditTrail>("AuditTrails",              _app.AuditTrails.Where(x => x.TenantId == tenantId)),
            };

            await using var dest = await _factory.CreateAsync(tenantId);
            await using var conn = new SqlConnection(tenant.ConnectionString);
            await conn.OpenAsync();
            await using var tx = (SqlTransaction)await conn.BeginTransactionAsync();

            try
            {
                _logger.LogInformation("Starting data migration for tenant {TenantId}.", tenantId);

                // 1) Load all source rows + clear destination operational tables (reverse order).
                foreach (var step in steps)
                    step.Rows = await step.LoadAsync();

                for (var i = steps.Count - 1; i >= 0; i--)
                    await ExecuteAsync(conn, tx, $"DELETE FROM [{steps[i].TableName}]");

                // 2) Bulk copy (parents → children), preserving identity PKs.
                foreach (var step in steps)
                    await step.CopyAsync(dest, conn, tx);

                // 3) Validate row counts before committing.
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

                // 4) Mark migrated (only after a clean, validated commit).
                var migratedAt = DateTime.UtcNow;
                tenant.DataMigrated      = true;
                tenant.DataMigratedAtUtc = migratedAt;
                await _app.SaveChangesAsync();
                _resolver.Invalidate(tenantId);

                result.Success       = true;
                result.MigratedAtUtc = migratedAt;
                result.Message       = $"Migrated {result.TotalRowsCopied} rows across {result.Tables.Count} tables. All counts validated.";
                _logger.LogInformation("Data migration completed for tenant {TenantId}: {Rows} rows.",
                    tenantId, result.TotalRowsCopied);
                return result;
            }
            catch (Exception ex)
            {
                try { await tx.RollbackAsync(); } catch { /* ignore rollback errors */ }
                result.Success = false;
                result.Message = $"Migration failed: {ex.Message}";
                _logger.LogError(ex, "Data migration failed for tenant {TenantId}.", tenantId);
                return result;
            }
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

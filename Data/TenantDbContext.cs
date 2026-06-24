using HardwareManagementSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HardwareManagementSystem.Data
{
    /// <summary>
    /// PHASE 5.0A FOUNDATION — Tenant Operational Database Context.
    ///
    /// This context represents each tenant's isolated database in the
    /// Database-Per-Tenant architecture. It owns all operational business data:
    /// products, inventory, sales, purchasing, AR/AP, branches, and tenant settings.
    ///
    /// ⚠️  NOT REGISTERED FOR RUNTIME USE IN PHASE 5.0A.
    ///     ApplicationDbContext continues to power all runtime operations.
    ///     This class exists for compilation and architecture documentation only.
    ///     Registration and activation will occur in Phase 5.0B via
    ///     ITenantDatabaseResolver and TenantDbContextFactory.
    ///
    /// Architecture Notes:
    ///     - TenantDbContext does NOT inherit IdentityDbContext.
    ///       ASP.NET Identity stays in PlatformDbContext.
    ///     - TenantId column filters on all entities become unnecessary once
    ///       each tenant has their own database (Phase 5.0E cleanup).
    ///     - FK from UserBranch.UserId to AspNetUsers.Id becomes an
    ///       application-enforced relationship (not a DB-enforced FK) because
    ///       users live in PlatformDbContext, not TenantDbContext.
    ///     - FK from all entities to Tenants table also becomes application-level
    ///       only, since Tenants lives in PlatformDbContext.
    ///
    /// Connection:
    ///     In Phase 5.0B, this context will be instantiated via TenantDbContextFactory
    ///     using a connection string resolved per request from ITenantDatabaseResolver.
    ///     Each tenant has their own SQL Server database.
    /// </summary>
    public class TenantDbContext : DbContext, ITenantOperationalDbContext
    {
        public TenantDbContext(DbContextOptions<TenantDbContext> options)
            : base(options)
        {
        }

        // ── Product / Inventory Master ────────────────────────────────────────
        public DbSet<Category> Categories { get; set; }
        public DbSet<Unit> Units { get; set; }
        public DbSet<Item> Items { get; set; }
        public DbSet<ItemUnitConversion> ItemUnitConversions { get; set; }

        // ── Supplier & Purchasing ─────────────────────────────────────────────
        public DbSet<Supplier> Suppliers { get; set; }
        public DbSet<StockInHeader> StockInHeaders { get; set; }
        public DbSet<StockInDetail> StockInDetails { get; set; }
        public DbSet<PurchaseOrder> PurchaseOrders { get; set; }
        public DbSet<PurchaseOrderItem> PurchaseOrderItems { get; set; }
        public DbSet<SupplierPayment> SupplierPayments { get; set; }

        // ── Customer & Sales ──────────────────────────────────────────────────
        public DbSet<Customer> Customers { get; set; }
        public DbSet<CustomerLedger> CustomerLedgers { get; set; }
        public DbSet<SalesHeader> SalesHeaders { get; set; }
        public DbSet<SalesDetail> SalesDetails { get; set; }
        public DbSet<SalesReturnHeader> SalesReturnHeaders { get; set; }
        public DbSet<SalesReturnDetail> SalesReturnDetails { get; set; }
        public DbSet<Quotation> Quotations { get; set; }
        public DbSet<QuotationItem> QuotationItems { get; set; }
        public DbSet<DeliveryReceipt> DeliveryReceipts { get; set; }
        public DbSet<DeliveryReceiptItem> DeliveryReceiptItems { get; set; }

        // ── Inventory Operations ──────────────────────────────────────────────
        public DbSet<StockAdjustmentHeader> StockAdjustmentHeaders { get; set; }
        public DbSet<StockAdjustmentDetail> StockAdjustmentDetails { get; set; }
        public DbSet<DamagedGoodsHeader> DamagedGoodsHeaders { get; set; }
        public DbSet<DamagedGoodsDetail> DamagedGoodsDetails { get; set; }
        public DbSet<SupplierReturnHeader> SupplierReturnHeaders { get; set; }
        public DbSet<SupplierReturnDetail> SupplierReturnDetails { get; set; }

        // ── Expenses ──────────────────────────────────────────────────────────
        public DbSet<Expense> Expenses { get; set; }

        // ── Branch Operations ─────────────────────────────────────────────────
        public DbSet<Branch> Branches { get; set; }
        public DbSet<BranchProductStock> BranchProductStocks { get; set; }
        /// <summary>
        /// UserBranch.UserId references AspNetUsers.Id (PlatformDbContext).
        /// This is an application-enforced relationship only — no DB-level FK in TenantDbContext.
        /// </summary>
        public DbSet<UserBranch> UserBranches { get; set; }
        public DbSet<BranchTransfer> BranchTransfers { get; set; }
        public DbSet<BranchTransferItem> BranchTransferItems { get; set; }

        // ── Tenant Configuration ──────────────────────────────────────────────
        /// <summary>
        /// Each tenant database has exactly one SystemSettings row.
        /// The TenantId column is retained for Phase 5.0E cleanup but is redundant
        /// in a per-tenant database (every row implicitly belongs to the tenant).
        /// </summary>
        public DbSet<SystemSetting> SystemSettings { get; set; }

        // ── Import / Data Entry ───────────────────────────────────────────────
        public DbSet<ImportBatch> ImportBatches { get; set; }
        public DbSet<ImportBatchRow> ImportBatchRows { get; set; }

        // ── TRANSITION: Tenant-scoped Notifications ───────────────────────────
        /// <summary>
        /// [TRANSITION TABLE] Tenant-scoped notifications (TenantId set) belong here.
        /// Platform broadcast notifications (TenantId = null) belong in PlatformDbContext.
        /// Design decision required before Phase 5.0C.
        /// </summary>
        public DbSet<Notification> Notifications { get; set; }

        // ── TRANSITION: Tenant Audit ───────────────────────────────────────────
        /// <summary>
        /// [TRANSITION TABLE] Tenant-scoped audit entries belong here.
        /// Platform-level audit entries (TenantId = null, SuperAdmin actions) belong in PlatformDbContext.
        /// Design decision required before Phase 5.0C.
        /// </summary>
        public DbSet<AuditTrail> AuditTrails { get; set; }

        // ── Replicated platform reference data (Phase 5.0C) ───────────────────
        /// <summary>
        /// Replicated copy of the global permission matrix. Each dedicated tenant
        /// database carries its own RolePermissions so authorization works without a
        /// cross-database lookup. Seeded at provisioning time.
        /// </summary>
        public DbSet<RolePermission> RolePermissions { get; set; }

        /// <summary>
        /// Read-only replicated copy of the SubscriptionPlans reference catalog,
        /// seeded at provisioning time for tenant-local reporting/reference.
        /// </summary>
        public DbSet<SubscriptionPlan> SubscriptionPlans { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // ── NOTE: No FK to Tenants or SubscriptionPlans ──────────────────
            // In the tenant database, there is no Tenants table.
            // All references to TenantId are application-managed constants.
            // The FK constraints that exist in ApplicationDbContext pointing to
            // the Tenants table will be dropped in Phase 5.0E.

            // ── NOTE: No FK to AspNetUsers ────────────────────────────────────
            // UserBranch.UserId is application-enforced only. Users live in PlatformDbContext.

            // ── Tenant is a PLATFORM concept ─────────────────────────────────
            // A tenant database has no Tenants table. Ignore the Tenant type and
            // every Tenant navigation so EF does not try to create a Tenants table.
            // The int? TenantId scalar column is preserved on each entity.
            builder.Ignore<Tenant>();
            builder.Entity<Category>().Ignore(e => e.Tenant);
            builder.Entity<Unit>().Ignore(e => e.Tenant);
            builder.Entity<Item>().Ignore(e => e.Tenant);
            builder.Entity<Supplier>().Ignore(e => e.Tenant);
            builder.Entity<Customer>().Ignore(e => e.Tenant);
            builder.Entity<Branch>().Ignore(e => e.Tenant);
            builder.Entity<BranchProductStock>().Ignore(e => e.Tenant);
            builder.Entity<BranchTransfer>().Ignore(e => e.Tenant);
            builder.Entity<SalesHeader>().Ignore(e => e.Tenant);
            builder.Entity<SalesReturnHeader>().Ignore(e => e.Tenant);
            builder.Entity<StockInHeader>().Ignore(e => e.Tenant);
            builder.Entity<StockAdjustmentHeader>().Ignore(e => e.Tenant);
            builder.Entity<DamagedGoodsHeader>().Ignore(e => e.Tenant);
            builder.Entity<SupplierReturnHeader>().Ignore(e => e.Tenant);
            builder.Entity<Quotation>().Ignore(e => e.Tenant);
            builder.Entity<DeliveryReceipt>().Ignore(e => e.Tenant);
            builder.Entity<PurchaseOrder>().Ignore(e => e.Tenant);
            builder.Entity<Expense>().Ignore(e => e.Tenant);
            builder.Entity<ImportBatch>().Ignore(e => e.Tenant);
            builder.Entity<SystemSetting>().Ignore(e => e.Tenant);

            // ── Replicated reference data indexes ────────────────────────────
            builder.Entity<RolePermission>(entity =>
            {
                entity.HasIndex(r => new { r.RoleName, r.ModuleName })
                      .IsUnique()
                      .HasDatabaseName("UX_RolePermissions_Role_Module");
            });

            builder.Entity<SubscriptionPlan>(entity =>
            {
                entity.Property(s => s.MonthlyPrice).HasColumnType("decimal(10,2)");
            });

            // ── Relationship delete behavior (mirrors ApplicationDbContext) ───
            // EF convention makes NON-NULLABLE FKs cascade by default. That causes
            // "multiple cascade paths" errors on SQL Server when a table has two FKs
            // to the same principal (BranchTransfer → Branch x2) or when a child is
            // reachable via two cascade chains (SalesReturnDetail). We replicate the
            // exact delete behavior used by the proven shared schema.

            // SalesReturn chain — all Restrict (breaks the dual cascade path)
            builder.Entity<SalesReturnDetail>()
                .HasOne(d => d.SalesReturnHeader).WithMany(h => h.SalesReturnDetails)
                .HasForeignKey(d => d.SalesReturnHeaderId).OnDelete(DeleteBehavior.Restrict);
            builder.Entity<SalesReturnDetail>()
                .HasOne(d => d.SalesDetail).WithMany()
                .HasForeignKey(d => d.SalesDetailId).OnDelete(DeleteBehavior.Restrict);
            builder.Entity<SalesReturnDetail>()
                .HasOne(d => d.Item).WithMany()
                .HasForeignKey(d => d.ItemId).OnDelete(DeleteBehavior.Restrict);
            builder.Entity<SalesReturnHeader>()
                .HasOne(h => h.SalesHeader).WithMany()
                .HasForeignKey(h => h.SalesHeaderId).OnDelete(DeleteBehavior.Restrict);

            // SupplierPayment
            builder.Entity<SupplierPayment>()
                .HasOne(p => p.StockInHeader).WithMany()
                .HasForeignKey(p => p.StockInHeaderId).OnDelete(DeleteBehavior.NoAction);
            builder.Entity<SupplierPayment>()
                .HasOne(p => p.Supplier).WithMany()
                .HasForeignKey(p => p.SupplierId).OnDelete(DeleteBehavior.NoAction);

            // Optional branch FKs — NoAction
            builder.Entity<StockInHeader>()
                .HasOne(s => s.Branch).WithMany()
                .HasForeignKey(s => s.BranchId).OnDelete(DeleteBehavior.NoAction).IsRequired(false);
            builder.Entity<SalesHeader>()
                .HasOne(s => s.Branch).WithMany()
                .HasForeignKey(s => s.BranchId).OnDelete(DeleteBehavior.NoAction).IsRequired(false);
            builder.Entity<Expense>()
                .HasOne(e => e.Branch).WithMany()
                .HasForeignKey(e => e.BranchId).OnDelete(DeleteBehavior.NoAction).IsRequired(false);
            builder.Entity<StockAdjustmentHeader>()
                .HasOne(a => a.Branch).WithMany()
                .HasForeignKey(a => a.BranchId).OnDelete(DeleteBehavior.NoAction).IsRequired(false);

            // BranchTransfer — DUAL branch FK: BOTH Restrict (root-cause fix)
            builder.Entity<BranchTransfer>()
                .HasOne(t => t.FromBranch).WithMany()
                .HasForeignKey(t => t.FromBranchId).OnDelete(DeleteBehavior.Restrict);
            builder.Entity<BranchTransfer>()
                .HasOne(t => t.ToBranch).WithMany()
                .HasForeignKey(t => t.ToBranchId).OnDelete(DeleteBehavior.Restrict);
            builder.Entity<BranchTransferItem>()
                .HasOne(ti => ti.BranchTransfer).WithMany(t => t.BranchTransferItems)
                .HasForeignKey(ti => ti.BranchTransferId).OnDelete(DeleteBehavior.Cascade);
            builder.Entity<BranchTransferItem>()
                .HasOne(ti => ti.Product).WithMany()
                .HasForeignKey(ti => ti.ProductId).OnDelete(DeleteBehavior.Restrict);

            // Quotation
            builder.Entity<Quotation>()
                .HasOne(q => q.Branch).WithMany()
                .HasForeignKey(q => q.BranchId).OnDelete(DeleteBehavior.NoAction).IsRequired(false);
            builder.Entity<Quotation>()
                .HasOne(q => q.Customer).WithMany()
                .HasForeignKey(q => q.CustomerId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
            builder.Entity<Quotation>()
                .HasOne(q => q.ConvertedToSale).WithMany()
                .HasForeignKey(q => q.ConvertedToSaleId).OnDelete(DeleteBehavior.NoAction).IsRequired(false);
            builder.Entity<QuotationItem>()
                .HasOne(qi => qi.Quotation).WithMany(q => q.Items)
                .HasForeignKey(qi => qi.QuotationId).OnDelete(DeleteBehavior.Cascade);
            builder.Entity<QuotationItem>()
                .HasOne(qi => qi.Item).WithMany()
                .HasForeignKey(qi => qi.ItemId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);

            // DeliveryReceipt
            builder.Entity<DeliveryReceipt>()
                .HasOne(dr => dr.Customer).WithMany()
                .HasForeignKey(dr => dr.CustomerId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
            builder.Entity<DeliveryReceipt>()
                .HasOne(dr => dr.SalesHeader).WithMany()
                .HasForeignKey(dr => dr.SalesHeaderId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
            builder.Entity<DeliveryReceipt>()
                .HasOne(dr => dr.Branch).WithMany()
                .HasForeignKey(dr => dr.BranchId).OnDelete(DeleteBehavior.NoAction).IsRequired(false);
            builder.Entity<DeliveryReceiptItem>()
                .HasOne(dri => dri.DeliveryReceipt).WithMany(dr => dr.Items)
                .HasForeignKey(dri => dri.DeliveryReceiptId).OnDelete(DeleteBehavior.Cascade);
            builder.Entity<DeliveryReceiptItem>()
                .HasOne(dri => dri.Item).WithMany()
                .HasForeignKey(dri => dri.ItemId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);

            // Detail → Item: Restrict
            builder.Entity<SalesDetail>()
                .HasOne(d => d.Item).WithMany()
                .HasForeignKey(d => d.ItemId).OnDelete(DeleteBehavior.Restrict);
            builder.Entity<StockInDetail>()
                .HasOne(d => d.Item).WithMany()
                .HasForeignKey(d => d.ItemId).OnDelete(DeleteBehavior.Restrict);
            builder.Entity<StockAdjustmentDetail>()
                .HasOne(d => d.Item).WithMany()
                .HasForeignKey(d => d.ItemId).OnDelete(DeleteBehavior.Restrict);

            // CustomerLedger → Customer
            builder.Entity<CustomerLedger>()
                .HasOne(l => l.Customer).WithMany()
                .HasForeignKey(l => l.CustomerId).OnDelete(DeleteBehavior.Restrict);

            // PurchaseOrder
            builder.Entity<PurchaseOrder>()
                .HasOne(po => po.Branch).WithMany()
                .HasForeignKey(po => po.BranchId).OnDelete(DeleteBehavior.NoAction).IsRequired(false);
            builder.Entity<PurchaseOrder>()
                .HasOne(po => po.Supplier).WithMany()
                .HasForeignKey(po => po.SupplierId).OnDelete(DeleteBehavior.Restrict);
            builder.Entity<PurchaseOrderItem>()
                .HasOne(pi => pi.PurchaseOrder).WithMany(po => po.Items)
                .HasForeignKey(pi => pi.PurchaseOrderId).OnDelete(DeleteBehavior.Cascade);
            builder.Entity<PurchaseOrderItem>()
                .HasOne(pi => pi.Item).WithMany()
                .HasForeignKey(pi => pi.ItemId).OnDelete(DeleteBehavior.Restrict);

            // UserBranch → Branch (single path, cascade is safe)
            builder.Entity<UserBranch>()
                .HasOne(ub => ub.Branch).WithMany()
                .HasForeignKey(ub => ub.BranchId).OnDelete(DeleteBehavior.Cascade);

            // ── Categories ───────────────────────────────────────────────────
            builder.Entity<Category>(entity =>
            {
                entity.HasIndex(c => c.TenantId).HasDatabaseName("IX_Categories_TenantId");
                entity.HasIndex(c => new { c.TenantId, c.CategoryName })
                      .IsUnique()
                      .HasDatabaseName("UX_Categories_TenantId_Name");
            });

            // ── Units ────────────────────────────────────────────────────────
            builder.Entity<Unit>(entity =>
            {
                entity.HasIndex(u => u.TenantId).HasDatabaseName("IX_Units_TenantId");
            });

            // ── Items ─────────────────────────────────────────────────────────
            builder.Entity<Item>(entity =>
            {
                entity.HasIndex(i => i.TenantId).HasDatabaseName("IX_Items_TenantId");
                entity.HasOne(i => i.Category).WithMany().HasForeignKey(i => i.CategoryId)
                      .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(i => i.Unit).WithMany().HasForeignKey(i => i.UnitId)
                      .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(i => i.BaseUnit).WithMany().HasForeignKey(i => i.BaseUnitId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<ItemUnitConversion>(entity =>
            {
                entity.HasIndex(c => c.TenantId).HasDatabaseName("IX_ItemUnitConversions_TenantId");
                entity.HasIndex(c => new { c.ItemId, c.UnitId })
                      .IsUnique()
                      .HasDatabaseName("UX_ItemUnitConversions_ItemId_UnitId");
                entity.HasOne(c => c.Item).WithMany(i => i.UnitConversions)
                      .HasForeignKey(c => c.ItemId)
                      .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(c => c.Unit).WithMany()
                      .HasForeignKey(c => c.UnitId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<StockInDetail>()
                .HasOne(d => d.ReceivedUnit).WithMany()
                .HasForeignKey(d => d.ReceivedUnitId)
                .OnDelete(DeleteBehavior.NoAction).IsRequired(false);

            builder.Entity<PurchaseOrderItem>()
                .HasOne(pi => pi.OrderedUnit).WithMany()
                .HasForeignKey(pi => pi.OrderedUnitId)
                .OnDelete(DeleteBehavior.NoAction).IsRequired(false);

            // ── Suppliers ─────────────────────────────────────────────────────
            builder.Entity<Supplier>(entity =>
            {
                entity.HasIndex(s => s.TenantId).HasDatabaseName("IX_Suppliers_TenantId");
            });

            // ── Customers ─────────────────────────────────────────────────────
            builder.Entity<Customer>(entity =>
            {
                entity.HasIndex(c => c.TenantId).HasDatabaseName("IX_Customers_TenantId");
            });

            // ── SalesHeaders ──────────────────────────────────────────────────
            builder.Entity<SalesHeader>(entity =>
            {
                entity.HasIndex(s => s.TenantId).HasDatabaseName("IX_SalesHeaders_TenantId");
                entity.HasIndex(s => new { s.TenantId, s.SalesNumber })
                      .IsUnique()
                      .HasDatabaseName("UX_SalesHeaders_TenantId_SalesNumber");
            });

            // ── StockInHeaders ────────────────────────────────────────────────
            builder.Entity<StockInHeader>(entity =>
            {
                entity.HasIndex(s => s.TenantId).HasDatabaseName("IX_StockInHeaders_TenantId");
            });

            // ── Branches ──────────────────────────────────────────────────────
            builder.Entity<Branch>(entity =>
            {
                entity.HasIndex(b => b.TenantId).HasDatabaseName("IX_Branches_TenantId");
                entity.HasIndex(b => new { b.TenantId, b.Code })
                      .IsUnique()
                      .HasDatabaseName("UX_Branches_TenantId_Code");
            });

            // ── BranchProductStock ────────────────────────────────────────────
            builder.Entity<BranchProductStock>(entity =>
            {
                entity.HasIndex(b => b.TenantId).HasDatabaseName("IX_BranchProductStocks_TenantId");
                entity.HasIndex(b => new { b.BranchId, b.ProductId })
                      .IsUnique()
                      .HasDatabaseName("UX_BranchProductStocks_BranchId_ProductId");
                entity.HasOne(b => b.Branch).WithMany().HasForeignKey(b => b.BranchId)
                      .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(b => b.Product).WithMany().HasForeignKey(b => b.ProductId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // ── BranchTransfers ───────────────────────────────────────────────
            builder.Entity<BranchTransfer>(entity =>
            {
                entity.HasIndex(bt => bt.TenantId).HasDatabaseName("IX_BranchTransfers_TenantId");
            });

            // ── Quotations ────────────────────────────────────────────────────
            builder.Entity<Quotation>(entity =>
            {
                entity.HasIndex(q => q.TenantId).HasDatabaseName("IX_Quotations_TenantId");
            });

            // ── DeliveryReceipts ──────────────────────────────────────────────
            builder.Entity<DeliveryReceipt>(entity =>
            {
                entity.HasIndex(d => d.TenantId).HasDatabaseName("IX_DeliveryReceipts_TenantId");
            });

            // ── PurchaseOrders ────────────────────────────────────────────────
            builder.Entity<PurchaseOrder>(entity =>
            {
                entity.HasIndex(p => p.TenantId).HasDatabaseName("IX_PurchaseOrders_TenantId");
            });

            // ── Expenses ──────────────────────────────────────────────────────
            builder.Entity<Expense>(entity =>
            {
                entity.HasIndex(e => e.TenantId).HasDatabaseName("IX_Expenses_TenantId");
            });

            // ── StockAdjustmentHeaders ────────────────────────────────────────
            builder.Entity<StockAdjustmentHeader>(entity =>
            {
                entity.HasIndex(s => s.TenantId).HasDatabaseName("IX_StockAdjustmentHeaders_TenantId");
            });

            // ── Phase 5.3 Damaged Goods / Supplier Returns ────────────────────
            builder.Entity<DamagedGoodsHeader>(entity =>
            {
                entity.HasIndex(d => d.TenantId).HasDatabaseName("IX_DamagedGoodsHeaders_TenantId");
                entity.HasIndex(d => new { d.TenantId, d.DamageNumber })
                    .IsUnique()
                    .HasFilter("[TenantId] IS NOT NULL")
                    .HasDatabaseName("UX_DamagedGoodsHeaders_TenantId_DamageNumber");
            });

            builder.Entity<DamagedGoodsDetail>()
                .HasOne(d => d.DamagedGoodsHeader)
                .WithMany(h => h.Details)
                .HasForeignKey(d => d.DamagedGoodsHeaderId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<DamagedGoodsDetail>()
                .HasOne(d => d.Item)
                .WithMany()
                .HasForeignKey(d => d.ItemId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<SupplierReturnHeader>(entity =>
            {
                entity.HasIndex(s => s.TenantId).HasDatabaseName("IX_SupplierReturnHeaders_TenantId");
                entity.HasIndex(s => new { s.TenantId, s.ReturnNumber })
                    .IsUnique()
                    .HasFilter("[TenantId] IS NOT NULL")
                    .HasDatabaseName("UX_SupplierReturnHeaders_TenantId_ReturnNumber");
            });

            builder.Entity<SupplierReturnDetail>()
                .HasOne(d => d.SupplierReturnHeader)
                .WithMany(h => h.Details)
                .HasForeignKey(d => d.SupplierReturnHeaderId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<SupplierReturnDetail>()
                .HasOne(d => d.Item)
                .WithMany()
                .HasForeignKey(d => d.ItemId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<SupplierReturnHeader>()
                .HasOne(h => h.LinkedDamagedGoods)
                .WithMany()
                .HasForeignKey(h => h.LinkedDamagedGoodsId)
                .OnDelete(DeleteBehavior.NoAction)
                .IsRequired(false);

            // ── SalesReturnHeaders ────────────────────────────────────────────
            builder.Entity<SalesReturnHeader>(entity =>
            {
                entity.HasIndex(s => s.TenantId).HasDatabaseName("IX_SalesReturnHeaders_TenantId");
            });

            // ── ImportBatches ─────────────────────────────────────────────────
            builder.Entity<ImportBatch>(entity =>
            {
                entity.HasIndex(i => i.TenantId).HasDatabaseName("IX_ImportBatches_TenantId");
            });

            // ── Notifications [TRANSITION] ────────────────────────────────────
            builder.Entity<Notification>(entity =>
            {
                entity.HasIndex(n => n.TenantId).HasDatabaseName("IX_Notifications_TenantId");
            });

            // ── AuditTrails [TRANSITION] ──────────────────────────────────────
            builder.Entity<AuditTrail>(entity =>
            {
                entity.HasIndex(a => a.TenantId).HasDatabaseName("IX_AuditTrails_TenantId");
                entity.HasIndex(a => new { a.TenantId, a.CreatedAt })
                      .HasDatabaseName("IX_AuditTrails_TenantId_CreatedAt");
            });

            // ── SystemSettings ────────────────────────────────────────────────
            builder.Entity<SystemSetting>(entity =>
            {
                // No Tenant FK in TenantDbContext — the database IS the tenant scope.
            });
        }
    }
}

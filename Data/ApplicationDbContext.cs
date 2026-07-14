using HardwareManagementSystem.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace HardwareManagementSystem.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>, ITenantOperationalDbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<Category> Categories { get; set; }

        public DbSet<Unit> Units { get; set; }

        public DbSet<Supplier> Suppliers { get; set; }

        public DbSet<Customer> Customers { get; set; }

        public DbSet<Item> Items { get; set; }

        public DbSet<ItemUnitConversion> ItemUnitConversions { get; set; }

        public DbSet<StockInHeader> StockInHeaders { get; set; }

        public DbSet<StockInDetail> StockInDetails { get; set; }

        public DbSet<SalesHeader> SalesHeaders { get; set; }

        public DbSet<SalesDetail> SalesDetails { get; set; }

        public DbSet<RolePermission> RolePermissions { get; set; }

        public DbSet<SystemSetting> SystemSettings { get; set; }

        public DbSet<AuditTrail> AuditTrails { get; set; }

        public DbSet<StockAdjustmentHeader> StockAdjustmentHeaders { get; set; }

        public DbSet<StockAdjustmentDetail> StockAdjustmentDetails { get; set; }

        public DbSet<SalesReturnHeader> SalesReturnHeaders { get; set; }

        public DbSet<SalesReturnDetail> SalesReturnDetails { get; set; }

        public DbSet<Notification> Notifications { get; set; }

        public DbSet<CustomerLedger> CustomerLedgers { get; set; }
        public DbSet<SupplierPayment> SupplierPayments { get; set; }
        public DbSet<Expense> Expenses { get; set; }

        public DbSet<ImportBatch> ImportBatches { get; set; }

        public DbSet<ImportBatchRow> ImportBatchRows { get; set; }

        public DbSet<Branch> Branches { get; set; }

        public DbSet<BranchProductStock> BranchProductStocks { get; set; }

        public DbSet<UserBranch> UserBranches { get; set; }

        public DbSet<BranchTransfer> BranchTransfers { get; set; }

        public DbSet<BranchTransferItem> BranchTransferItems { get; set; }

        public DbSet<Tenant> Tenants { get; set; }

        public DbSet<SubscriptionPlan> SubscriptionPlans { get; set; }

        public DbSet<Quotation> Quotations { get; set; }

        public DbSet<QuotationItem> QuotationItems { get; set; }

        public DbSet<DeliveryReceipt> DeliveryReceipts { get; set; }

        public DbSet<DeliveryReceiptItem> DeliveryReceiptItems { get; set; }

        public DbSet<PurchaseOrder> PurchaseOrders { get; set; }

        public DbSet<PurchaseOrderItem> PurchaseOrderItems { get; set; }

        public DbSet<BackupRecord> BackupRecords { get; set; }

        public DbSet<RestoreRecord> RestoreRecords { get; set; }

        public DbSet<DamagedGoodsHeader> DamagedGoodsHeaders { get; set; }

        public DbSet<DamagedGoodsDetail> DamagedGoodsDetails { get; set; }

        public DbSet<SupplierReturnHeader> SupplierReturnHeaders { get; set; }

        public DbSet<SupplierReturnDetail> SupplierReturnDetails { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<SalesReturnDetail>()
                .HasOne(d => d.SalesReturnHeader)
                .WithMany(h => h.SalesReturnDetails)
                .HasForeignKey(d => d.SalesReturnHeaderId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<SalesReturnDetail>()
                .HasOne(d => d.SalesDetail)
                .WithMany()
                .HasForeignKey(d => d.SalesDetailId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<SalesReturnDetail>()
                .HasOne(d => d.Item)
                .WithMany()
                .HasForeignKey(d => d.ItemId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<SalesReturnHeader>()
                .HasOne(h => h.SalesHeader)
                .WithMany()
                .HasForeignKey(h => h.SalesHeaderId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<SupplierPayment>()
                .HasOne(p => p.StockInHeader)
                .WithMany()
                .HasForeignKey(p => p.StockInHeaderId)
                .OnDelete(DeleteBehavior.NoAction);

            builder.Entity<SupplierPayment>()
                .HasOne(p => p.Supplier)
                .WithMany()
                .HasForeignKey(p => p.SupplierId)
                .OnDelete(DeleteBehavior.NoAction);

            // Branch.Code: global-unique index replaced by per-tenant composite below (H-12)

            // ============================================
            // BRANCH PRODUCT STOCK — unique per branch+product
            // ============================================

            builder.Entity<BranchProductStock>()
                .HasIndex(s => new { s.BranchId, s.ProductId })
                .IsUnique();

            builder.Entity<BranchProductStock>()
                .HasOne(s => s.Branch)
                .WithMany()
                .HasForeignKey(s => s.BranchId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<BranchProductStock>()
                .HasOne(s => s.Product)
                .WithMany()
                .HasForeignKey(s => s.ProductId)
                .OnDelete(DeleteBehavior.Cascade);

            // ============================================
            // USER BRANCHES — no cascade on delete
            // ============================================

            builder.Entity<UserBranch>()
                .HasOne(ub => ub.Branch)
                .WithMany()
                .HasForeignKey(ub => ub.BranchId)
                .OnDelete(DeleteBehavior.Cascade);

            // ============================================
            // STOCK-IN / SALES / EXPENSE — optional branch FK
            // ============================================

            builder.Entity<StockInHeader>()
                .HasOne(s => s.Branch)
                .WithMany()
                .HasForeignKey(s => s.BranchId)
                .OnDelete(DeleteBehavior.NoAction)
                .IsRequired(false);

            builder.Entity<StockInHeader>()
                .HasOne(s => s.PurchaseOrder)
                .WithMany()
                .HasForeignKey(s => s.PurchaseOrderId)
                .OnDelete(DeleteBehavior.NoAction)
                .IsRequired(false);

            builder.Entity<SalesHeader>()
                .HasOne(s => s.Branch)
                .WithMany()
                .HasForeignKey(s => s.BranchId)
                .OnDelete(DeleteBehavior.NoAction)
                .IsRequired(false);

            builder.Entity<Expense>()
                .HasOne(e => e.Branch)
                .WithMany()
                .HasForeignKey(e => e.BranchId)
                .OnDelete(DeleteBehavior.NoAction)
                .IsRequired(false);

            // ============================================
            // STOCK ADJUSTMENT — optional branch FK
            // ============================================

            builder.Entity<StockAdjustmentHeader>()
                .HasOne(a => a.Branch)
                .WithMany()
                .HasForeignKey(a => a.BranchId)
                .OnDelete(DeleteBehavior.NoAction)
                .IsRequired(false);

            // ============================================
            // BRANCH TRANSFER
            // ============================================

            builder.Entity<BranchTransfer>()
                .HasOne(t => t.FromBranch)
                .WithMany()
                .HasForeignKey(t => t.FromBranchId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<BranchTransfer>()
                .HasOne(t => t.ToBranch)
                .WithMany()
                .HasForeignKey(t => t.ToBranchId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<BranchTransferItem>()
                .HasOne(ti => ti.BranchTransfer)
                .WithMany(t => t.BranchTransferItems)
                .HasForeignKey(ti => ti.BranchTransferId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<BranchTransferItem>()
                .HasOne(ti => ti.Product)
                .WithMany()
                .HasForeignKey(ti => ti.ProductId)
                .OnDelete(DeleteBehavior.Restrict);

            // ============================================
            // TENANT — unique index on Code
            // ============================================

            builder.Entity<Tenant>()
                .HasIndex(t => t.Code)
                .IsUnique();

            // ============================================
            // TENANT FK — ApplicationUser
            // ============================================

            builder.Entity<ApplicationUser>()
                .HasOne(u => u.Tenant)
                .WithMany()
                .HasForeignKey(u => u.TenantId)
                .OnDelete(DeleteBehavior.NoAction)
                .IsRequired(false);

            // ============================================
            // TENANT FK — tenant-owned tables (all nullable)
            // ============================================

            builder.Entity<Branch>()
                .HasOne(b => b.Tenant)
                .WithMany()
                .HasForeignKey(b => b.TenantId)
                .OnDelete(DeleteBehavior.NoAction)
                .IsRequired(false);

            builder.Entity<Item>()
                .HasOne(i => i.Tenant)
                .WithMany()
                .HasForeignKey(i => i.TenantId)
                .OnDelete(DeleteBehavior.NoAction)
                .IsRequired(false);

            builder.Entity<Category>()
                .HasOne(c => c.Tenant)
                .WithMany()
                .HasForeignKey(c => c.TenantId)
                .OnDelete(DeleteBehavior.NoAction)
                .IsRequired(false);

            builder.Entity<Unit>()
                .HasOne(u => u.Tenant)
                .WithMany()
                .HasForeignKey(u => u.TenantId)
                .OnDelete(DeleteBehavior.NoAction)
                .IsRequired(false);

            builder.Entity<Supplier>()
                .HasOne(s => s.Tenant)
                .WithMany()
                .HasForeignKey(s => s.TenantId)
                .OnDelete(DeleteBehavior.NoAction)
                .IsRequired(false);

            builder.Entity<Customer>()
                .HasOne(c => c.Tenant)
                .WithMany()
                .HasForeignKey(c => c.TenantId)
                .OnDelete(DeleteBehavior.NoAction)
                .IsRequired(false);

            builder.Entity<SalesHeader>()
                .HasOne(s => s.Tenant)
                .WithMany()
                .HasForeignKey(s => s.TenantId)
                .OnDelete(DeleteBehavior.NoAction)
                .IsRequired(false);

            builder.Entity<StockInHeader>()
                .HasOne(s => s.Tenant)
                .WithMany()
                .HasForeignKey(s => s.TenantId)
                .OnDelete(DeleteBehavior.NoAction)
                .IsRequired(false);

            builder.Entity<Expense>()
                .HasOne(e => e.Tenant)
                .WithMany()
                .HasForeignKey(e => e.TenantId)
                .OnDelete(DeleteBehavior.NoAction)
                .IsRequired(false);

            builder.Entity<StockAdjustmentHeader>()
                .HasOne(a => a.Tenant)
                .WithMany()
                .HasForeignKey(a => a.TenantId)
                .OnDelete(DeleteBehavior.NoAction)
                .IsRequired(false);

            builder.Entity<BranchProductStock>()
                .HasOne(s => s.Tenant)
                .WithMany()
                .HasForeignKey(s => s.TenantId)
                .OnDelete(DeleteBehavior.NoAction)
                .IsRequired(false);

            builder.Entity<BranchTransfer>()
                .HasOne(t => t.Tenant)
                .WithMany()
                .HasForeignKey(t => t.TenantId)
                .OnDelete(DeleteBehavior.NoAction)
                .IsRequired(false);

            builder.Entity<ImportBatch>()
                .HasOne(b => b.Tenant)
                .WithMany()
                .HasForeignKey(b => b.TenantId)
                .OnDelete(DeleteBehavior.NoAction)
                .IsRequired(false);

            // ============================================
            // TENANT INDEXES on tenant-owned tables
            // ============================================

            builder.Entity<Branch>().HasIndex(b => b.TenantId);
            builder.Entity<Item>().HasIndex(i => i.TenantId);
            builder.Entity<Category>().HasIndex(c => c.TenantId);
            builder.Entity<Unit>().HasIndex(u => u.TenantId);
            builder.Entity<Supplier>().HasIndex(s => s.TenantId);
            builder.Entity<Customer>().HasIndex(c => c.TenantId);
            builder.Entity<SalesHeader>().HasIndex(s => s.TenantId);
            builder.Entity<StockInHeader>().HasIndex(s => s.TenantId);
            builder.Entity<Expense>().HasIndex(e => e.TenantId);
            builder.Entity<StockAdjustmentHeader>().HasIndex(a => a.TenantId);
            builder.Entity<BranchProductStock>().HasIndex(s => s.TenantId);
            builder.Entity<BranchTransfer>().HasIndex(t => t.TenantId);
            builder.Entity<ImportBatch>().HasIndex(b => b.TenantId);
            builder.Entity<AuditTrail>().HasIndex(a => a.TenantId);

            // Phase 4.7 — missing TenantId indexes identified in pilot-readiness audit
            builder.Entity<SalesReturnHeader>().HasIndex(s => s.TenantId)
                .HasDatabaseName("IX_SalesReturnHeaders_TenantId");
            builder.Entity<Notification>().HasIndex(n => n.TenantId)
                .HasDatabaseName("IX_Notifications_TenantId");

            // ============================================
            // COMPOSITE PERFORMANCE INDEXES
            // ============================================

            builder.Entity<SalesHeader>()
                .HasIndex(s => new { s.TenantId, s.BranchId, s.SalesDate, s.Status })
                .HasDatabaseName("IX_SalesHeaders_TenantId_BranchId_SalesDate_Status");

            builder.Entity<Expense>()
                .HasIndex(e => new { e.TenantId, e.BranchId, e.ExpenseDate })
                .HasDatabaseName("IX_Expenses_TenantId_BranchId_ExpenseDate");

            builder.Entity<StockInHeader>()
                .HasIndex(s => new { s.TenantId, s.DateReceived, s.PaymentStatus })
                .HasDatabaseName("IX_StockInHeaders_TenantId_DateReceived_PaymentStatus");

            builder.Entity<BranchProductStock>()
                .HasIndex(s => new { s.BranchId, s.TenantId, s.Quantity })
                .HasDatabaseName("IX_BranchProductStocks_BranchId_TenantId_Quantity");

            builder.Entity<AuditTrail>()
                .HasIndex(a => new { a.TenantId, a.CreatedAt })
                .HasDatabaseName("IX_AuditTrails_TenantId_CreatedAt");

            builder.Entity<ImportBatch>()
                .HasIndex(b => new { b.TenantId, b.CreatedAtUtc })
                .HasDatabaseName("IX_ImportBatches_TenantId_CreatedAtUtc");

            builder.Entity<BranchTransfer>()
                .HasIndex(t => new { t.TenantId, t.Status, t.CreatedAtUtc })
                .HasDatabaseName("IX_BranchTransfers_TenantId_Status_CreatedAtUtc");

            builder.Entity<CustomerLedger>()
                .HasIndex(l => new { l.CustomerId, l.Id })
                .HasDatabaseName("IX_CustomerLedgers_CustomerId_Id");

            builder.Entity<SupplierPayment>()
                .HasIndex(p => new { p.SupplierId, p.PaymentDate })
                .HasDatabaseName("IX_SupplierPayments_SupplierId_PaymentDate");

            // ── SystemSetting → Tenant ────────────────────────────
            builder.Entity<SystemSetting>()
                .HasOne(s => s.Tenant)
                .WithMany()
                .HasForeignKey(s => s.TenantId)
                .OnDelete(DeleteBehavior.NoAction)
                .IsRequired(false);

            builder.Entity<SystemSetting>()
                .HasIndex(s => s.TenantId)
                .HasDatabaseName("IX_SystemSettings_TenantId");

            // ── Tenant → SubscriptionPlan ─────────────────────────
            builder.Entity<Tenant>()
                .HasOne(t => t.SubscriptionPlan)
                .WithMany()
                .HasForeignKey(t => t.SubscriptionPlanId)
                .OnDelete(DeleteBehavior.SetNull)
                .IsRequired(false);

            // ── Quotation FKs ─────────────────────────────────────
            builder.Entity<Quotation>()
                .HasOne(q => q.Tenant)
                .WithMany()
                .HasForeignKey(q => q.TenantId)
                .OnDelete(DeleteBehavior.NoAction)
                .IsRequired(false);

            builder.Entity<Quotation>()
                .HasOne(q => q.Branch)
                .WithMany()
                .HasForeignKey(q => q.BranchId)
                .OnDelete(DeleteBehavior.NoAction)
                .IsRequired(false);

            builder.Entity<Quotation>()
                .HasOne(q => q.Customer)
                .WithMany()
                .HasForeignKey(q => q.CustomerId)
                .OnDelete(DeleteBehavior.SetNull)
                .IsRequired(false);

            builder.Entity<QuotationItem>()
                .HasOne(qi => qi.Quotation)
                .WithMany(q => q.Items)
                .HasForeignKey(qi => qi.QuotationId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<QuotationItem>()
                .HasOne(qi => qi.Item)
                .WithMany()
                .HasForeignKey(qi => qi.ItemId)
                .OnDelete(DeleteBehavior.SetNull)
                .IsRequired(false);

            builder.Entity<Quotation>().HasIndex(q => new { q.TenantId, q.BranchId, q.QuotationDate });
            builder.Entity<Quotation>().HasIndex(q => q.QuotationNo);

            // ── Quotation → ConvertedToSale (no cascade) ─────────────
            builder.Entity<Quotation>()
                .HasOne(q => q.ConvertedToSale)
                .WithMany()
                .HasForeignKey(q => q.ConvertedToSaleId)
                .OnDelete(DeleteBehavior.NoAction)
                .IsRequired(false);

            // ── DeliveryReceipt FKs ────────────────────────────────────
            builder.Entity<DeliveryReceipt>()
                .HasOne(dr => dr.Customer)
                .WithMany()
                .HasForeignKey(dr => dr.CustomerId)
                .OnDelete(DeleteBehavior.SetNull)
                .IsRequired(false);

            builder.Entity<DeliveryReceipt>()
                .HasOne(dr => dr.SalesHeader)
                .WithMany()
                .HasForeignKey(dr => dr.SalesHeaderId)
                .OnDelete(DeleteBehavior.SetNull)
                .IsRequired(false);

            builder.Entity<DeliveryReceipt>()
                .HasOne(dr => dr.Tenant)
                .WithMany()
                .HasForeignKey(dr => dr.TenantId)
                .OnDelete(DeleteBehavior.NoAction)
                .IsRequired(false);

            builder.Entity<DeliveryReceipt>()
                .HasOne(dr => dr.Branch)
                .WithMany()
                .HasForeignKey(dr => dr.BranchId)
                .OnDelete(DeleteBehavior.NoAction)
                .IsRequired(false);

            builder.Entity<DeliveryReceiptItem>()
                .HasOne(dri => dri.DeliveryReceipt)
                .WithMany(dr => dr.Items)
                .HasForeignKey(dri => dri.DeliveryReceiptId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<DeliveryReceiptItem>()
                .HasOne(dri => dri.Item)
                .WithMany()
                .HasForeignKey(dri => dri.ItemId)
                .OnDelete(DeleteBehavior.SetNull)
                .IsRequired(false);

            builder.Entity<DeliveryReceipt>()
                .HasIndex(dr => new { dr.TenantId, dr.BranchId, dr.DeliveryDate });
            builder.Entity<DeliveryReceipt>()
                .HasIndex(dr => dr.DRNumber);

            // ── SalesReturnHeader → Tenant FK (H-11) ─────────────────────
            builder.Entity<SalesReturnHeader>()
                .HasOne(h => h.Tenant)
                .WithMany()
                .HasForeignKey(h => h.TenantId)
                .OnDelete(DeleteBehavior.NoAction)
                .IsRequired(false);

            builder.Entity<SalesReturnHeader>()
                .HasIndex(h => h.TenantId)
                .HasDatabaseName("IX_SalesReturnHeaders_TenantId");

            // ─────────────────────────────────────────────────────────────
            // H-13 — Restrict dangerous cascade deletes
            // ─────────────────────────────────────────────────────────────

            // SalesDetail → Item: prevent deleting an item that has sales lines
            builder.Entity<SalesDetail>()
                .HasOne(d => d.Item)
                .WithMany()
                .HasForeignKey(d => d.ItemId)
                .OnDelete(DeleteBehavior.Restrict);

            // StockInDetail → Item
            builder.Entity<StockInDetail>()
                .HasOne(d => d.Item)
                .WithMany()
                .HasForeignKey(d => d.ItemId)
                .OnDelete(DeleteBehavior.Restrict);

            // StockAdjustmentDetail → Item
            builder.Entity<StockAdjustmentDetail>()
                .HasOne(d => d.Item)
                .WithMany()
                .HasForeignKey(d => d.ItemId)
                .OnDelete(DeleteBehavior.Restrict);

            // Item → Category and Unit: prevent deleting a category/unit that has items
            builder.Entity<Item>()
                .HasOne(i => i.Category)
                .WithMany()
                .HasForeignKey(i => i.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Item>()
                .HasOne(i => i.Unit)
                .WithMany()
                .HasForeignKey(i => i.UnitId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Item>()
                .HasOne(i => i.BaseUnit)
                .WithMany()
                .HasForeignKey(i => i.BaseUnitId)
                .OnDelete(DeleteBehavior.Restrict);

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
                entity.Property(c => c.ConversionQuantity).HasColumnType("decimal(18,6)");
            });

            builder.Entity<StockInDetail>()
                .HasOne(d => d.ReceivedUnit)
                .WithMany()
                .HasForeignKey(d => d.ReceivedUnitId)
                .OnDelete(DeleteBehavior.NoAction)
                .IsRequired(false);

            builder.Entity<PurchaseOrderItem>()
                .HasOne(pi => pi.OrderedUnit)
                .WithMany()
                .HasForeignKey(pi => pi.OrderedUnitId)
                .OnDelete(DeleteBehavior.NoAction)
                .IsRequired(false);

            // CustomerLedger → Customer: prevent deleting a customer with ledger rows
            builder.Entity<CustomerLedger>()
                .HasOne(l => l.Customer)
                .WithMany()
                .HasForeignKey(l => l.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);

            // ─────────────────────────────────────────────────────────────
            // H-12 — Unique indexes on all document numbers (tenant-scoped)
            // ─────────────────────────────────────────────────────────────

            // Branch.Code: change from global-unique to per-tenant unique (H-12)
            builder.Entity<Branch>()
                .HasIndex(b => new { b.TenantId, b.Code })
                .IsUnique()
                .HasDatabaseName("UX_Branches_TenantId_Code");

            // SalesNumber per tenant
            builder.Entity<SalesHeader>()
                .HasIndex(s => new { s.TenantId, s.SalesNumber })
                .IsUnique()
                .HasDatabaseName("UX_SalesHeaders_TenantId_SalesNumber");

            // StockInNumber per tenant
            builder.Entity<StockInHeader>()
                .HasIndex(s => new { s.TenantId, s.StockInNumber })
                .IsUnique()
                .HasDatabaseName("UX_StockInHeaders_TenantId_StockInNumber");

            // ExpenseNumber per tenant
            builder.Entity<Expense>()
                .HasIndex(e => new { e.TenantId, e.ExpenseNumber })
                .IsUnique()
                .HasDatabaseName("UX_Expenses_TenantId_ExpenseNumber");

            // AdjustmentNumber per tenant
            builder.Entity<StockAdjustmentHeader>()
                .HasIndex(a => new { a.TenantId, a.AdjustmentNumber })
                .IsUnique()
                .HasDatabaseName("UX_StockAdjustmentHeaders_TenantId_AdjustmentNumber");

            // TransferNumber per tenant
            builder.Entity<BranchTransfer>()
                .HasIndex(t => new { t.TenantId, t.TransferNumber })
                .IsUnique()
                .HasDatabaseName("UX_BranchTransfers_TenantId_TransferNumber");

            // ReturnNumber per tenant
            builder.Entity<SalesReturnHeader>()
                .HasIndex(h => new { h.TenantId, h.ReturnNumber })
                .IsUnique()
                .HasDatabaseName("UX_SalesReturnHeaders_TenantId_ReturnNumber");

            // QuotationNo per tenant
            builder.Entity<Quotation>()
                .HasIndex(q => new { q.TenantId, q.QuotationNo })
                .IsUnique()
                .HasDatabaseName("UX_Quotations_TenantId_QuotationNo");

            // DRNumber per tenant
            builder.Entity<DeliveryReceipt>()
                .HasIndex(dr => new { dr.TenantId, dr.DRNumber })
                .IsUnique()
                .HasDatabaseName("UX_DeliveryReceipts_TenantId_DRNumber");

            // ============================================
            // PURCHASE ORDERS
            // ============================================

            builder.Entity<PurchaseOrder>()
                .HasOne(po => po.Tenant)
                .WithMany()
                .HasForeignKey(po => po.TenantId)
                .OnDelete(DeleteBehavior.NoAction)
                .IsRequired(false);

            builder.Entity<PurchaseOrder>()
                .HasOne(po => po.Branch)
                .WithMany()
                .HasForeignKey(po => po.BranchId)
                .OnDelete(DeleteBehavior.NoAction)
                .IsRequired(false);

            builder.Entity<PurchaseOrder>()
                .HasOne(po => po.Supplier)
                .WithMany()
                .HasForeignKey(po => po.SupplierId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<PurchaseOrderItem>()
                .HasOne(pi => pi.PurchaseOrder)
                .WithMany(po => po.Items)
                .HasForeignKey(pi => pi.PurchaseOrderId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<PurchaseOrderItem>()
                .HasOne(pi => pi.Item)
                .WithMany()
                .HasForeignKey(pi => pi.ItemId)
                .OnDelete(DeleteBehavior.Restrict);

            // PONumber unique per tenant
            builder.Entity<PurchaseOrder>()
                .HasIndex(po => new { po.TenantId, po.PONumber })
                .IsUnique()
                .HasFilter("[TenantId] IS NOT NULL")
                .HasDatabaseName("UX_PurchaseOrders_TenantId_PONumber");

            // Composite indexes for common filter queries
            builder.Entity<PurchaseOrder>()
                .HasIndex(po => new { po.TenantId, po.Status, po.PODate })
                .HasDatabaseName("IX_PurchaseOrders_TenantId_Status_PODate");

            builder.Entity<PurchaseOrder>()
                .HasIndex(po => new { po.TenantId, po.SupplierId })
                .HasDatabaseName("IX_PurchaseOrders_TenantId_SupplierId");

            // ============================================
            // BACKUP RECORDS — platform-owned audit trail
            // ============================================

            builder.Entity<BackupRecord>()
                .HasOne(b => b.Tenant)
                .WithMany()
                .HasForeignKey(b => b.TenantId)
                .OnDelete(DeleteBehavior.SetNull)
                .IsRequired(false);

            builder.Entity<BackupRecord>()
                .HasIndex(b => new { b.TenantId, b.StartedAtUtc })
                .HasDatabaseName("IX_BackupRecords_TenantId_StartedAtUtc");

            builder.Entity<BackupRecord>()
                .HasIndex(b => b.StartedAtUtc)
                .HasDatabaseName("IX_BackupRecords_StartedAtUtc");

            builder.Entity<BackupRecord>()
                .Property(b => b.DatabaseType)
                .HasConversion<string>()
                .HasMaxLength(20);

            builder.Entity<BackupRecord>()
                .Property(b => b.Status)
                .HasConversion<string>()
                .HasMaxLength(20);

            builder.Entity<BackupRecord>()
                .Property(b => b.BackupType)
                .HasConversion<string>()
                .HasMaxLength(20);

            // ============================================
            // RESTORE RECORDS — platform-owned audit trail
            // ============================================

            builder.Entity<RestoreRecord>()
                .HasOne(r => r.BackupRecord)
                .WithMany()
                .HasForeignKey(r => r.BackupRecordId)
                .OnDelete(DeleteBehavior.SetNull)
                .IsRequired(false);

            builder.Entity<RestoreRecord>()
                .HasOne(r => r.Tenant)
                .WithMany()
                .HasForeignKey(r => r.TenantId)
                .OnDelete(DeleteBehavior.SetNull)
                .IsRequired(false);

            builder.Entity<RestoreRecord>()
                .HasIndex(r => r.StartedAtUtc)
                .HasDatabaseName("IX_RestoreRecords_StartedAtUtc");

            builder.Entity<RestoreRecord>()
                .Property(r => r.DatabaseType)
                .HasConversion<string>()
                .HasMaxLength(20);

            builder.Entity<RestoreRecord>()
                .Property(r => r.RestoreMode)
                .HasConversion<string>()
                .HasMaxLength(20);

            builder.Entity<RestoreRecord>()
                .Property(r => r.Status)
                .HasConversion<string>()
                .HasMaxLength(20);

            // ============================================
            // PHASE 5.3 — DAMAGED GOODS & SUPPLIER RETURNS
            // ============================================

            builder.Entity<DamagedGoodsHeader>()
                .HasOne(h => h.Branch)
                .WithMany()
                .HasForeignKey(h => h.BranchId)
                .OnDelete(DeleteBehavior.NoAction)
                .IsRequired(false);

            builder.Entity<DamagedGoodsHeader>()
                .HasOne(h => h.Tenant)
                .WithMany()
                .HasForeignKey(h => h.TenantId)
                .OnDelete(DeleteBehavior.SetNull)
                .IsRequired(false);

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

            builder.Entity<DamagedGoodsDetail>()
                .HasOne(d => d.Unit)
                .WithMany()
                .HasForeignKey(d => d.UnitId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<DamagedGoodsHeader>()
                .HasIndex(h => new { h.TenantId, h.DamageNumber })
                .IsUnique()
                .HasFilter("[TenantId] IS NOT NULL")
                .HasDatabaseName("UX_DamagedGoodsHeaders_TenantId_DamageNumber");

            builder.Entity<SupplierReturnHeader>()
                .HasOne(h => h.Supplier)
                .WithMany()
                .HasForeignKey(h => h.SupplierId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<SupplierReturnHeader>()
                .HasOne(h => h.Branch)
                .WithMany()
                .HasForeignKey(h => h.BranchId)
                .OnDelete(DeleteBehavior.NoAction)
                .IsRequired(false);

            builder.Entity<SupplierReturnHeader>()
                .HasOne(h => h.LinkedDamagedGoods)
                .WithMany()
                .HasForeignKey(h => h.LinkedDamagedGoodsId)
                .OnDelete(DeleteBehavior.NoAction)
                .IsRequired(false);

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

            builder.Entity<SupplierReturnDetail>()
                .HasOne(d => d.Unit)
                .WithMany()
                .HasForeignKey(d => d.UnitId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<SupplierReturnHeader>()
                .HasIndex(h => new { h.TenantId, h.ReturnNumber })
                .IsUnique()
                .HasFilter("[TenantId] IS NOT NULL")
                .HasDatabaseName("UX_SupplierReturnHeaders_TenantId_ReturnNumber");
        }
    }
}
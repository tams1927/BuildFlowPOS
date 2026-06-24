using HardwareManagementSystem.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace HardwareManagementSystem.Data
{
    /// <summary>
    /// Phase 5.0D — abstraction over a tenant's operational database.
    ///
    /// Both <see cref="ApplicationDbContext"/> (shared) and <see cref="TenantDbContext"/>
    /// (dedicated) implement this interface. A service can depend on this interface and
    /// obtain the correct context for the current tenant from
    /// <c>ITenantOperationalContextProvider</c> — without knowing or caring which physical
    /// database it is talking to.
    ///
    /// IMPORTANT: This is an OPT-IN abstraction. Existing controllers/services that inject
    /// <see cref="ApplicationDbContext"/> directly continue to work unchanged (they always
    /// use the shared database). Migrating a service to this interface is what makes it
    /// route to the dedicated database for routing-enabled tenants — that cutover is done
    /// incrementally and verified in the 5.0D.1 sprint.
    ///
    /// Only operational (tenant-owned) entity sets are exposed here. Platform-only sets
    /// (Tenants, SubscriptionPlans, Identity) intentionally live only on the shared context.
    /// </summary>
    public interface ITenantOperationalDbContext
    {
        DbSet<Category> Categories { get; }
        DbSet<Unit> Units { get; }
        DbSet<Item> Items { get; }
        DbSet<ItemUnitConversion> ItemUnitConversions { get; }

        DbSet<Supplier> Suppliers { get; }
        DbSet<StockInHeader> StockInHeaders { get; }
        DbSet<StockInDetail> StockInDetails { get; }
        DbSet<PurchaseOrder> PurchaseOrders { get; }
        DbSet<PurchaseOrderItem> PurchaseOrderItems { get; }
        DbSet<SupplierPayment> SupplierPayments { get; }

        DbSet<Customer> Customers { get; }
        DbSet<CustomerLedger> CustomerLedgers { get; }
        DbSet<SalesHeader> SalesHeaders { get; }
        DbSet<SalesDetail> SalesDetails { get; }
        DbSet<SalesReturnHeader> SalesReturnHeaders { get; }
        DbSet<SalesReturnDetail> SalesReturnDetails { get; }
        DbSet<Quotation> Quotations { get; }
        DbSet<QuotationItem> QuotationItems { get; }
        DbSet<DeliveryReceipt> DeliveryReceipts { get; }
        DbSet<DeliveryReceiptItem> DeliveryReceiptItems { get; }

        DbSet<StockAdjustmentHeader> StockAdjustmentHeaders { get; }
        DbSet<StockAdjustmentDetail> StockAdjustmentDetails { get; }
        DbSet<DamagedGoodsHeader> DamagedGoodsHeaders { get; }
        DbSet<DamagedGoodsDetail> DamagedGoodsDetails { get; }
        DbSet<SupplierReturnHeader> SupplierReturnHeaders { get; }
        DbSet<SupplierReturnDetail> SupplierReturnDetails { get; }
        DbSet<Expense> Expenses { get; }

        DbSet<Branch> Branches { get; }
        DbSet<BranchProductStock> BranchProductStocks { get; }
        DbSet<UserBranch> UserBranches { get; }
        DbSet<BranchTransfer> BranchTransfers { get; }
        DbSet<BranchTransferItem> BranchTransferItems { get; }

        DbSet<SystemSetting> SystemSettings { get; }
        DbSet<ImportBatch> ImportBatches { get; }
        DbSet<ImportBatchRow> ImportBatchRows { get; }
        DbSet<Notification> Notifications { get; }
        DbSet<AuditTrail> AuditTrails { get; }

        DatabaseFacade Database { get; }

        Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    }
}

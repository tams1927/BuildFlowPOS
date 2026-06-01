using HardwareManagementSystem.Models;
using HardwareManagementSystem.Services;
using Microsoft.EntityFrameworkCore;

namespace HardwareManagementSystem.Controllers
{
    public partial class ReportsController
    {
        private async Task<int?> GetReportTenantIdAsync() =>
            await _tenantGuard.GetEffectiveTenantIdAsync();

        private IQueryable<SalesHeader> ApplyTenantScope(IQueryable<SalesHeader> query, int? tenantId)
        {
            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
            {
                query = query.Where(x => x.TenantId == tenantId || x.TenantId == null);
            }

            return query;
        }

        private IQueryable<Item> ApplyTenantScope(IQueryable<Item> query, int? tenantId)
        {
            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
            {
                query = query.Where(x => x.TenantId == tenantId || x.TenantId == null);
            }

            return query;
        }

        private IQueryable<Expense> ApplyTenantScope(IQueryable<Expense> query, int? tenantId)
        {
            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
            {
                query = query.Where(x => x.TenantId == tenantId || x.TenantId == null);
            }

            return query;
        }

        private IQueryable<StockInHeader> ApplyTenantScope(IQueryable<StockInHeader> query, int? tenantId)
        {
            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
            {
                query = query.Where(x => x.TenantId == tenantId || x.TenantId == null);
            }

            return query;
        }

        private IQueryable<BranchTransfer> ApplyTenantScope(IQueryable<BranchTransfer> query, int? tenantId)
        {
            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
            {
                query = query.Where(x => x.TenantId == tenantId || x.TenantId == null);
            }

            return query;
        }

        private IQueryable<BranchProductStock> ApplyTenantScope(IQueryable<BranchProductStock> query, int? tenantId)
        {
            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
            {
                query = query.Where(x => x.TenantId == tenantId || x.TenantId == null);
            }

            return query;
        }

        private IQueryable<CustomerLedger> ApplyTenantScope(IQueryable<CustomerLedger> query, int? tenantId)
        {
            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
            {
                query = query.Where(l =>
                    _context.Customers.Any(c =>
                        c.Id == l.CustomerId &&
                        (c.TenantId == tenantId || c.TenantId == null)));
            }

            return query;
        }

        private IQueryable<SalesReturnHeader> ApplyTenantScope(IQueryable<SalesReturnHeader> query, int? tenantId)
        {
            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
            {
                query = query.Where(r =>
                    _context.SalesHeaders.Any(s =>
                        s.Id == r.SalesHeaderId &&
                        (s.TenantId == tenantId || s.TenantId == null)));
            }

            return query;
        }

        private IQueryable<SalesDetail> ApplyTenantScope(IQueryable<SalesDetail> query, int? tenantId)
        {
            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
            {
                query = query.Where(d =>
                    _context.SalesHeaders.Any(s =>
                        s.Id == d.SalesHeaderId &&
                        (s.TenantId == tenantId || s.TenantId == null)));
            }

            return query;
        }

        private IQueryable<SupplierPayment> ApplyTenantScope(IQueryable<SupplierPayment> query, int? tenantId)
        {
            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
            {
                query = query.Where(p =>
                    _context.StockInHeaders.Any(s =>
                        s.Id == p.StockInHeaderId &&
                        (s.TenantId == tenantId || s.TenantId == null)));
            }

            return query;
        }

        private IQueryable<BranchTransferItem> ApplyTenantScope(IQueryable<BranchTransferItem> query, int? tenantId)
        {
            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
            {
                query = query.Where(i =>
                    _context.BranchTransfers.Any(t =>
                        t.Id == i.BranchTransferId &&
                        (t.TenantId == tenantId || t.TenantId == null)));
            }

            return query;
        }

        private IQueryable<Customer> ApplyCustomerTenantScope(IQueryable<Customer> query, int? tenantId)
        {
            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
            {
                query = query.Where(c => c.TenantId == tenantId || c.TenantId == null);
            }

            return query;
        }
    }
}

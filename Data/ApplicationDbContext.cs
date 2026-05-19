using HardwareManagementSystem.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace HardwareManagementSystem.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
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
        }
    }
}
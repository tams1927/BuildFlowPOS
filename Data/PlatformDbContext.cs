using HardwareManagementSystem.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace HardwareManagementSystem.Data
{
    /// <summary>
    /// PHASE 5.0A FOUNDATION — Platform Database Context.
    ///
    /// This context represents the future Platform Database in the Database-Per-Tenant
    /// architecture. It owns SaaS-level entities: tenant registry, subscription plans,
    /// role permissions, ASP.NET Identity, and global audit/notification data.
    ///
    /// ⚠️  NOT REGISTERED FOR RUNTIME USE IN PHASE 5.0A.
    ///     ApplicationDbContext continues to power all runtime operations.
    ///     This class exists for compilation and architecture documentation only.
    ///     Registration and activation will occur in Phase 5.0B.
    ///
    /// Identity Strategy:
    ///     PlatformDbContext inherits IdentityDbContext so that ASP.NET Identity
    ///     (login, password reset, lockout, 2FA) remains centralized in the platform
    ///     database. Tenant databases do NOT contain Identity tables.
    ///
    /// Uncertain tables (marked with [TRANSITION] below):
    ///     - AuditTrails: SuperAdmin needs cross-tenant view. Decision deferred to Phase 5.0C.
    ///     - Notifications: TenantId = null rows are broadcast notifications. Decide in Phase 5.0C.
    /// </summary>
    public class PlatformDbContext : IdentityDbContext<ApplicationUser>
    {
        public PlatformDbContext(DbContextOptions<PlatformDbContext> options)
            : base(options)
        {
        }

        // ── SaaS Registry ────────────────────────────────────────────────────
        public DbSet<Tenant> Tenants { get; set; }

        public DbSet<SubscriptionPlan> SubscriptionPlans { get; set; }

        // ── Authorization ────────────────────────────────────────────────────
        /// <summary>
        /// Global permission matrix (RoleName × ModuleName).
        /// No TenantId column. Shared across all tenants today.
        /// Future: will be replicated into each TenantDbContext on provisioning,
        /// or tenants will call a platform authorization service.
        /// </summary>
        public DbSet<RolePermission> RolePermissions { get; set; }

        // ── TRANSITION: Global Audit ─────────────────────────────────────────
        /// <summary>
        /// [TRANSITION TABLE] AuditTrails has a nullable TenantId.
        /// Platform-level entries (TenantId = null) belong here.
        /// Tenant-level entries may be duplicated into each TenantDbContext.
        /// Design decision required before Phase 5.0C.
        /// </summary>
        public DbSet<AuditTrail> AuditTrails { get; set; }

        // ── TRANSITION: Broadcast Notifications ──────────────────────────────
        /// <summary>
        /// [TRANSITION TABLE] Notifications with TenantId = null are platform-level
        /// broadcasts visible to all tenants. These broadcast rows belong in PlatformDbContext.
        /// Tenant-scoped notifications (TenantId set) belong in TenantDbContext.
        /// Design decision required before Phase 5.0C.
        /// </summary>
        public DbSet<Notification> Notifications { get; set; }

        // ── SystemSettings Platform Row ───────────────────────────────────────
        /// <summary>
        /// [TRANSITION TABLE] SystemSettings with TenantId = null is the legacy/default
        /// platform settings row. In DB-per-tenant, each tenant DB has exactly one row.
        /// Retained here only for the TenantId = null row.
        /// </summary>
        public DbSet<SystemSetting> SystemSettings { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // ── Tenant registry ──────────────────────────────────────────────
            builder.Entity<Tenant>(entity =>
            {
                entity.HasIndex(t => t.Code).IsUnique().HasDatabaseName("UX_Tenants_Code");
                entity.HasOne(t => t.SubscriptionPlan)
                      .WithMany()
                      .HasForeignKey(t => t.SubscriptionPlanId)
                      .OnDelete(DeleteBehavior.SetNull);
            });

            builder.Entity<SubscriptionPlan>(entity =>
            {
                entity.Property(s => s.MonthlyPrice).HasColumnType("decimal(10,2)");
            });

            // ── Role permissions ─────────────────────────────────────────────
            builder.Entity<RolePermission>(entity =>
            {
                entity.HasIndex(r => new { r.RoleName, r.ModuleName })
                      .IsUnique()
                      .HasDatabaseName("UX_RolePermissions_Role_Module");
            });

            // ── Identity: ApplicationUser → Tenant FK ────────────────────────
            builder.Entity<ApplicationUser>(entity =>
            {
                entity.HasOne(u => u.Tenant)
                      .WithMany()
                      .HasForeignKey(u => u.TenantId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // ── AuditTrail [TRANSITION] ──────────────────────────────────────
            builder.Entity<AuditTrail>(entity =>
            {
                entity.HasIndex(a => a.TenantId).HasDatabaseName("IX_AuditTrails_TenantId");
                entity.HasIndex(a => new { a.TenantId, a.CreatedAt })
                      .HasDatabaseName("IX_AuditTrails_TenantId_CreatedAt");
            });

            // ── Notification [TRANSITION] ────────────────────────────────────
            builder.Entity<Notification>(entity =>
            {
                entity.HasIndex(n => n.TenantId).HasDatabaseName("IX_Notifications_TenantId");
            });

            // ── SystemSetting [TRANSITION] ───────────────────────────────────
            builder.Entity<SystemSetting>(entity =>
            {
                entity.HasOne(s => s.Tenant)
                      .WithMany()
                      .HasForeignKey(s => s.TenantId)
                      .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}

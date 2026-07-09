using System.ComponentModel.DataAnnotations;
using HardwareManagementSystem.Models.Interfaces;

namespace HardwareManagementSystem.Models
{
    public enum TenantStatus
    {
        Trial,
        Active,
        Suspended,
        Expired
    }

    public class Tenant : IPlatformEntity
    {
        public int Id { get; set; }

        [Required]
        [StringLength(150)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [StringLength(20)]
        public string Code { get; set; } = string.Empty;

        [StringLength(150)]
        public string? OwnerName { get; set; }

        [StringLength(150)]
        public string? Email { get; set; }

        [StringLength(30)]
        public string? Phone { get; set; }

        [StringLength(250)]
        public string? Address { get; set; }

        [StringLength(300)]
        public string? Notes { get; set; }

        public bool IsActive { get; set; } = true;

        // ── Subscription ────────────────────────────────────────────
        public TenantStatus Status { get; set; } = TenantStatus.Trial;

        public int? SubscriptionPlanId { get; set; }
        public SubscriptionPlan? SubscriptionPlan { get; set; }

        public DateTime? ExpirationDate { get; set; }

        /// <summary>Per-tenant override; falls back to plan limit if 0.</summary>
        public int MaxBranches { get; set; } = 3;

        /// <summary>Per-tenant override; falls back to plan limit if 0.</summary>
        public int MaxUsers { get; set; } = 10;

        /// <summary>Per-tenant override; falls back to plan limit if 0.</summary>
        public int MaxProducts { get; set; } = 500;

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

        // ── Database routing (Phase 5.0B foundation) ────────────────
        // These fields describe WHERE a tenant's operational data lives.
        // In Phase 5.0B every tenant stays in Shared mode; Dedicated routing
        // is not yet activated at runtime.

        /// <summary>
        /// Storage mode for this tenant's operational data.
        /// Defaults to <see cref="TenantDatabaseMode.Shared"/>.
        /// </summary>
        public TenantDatabaseMode DatabaseMode { get; set; } = TenantDatabaseMode.Shared;

        /// <summary>
        /// Logical database name for a dedicated tenant database
        /// (e.g. "HardBuild_Tenant_42"). Null while in Shared mode.
        /// </summary>
        [StringLength(128)]
        public string? DatabaseName { get; set; }

        /// <summary>
        /// Full connection string used when <see cref="DatabaseMode"/> is Dedicated.
        /// Backend-only — never exposed in normal tenant UI. Null while in Shared mode.
        /// </summary>
        [StringLength(1000)]
        public string? ConnectionString { get; set; }

        /// <summary>
        /// SQL Server host/instance for a dedicated tenant database.
        /// Informational only; null while in Shared mode.
        /// </summary>
        [StringLength(256)]
        public string? DatabaseServer { get; set; }

        /// <summary>
        /// Identifier of the last migration applied to the dedicated tenant database.
        /// Null until a dedicated database is provisioned (Phase 5.0C+).
        /// </summary>
        [StringLength(256)]
        public string? LastDatabaseMigration { get; set; }

        /// <summary>
        /// UTC timestamp when a dedicated tenant database was provisioned.
        /// Null while in Shared mode.
        /// </summary>
        public DateTime? DatabaseProvisionedAtUtc { get; set; }

        // ── Pilot routing (Phase 5.0D) ──────────────────────────────
        // Data must be copied AND validated into the dedicated database before
        // routing can be switched on. Routing is opt-in per tenant and reversible.

        /// <summary>
        /// True once operational data has been copied to the dedicated database
        /// AND row-count validation passed. Set only by the migration service.
        /// </summary>
        public bool DataMigrated { get; set; }

        /// <summary>UTC timestamp when data migration completed and validated.</summary>
        public DateTime? DataMigratedAtUtc { get; set; }

        /// <summary>
        /// Master switch for live per-tenant routing. When true (and all other
        /// conditions hold) the tenant's runtime operations use the dedicated
        /// database. Flipping to false instantly rolls back to the shared database.
        /// </summary>
        public bool RoutingEnabled { get; set; }

        /// <summary>
        /// RC1.7 — true when this tenant was provisioned directly into a dedicated
        /// database at creation time (no Migrate Data step was needed). Used to
        /// display appropriate UI labels and hide the legacy "Migrate Data" button.
        /// False for tenants created before RC1.7 or tenants that went through the
        /// manual shared→dedicated migration pipeline.
        /// </summary>
        public bool IsDirectlyProvisioned { get; set; }

        // ── Helpers ─────────────────────────────────────────────────
        public int EffectiveMaxBranches  => SubscriptionPlan != null && MaxBranches == 0 ? SubscriptionPlan.MaxBranches  : MaxBranches;
        public int EffectiveMaxUsers     => SubscriptionPlan != null && MaxUsers    == 0 ? SubscriptionPlan.MaxUsers     : MaxUsers;
        public int EffectiveMaxProducts  => SubscriptionPlan != null && MaxProducts == 0 ? SubscriptionPlan.MaxProducts  : MaxProducts;

        public bool IsExpired => ExpirationDate.HasValue && ExpirationDate.Value.Date < DateTime.UtcNow.Date;

        /// <summary>True when this tenant is configured for a dedicated database (Phase 5.0C+).</summary>
        public bool UsesDedicatedDatabase =>
            DatabaseMode == TenantDatabaseMode.Dedicated &&
            !string.IsNullOrWhiteSpace(ConnectionString);

        /// <summary>True when this tenant is fully provisioned for a dedicated database.</summary>
        public bool IsDatabaseProvisioned =>
            DatabaseMode == TenantDatabaseMode.Dedicated &&
            DatabaseProvisionedAtUtc.HasValue &&
            !string.IsNullOrWhiteSpace(ConnectionString);

        /// <summary>
        /// Phase 5.0D — the single source of truth for live routing. ALL conditions
        /// must hold before runtime operations use the dedicated database:
        /// Dedicated mode + provisioned + data migrated + routing switch on.
        /// </summary>
        public bool RoutingActive =>
            IsDatabaseProvisioned && DataMigrated && RoutingEnabled;
    }
}

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using HardwareManagementSystem.Models.Interfaces;

namespace HardwareManagementSystem.Models
{
    public class SystemSetting : ITenantEntity
    {
        public int Id { get; set; }

        // ── Tenant isolation ────────────────────────────────────────
        // Each tenant owns its own settings row.
        // SuperAdmin platform row has TenantId = null.
        public int? TenantId { get; set; }
        public virtual Tenant? Tenant { get; set; }

        [Required]
        [StringLength(150)]
        public string BusinessName { get; set; } = "HardBuild POS";

        [StringLength(250)]
        public string? BusinessAddress { get; set; }

        [StringLength(50)]
        public string? ContactNumber { get; set; }

        [StringLength(100)]
        public string? Email { get; set; }

        [StringLength(10)]
        public string CurrencySymbol { get; set; } = "₱";

        [Column(TypeName = "decimal(18,2)")]
        public decimal DefaultVatPercent { get; set; } = 0;

        [StringLength(250)]
        public string? ReceiptFooter { get; set; }

        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        [StringLength(20)]
        public string ReceiptPaperSize { get; set; } = "80mm";

        [StringLength(30)]
        public string ThemeColor { get; set; } = "dark-blue";

        [StringLength(50)]
        public string TaxMode { get; set; } = "VAT";
        // VAT, NONVAT, MANUAL

        [StringLength(30)]
        public string? TIN { get; set; }

        [StringLength(50)]
        public string? VATRegNumber { get; set; }

        /// <summary>Relative web path to the tenant logo, e.g. /uploads/logos/tenant-3/logo.png</summary>
        [StringLength(300)]
        public string? LogoPath { get; set; }
    }
}
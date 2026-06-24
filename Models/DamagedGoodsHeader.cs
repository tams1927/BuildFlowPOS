using System.ComponentModel.DataAnnotations;
using HardwareManagementSystem.Models.Interfaces;

namespace HardwareManagementSystem.Models
{
    public class DamagedGoodsHeader : ITenantEntity
    {
        public int Id { get; set; }

        public int? TenantId { get; set; }

        public int? BranchId { get; set; }

        [Required]
        [StringLength(50)]
        public string DamageNumber { get; set; } = string.Empty;

        public DateTime DamageDate { get; set; } = DateTime.Now;

        [Required]
        [StringLength(30)]
        public string Status { get; set; } = "Pending";

        [StringLength(500)]
        public string? Remarks { get; set; }

        [StringLength(450)]
        public string? CreatedByUserId { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAtUtc { get; set; }

        public Tenant? Tenant { get; set; }

        public Branch? Branch { get; set; }

        public ICollection<DamagedGoodsDetail> Details { get; set; } = new List<DamagedGoodsDetail>();
    }
}

using System.ComponentModel.DataAnnotations;
using HardwareManagementSystem.Models.Interfaces;

namespace HardwareManagementSystem.Models
{
    public class SupplierReturnHeader : ITenantEntity
    {
        public int Id { get; set; }

        public int? TenantId { get; set; }

        public int? BranchId { get; set; }

        public int SupplierId { get; set; }

        [Required]
        [StringLength(50)]
        public string ReturnNumber { get; set; } = string.Empty;

        public DateTime ReturnDate { get; set; } = DateTime.Now;

        [Required]
        [StringLength(30)]
        public string Status { get; set; } = "Pending";

        public int? LinkedDamagedGoodsId { get; set; }

        [StringLength(500)]
        public string? Remarks { get; set; }

        [StringLength(450)]
        public string? CreatedByUserId { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAtUtc { get; set; }

        public Tenant? Tenant { get; set; }

        public Branch? Branch { get; set; }

        public Supplier? Supplier { get; set; }

        public DamagedGoodsHeader? LinkedDamagedGoods { get; set; }

        public ICollection<SupplierReturnDetail> Details { get; set; } = new List<SupplierReturnDetail>();
    }
}

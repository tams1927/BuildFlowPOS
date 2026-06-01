using System.ComponentModel.DataAnnotations.Schema;
using HardwareManagementSystem.Models.Interfaces;

namespace HardwareManagementSystem.Models
{
    public class BranchTransferItem : ITenantEntity
    {
        public int Id { get; set; }

        public int BranchTransferId { get; set; }

        public int ProductId { get; set; }

        [Column(TypeName = "decimal(18,3)")]
        public decimal Quantity { get; set; }

        public BranchTransfer? BranchTransfer { get; set; }

        public Item? Product { get; set; }
    }
}

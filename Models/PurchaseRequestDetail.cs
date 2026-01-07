using System.ComponentModel.DataAnnotations;

namespace MNBEMART.Models
{
    public class PurchaseRequestDetail
    {
        public int Id { get; set; }

        [Required]
        public int PurchaseRequestId { get; set; }
        public PurchaseRequest? PurchaseRequest { get; set; }

        [Required]
        public int MaterialId { get; set; }
        public Material? Material { get; set; }

        [Required]
        public decimal RequestedQuantity { get; set; }

        public decimal CurrentStock { get; set; }
        public decimal MinimumStock { get; set; }

        public int? PreferredSupplierId { get; set; }
        public Supplier? PreferredSupplier { get; set; }

        public decimal? EstimatedPrice { get; set; }

        [StringLength(200)]
        public string? Notes { get; set; }
    }
}























































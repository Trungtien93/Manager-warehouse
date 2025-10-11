using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;


namespace MNBEMART.Models
{
    public class StockReceipt
    {
        public int Id { get; set; }
        public string? ReceiptNumber { get; set; }
        public DateTime CreatedAt { get; set; }

        [Required] public int CreatedById { get; set; }
        [ValidateNever] public User? CreatedBy { get; set; }

        [Required] public int WarehouseId { get; set; }
        [ValidateNever] public Warehouse? Warehouse { get; set; }

        public string? DeliveredByName { get; set; }
        public string? ReferenceDocumentNumber { get; set; }
        public DateTime? ReferenceDate { get; set; }

        // KHÔNG required
        public string? AttachedDocuments { get; set; }

        public DocumentStatus Status { get; set; } = DocumentStatus.Moi;

        public int? ApprovedById { get; set; }
        [ValidateNever] public User? ApprovedBy { get; set; }
        public DateTime? ApprovedAt { get; set; }
        public string? Note { get; set; }

        public DateTime? ReceivedAt { get; set; }
        public int? ReceivedById { get; set; }


        public ICollection<StockReceiptDetail> Details { get; set; } = new List<StockReceiptDetail>();

       
        
    }
}

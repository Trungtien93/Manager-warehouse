using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MNBEMART.Models
{
    public enum DocumentType
    {
        StockReceipt = 1,
        StockIssue = 2,
        StockTransfer = 3,
        PurchaseRequest = 4,
        Other = 99
    }

    public enum DocumentCategory
    {
        Invoice = 1,        // Hóa đơn
        Evidence = 2,        // Ảnh minh chứng
        Contract = 3,        // Hợp đồng
        DeliveryNote = 4,    // Phiếu giao hàng
        Other = 99           // Khác
    }

    public class Document
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string DocumentType { get; set; } = ""; // "StockReceipt", "StockIssue", etc.

        [Required]
        public int DocumentId { get; set; } // ID của document (StockReceipt.Id, StockIssue.Id, etc.)

        [Required]
        [MaxLength(255)]
        public string FileName { get; set; } = "";

        [Required]
        [MaxLength(500)]
        public string FilePath { get; set; } = ""; // Relative path từ wwwroot

        [Required]
        public long FileSize { get; set; } // Size in bytes

        [MaxLength(100)]
        public string? MimeType { get; set; }

        [MaxLength(500)]
        public string? Description { get; set; }

        public DocumentCategory? Category { get; set; } // Phân loại tài liệu

        [Required]
        public DateTime UploadedAt { get; set; } = DateTime.Now;

        [Required]
        public int UploadedById { get; set; }

        [ForeignKey("UploadedById")]
        public User? UploadedBy { get; set; }
    }
}



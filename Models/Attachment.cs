// Models/Attachment.cs
namespace MNBEMART.Models
{
    public class Attachment
    {
        public int Id { get; set; }
        public string DocumentType { get; set; } = ""; // "StockReceipt" | "StockIssue" | "StockTransfer" | "StockAdjustment" | "StockCount"
        public int DocumentId { get; set; }
        public string FileName { get; set; } = "";
        public string FileUrl { get; set; } = "";
        public DateTime UploadedAt { get; set; } = DateTime.Now;
        public int UploadedById { get; set; }

        public User UploadedBy { get; set; } = null!;
    }
}

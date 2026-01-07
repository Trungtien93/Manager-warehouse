using System;

namespace MNBEMART.Models
{
    public class AuditLog
    {
        public int Id { get; set; }

        public int UserId { get; set; }
        public User User { get; set; }

        // Ví dụ: Create/Approve/Delete...
        public string Action { get; set; }
        // Ví dụ: Receipts/Issues/Transfers...
        public string ObjectType { get; set; }
        public string ObjectId { get; set; }
        public DateTime Timestamp { get; set; }

        // Bổ sung để hiển thị giống mẫu
        public string? Module { get; set; } // Chức năng/nhóm
        public string? Content { get; set; } // Nội dung chi tiết
        public int? WarehouseId { get; set; }
        public Warehouse? Warehouse { get; set; }
    }
}

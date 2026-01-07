using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MNBEMART.Models
{
    public enum NotificationType
    {
        Receipt = 1,    // Phiếu nhập
        Issue = 2,      // Phiếu xuất
        Transfer = 3,   // Phiếu chuyển kho
        System = 4,     // Thông báo hệ thống
        PurchaseRequest = 5,  // Đề xuất đặt hàng
        ExpiryAlert = 6,      // Cảnh báo hết hạn
        LowStockAlert = 7,    // Cảnh báo tồn kho thấp
        UserRegistration = 8,  // User đăng ký mới
        RoleCreated = 9,      // Role mới được tạo
        WarehouseCreated = 10, // Kho mới được tạo
        UnconfirmedDocument = 11 // Phiếu chưa xác nhận quá lâu
    }

    public enum NotificationPriority
    {
        Low = 0,
        Normal = 1,
        High = 2,
        Urgent = 3
    }

    public class Notification
    {
        public int Id { get; set; }
        
        [Required]
        public NotificationType Type { get; set; }
        
        [Required]
        public int DocumentId { get; set; } // ID của phiếu (StockReceipt, StockIssue, StockTransfer)
        
        [Required]
        [MaxLength(200)]
        public string Title { get; set; } = "";
        
        [MaxLength(500)]
        public string? Message { get; set; }
        
        public bool IsRead { get; set; } = false;
        
        public bool IsImportant { get; set; } = false; // Đánh dấu quan trọng
        
        public bool IsArchived { get; set; } = false; // Lưu trữ
        
        public bool IsDeleted { get; set; } = false; // Xóa mềm
        
        public DateTime? DeletedAt { get; set; } // Thời gian xóa
        
        public NotificationPriority Priority { get; set; } = NotificationPriority.Normal; // Mức độ ưu tiên
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        public int? UserId { get; set; } // null = tất cả users, hoặc specific user ID
        
        [ForeignKey("UserId")]
        public User? User { get; set; }
    }
}











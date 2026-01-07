using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MNBEMART.Models
{
    public enum EmailDigestFrequency
    {
        None = 0,
        Daily = 1,
        Weekly = 2
    }

    public class NotificationSettings
    {
        [Key]
        public int UserId { get; set; }
        
        [ForeignKey("UserId")]
        public User User { get; set; } = null!;
        
        public bool EnableSound { get; set; } = true;
        
        public bool EnableDesktopNotifications { get; set; } = true;
        
        public bool EnableEmailNotifications { get; set; } = false;
        
        [MaxLength(50)]
        public string SoundType { get; set; } = "default"; // default, urgent, info
        
        public int UpdateFrequency { get; set; } = 30; // Tần suất cập nhật (giây)
        
        [MaxLength(1000)]
        public string? EnabledTypes { get; set; } // JSON string: danh sách NotificationType được bật
        
        public EmailDigestFrequency EmailDigestFrequency { get; set; } = EmailDigestFrequency.None;
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}




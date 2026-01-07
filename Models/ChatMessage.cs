using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MNBEMART.Models
{
    public class ChatMessage
    {
        public int Id { get; set; }

        [Required]
        public int UserId { get; set; }

        [ForeignKey("UserId")]
        public User? User { get; set; }

        [Required]
        [MaxLength(10)]
        public string Role { get; set; } = "user"; // "user" or "bot"

        [Required]
        [MaxLength(5000)]
        public string Message { get; set; } = "";

        [Required]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}


















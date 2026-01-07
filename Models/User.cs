using System.ComponentModel.DataAnnotations;

// Models/User.cs
namespace MNBEMART.Models
{
    public class User
    {
        public int Id { get; set; }
        public string FullName { get; set; }
        public string Username { get; set; }
        public string PasswordHash { get; set; }
        public string Role { get; set; }
        public string? AvatarUrl { get; set; }

        // Email & Verification
        [EmailAddress]
        public string? Email { get; set; }
        public bool IsEmailVerified { get; set; } = false;
        public string? EmailVerificationToken { get; set; }
        public DateTime? EmailVerifiedAt { get; set; }

        // Account Status & Approval
        public bool IsActive { get; set; } = false;  // Chưa được duyệt
        public DateTime RegisteredAt { get; set; } = DateTime.Now;
        
        // Approval
        public int? ApprovedById { get; set; }
        public User? ApprovedBy { get; set; }
        public DateTime? ApprovedAt { get; set; }
        public string? RejectionReason { get; set; }

        // Password Reset
        public string? PasswordResetToken { get; set; }
        public DateTime? PasswordResetTokenExpiry { get; set; }

        // Security
        public int FailedLoginAttempts { get; set; } = 0;
        public DateTime? LockedUntil { get; set; }
        public DateTime? LastLoginAt { get; set; }
        public string? LastLoginIp { get; set; }

        // Navigation Properties
        public ICollection<UserWarehouse> UserWarehouses { get; set; } = new List<UserWarehouse>();
        public ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
        public ICollection<StockReceipt> CreatedReceipts { get; set; } = new List<StockReceipt>();
        public ICollection<StockIssue> CreatedIssues { get; set; } = new List<StockIssue>();
        public ICollection<UserRole> UserRoles { get; set; }
        public ICollection<User> ApprovedUsers { get; set; } = new List<User>(); // Users approved by this admin
    }

}


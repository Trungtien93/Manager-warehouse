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

        public ICollection<UserWarehouse> UserWarehouses { get; set; } = new List<UserWarehouse>();
        public ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
        public ICollection<StockReceipt> CreatedReceipts { get; set; } = new List<StockReceipt>();
        public ICollection<StockIssue> CreatedIssues { get; set; } = new List<StockIssue>();
        public ICollection<UserRole> UserRoles { get; set; }
}

}


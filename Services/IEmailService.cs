namespace MNBEMART.Services
{
    public interface IEmailService
    {
        Task<bool> SendEmailAsync(string to, string subject, string body, bool isHtml = true);
        Task SendRegistrationNotificationToAdminAsync(string userFullName, string userEmail, string username, int userId);
        Task SendAccountActivatedEmailAsync(string userEmail, string userFullName, string username, string loginUrl);
        Task SendAccountRejectedEmailAsync(string userEmail, string userFullName, string reason);
        Task SendPasswordResetEmailAsync(string userEmail, string userFullName, string resetToken, string resetUrl);
        Task SendPasswordChangedConfirmationEmailAsync(string userEmail, string userFullName);
        Task SendPurchaseRequestNotificationAsync(int purchaseRequestId, string requestNumber, string requestedByName, List<string> materialNames, decimal totalQuantity);
        Task SendPurchaseRequestStatusChangeAsync(string userEmail, string userFullName, string requestNumber, string status, string? reason = null);
        Task SendExpiryAlertEmailAsync(List<ExpiryAlertItem> expiredItems, List<ExpiryAlertItem> expiringSoonItems);
        Task SendLowStockAlertEmailAsync(List<LowStockItem> lowStockItems);
        Task<bool> SendNotificationEmailAsync(string userEmail, Models.Notification notification);
        Task<bool> SendNotificationDigestAsync(string userEmail, List<Models.Notification> notifications, string frequency);
    }

    public class ExpiryAlertItem
    {
        public string MaterialCode { get; set; } = "";
        public string MaterialName { get; set; } = "";
        public string LotNumber { get; set; } = "";
        public decimal Quantity { get; set; }
        public string Unit { get; set; } = "";
        public DateTime ExpiryDate { get; set; }
        public int DaysRemaining { get; set; }
        public string WarehouseName { get; set; } = "";
    }

    public class LowStockItem
    {
        public string MaterialCode { get; set; } = "";
        public string MaterialName { get; set; } = "";
        public decimal CurrentStock { get; set; }
        public decimal MinimumStock { get; set; }
        public string Unit { get; set; } = "";
        public string WarehouseName { get; set; } = "";
    }
}




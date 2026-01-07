using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MNBEMART.Data;
using MNBEMART.Models;
using MNBEMART.Services;

namespace MNBEMART.Services
{
    public class QuickActionService : IQuickActionService
    {
        private readonly AppDbContext _context;
        private readonly INotificationService _notificationService;
        private readonly IEmailService _emailService;
        private readonly IConfiguration _configuration;

        public QuickActionService(
            AppDbContext context,
            INotificationService notificationService,
            IEmailService emailService,
            IConfiguration configuration)
        {
            _context = context;
            _notificationService = notificationService;
            _emailService = emailService;
            _configuration = configuration;
        }

        public async Task<bool> QuickApprovePurchaseRequestAsync(int purchaseRequestId, int userId)
        {
            var request = await _context.PurchaseRequests
                .Include(pr => pr.RequestedBy)
                .FirstOrDefaultAsync(pr => pr.Id == purchaseRequestId);

            if (request == null || request.Status != PRStatus.Pending)
                return false;

            request.Status = PRStatus.Approved;
            request.ApprovedById = userId;
            request.ApprovedDate = DateTime.Now;
            request.UpdatedAt = DateTime.Now;

            await _context.SaveChangesAsync();

            // Thông báo cho user tạo đề xuất
            if (request.RequestedById > 0)
            {
                await _notificationService.CreateNotificationAsync(
                    NotificationType.PurchaseRequest,
                    purchaseRequestId,
                    $"Đề xuất đặt hàng đã được duyệt: {request.RequestNumber}",
                    $"Đề xuất đặt hàng của bạn đã được admin duyệt",
                    request.RequestedById,
                    NotificationPriority.Normal
                );
            }

            // Update notification
            var notification = await _context.Notifications
                .FirstOrDefaultAsync(n => n.Type == NotificationType.PurchaseRequest && n.DocumentId == purchaseRequestId);
            if (notification != null)
            {
                notification.IsRead = true;
                await _context.SaveChangesAsync();
            }

            // Send email
            try
            {
                if (request.RequestedBy != null && !string.IsNullOrEmpty(request.RequestedBy.Email))
                {
                    await _emailService.SendPurchaseRequestStatusChangeAsync(
                        request.RequestedBy.Email,
                        request.RequestedBy.FullName,
                        request.RequestNumber,
                        "Approved"
                    );
                }
            }
            catch { }

            return true;
        }

        public async Task<bool> QuickRejectPurchaseRequestAsync(int purchaseRequestId, int userId, string? reason = null)
        {
            var request = await _context.PurchaseRequests
                .Include(pr => pr.RequestedBy)
                .FirstOrDefaultAsync(pr => pr.Id == purchaseRequestId);

            if (request == null || request.Status != PRStatus.Pending)
                return false;

            request.Status = PRStatus.Cancelled;
            request.Notes = (request.Notes ?? "") + $"\nLý do từ chối: {reason ?? "Không có lý do"}";
            request.UpdatedAt = DateTime.Now;

            await _context.SaveChangesAsync();

            // Thông báo cho user tạo đề xuất
            if (request.RequestedById > 0)
            {
                await _notificationService.CreateNotificationAsync(
                    NotificationType.PurchaseRequest,
                    purchaseRequestId,
                    $"Đề xuất đặt hàng bị từ chối: {request.RequestNumber}",
                    reason != null ? $"Lý do: {reason}" : "Đề xuất đặt hàng của bạn đã bị từ chối",
                    request.RequestedById,
                    NotificationPriority.High
                );
            }

            // Update notification
            var notification = await _context.Notifications
                .FirstOrDefaultAsync(n => n.Type == NotificationType.PurchaseRequest && n.DocumentId == purchaseRequestId);
            if (notification != null)
            {
                notification.IsRead = true;
                await _context.SaveChangesAsync();
            }

            // Send email
            try
            {
                if (request.RequestedBy != null && !string.IsNullOrEmpty(request.RequestedBy.Email))
                {
                    await _emailService.SendPurchaseRequestStatusChangeAsync(
                        request.RequestedBy.Email,
                        request.RequestedBy.FullName,
                        request.RequestNumber,
                        "Rejected",
                        reason
                    );
                }
            }
            catch { }

            return true;
        }

        public async Task<bool> QuickConfirmReceiptAsync(int receiptId, int userId)
        {
            var receipt = await _context.StockReceipts
                .Include(r => r.CreatedBy)
                .FirstOrDefaultAsync(r => r.Id == receiptId);

            if (receipt == null || receipt.Status != DocumentStatus.Moi)
                return false;

            receipt.Status = DocumentStatus.DaNhapHang;
            receipt.ApprovedById = userId;
            receipt.ApprovedAt = DateTime.Now;

            await _context.SaveChangesAsync();

            // Thông báo cho user tạo phiếu
            if (receipt.CreatedById > 0)
            {
                await _notificationService.CreateNotificationAsync(
                    NotificationType.Receipt,
                    receiptId,
                    $"Phiếu nhập đã được xác nhận: {receipt.ReceiptNumber}",
                    $"Phiếu nhập của bạn đã được xác nhận bởi admin",
                    receipt.CreatedById,
                    NotificationPriority.Normal
                );
            }

            // Update notification
            var notification = await _context.Notifications
                .FirstOrDefaultAsync(n => n.Type == NotificationType.Receipt && n.DocumentId == receiptId);
            if (notification != null)
            {
                notification.IsRead = true;
                await _context.SaveChangesAsync();
            }

            return true;
        }

        public async Task<bool> QuickCancelReceiptAsync(int receiptId, int userId)
        {
            var receipt = await _context.StockReceipts
                .FirstOrDefaultAsync(r => r.Id == receiptId);

            if (receipt == null || receipt.Status == DocumentStatus.DaHuy)
                return false;

            receipt.Status = DocumentStatus.DaHuy;

            await _context.SaveChangesAsync();

            // Thông báo cho user tạo phiếu
            if (receipt.CreatedById > 0)
            {
                await _notificationService.CreateNotificationAsync(
                    NotificationType.Receipt,
                    receiptId,
                    $"Phiếu nhập đã bị hủy: {receipt.ReceiptNumber}",
                    $"Phiếu nhập của bạn đã bị hủy bởi admin",
                    receipt.CreatedById,
                    NotificationPriority.High
                );
            }

            // Update notification
            var notification = await _context.Notifications
                .FirstOrDefaultAsync(n => n.Type == NotificationType.Receipt && n.DocumentId == receiptId);
            if (notification != null)
            {
                notification.IsRead = true;
                await _context.SaveChangesAsync();
            }

            return true;
        }

        public async Task<bool> QuickApproveUserAsync(int userId, int approvedBy)
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null || user.IsActive)
                return false;

            user.IsActive = true;
            user.ApprovedById = approvedBy;
            user.ApprovedAt = DateTime.Now;

            await _context.SaveChangesAsync();

            // Update notification
            var notification = await _context.Notifications
                .FirstOrDefaultAsync(n => n.Type == NotificationType.UserRegistration && n.DocumentId == userId);
            if (notification != null)
            {
                notification.IsRead = true;
                await _context.SaveChangesAsync();
            }

            // Send email
            try
            {
                if (!string.IsNullOrEmpty(user.Email))
                {
                    var baseUrl = _configuration["Email:BaseUrl"] ?? "http://localhost:5073";
                    await _emailService.SendAccountActivatedEmailAsync(
                        user.Email,
                        user.FullName,
                        user.Username,
                        $"{baseUrl}/Account/Login"
                    );
                }
            }
            catch { }

            return true;
        }

        public async Task<bool> QuickRejectUserAsync(int userId, int rejectedBy, string? reason = null)
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null || user.IsActive)
                return false;

            user.IsActive = false;
            user.RejectionReason = reason;

            await _context.SaveChangesAsync();

            // Update notification
            var notification = await _context.Notifications
                .FirstOrDefaultAsync(n => n.Type == NotificationType.UserRegistration && n.DocumentId == userId);
            if (notification != null)
            {
                notification.IsRead = true;
                await _context.SaveChangesAsync();
            }

            // Send email
            try
            {
                if (!string.IsNullOrEmpty(user.Email))
                {
                    await _emailService.SendAccountRejectedEmailAsync(
                        user.Email,
                        user.FullName,
                        reason ?? "Không đáp ứng yêu cầu"
                    );
                }
            }
            catch { }

            return true;
        }
    }
}


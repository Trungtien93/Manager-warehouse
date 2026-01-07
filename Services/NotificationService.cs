using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using MNBEMART.Data;
using MNBEMART.Models;
using MNBEMART.Hubs;

namespace MNBEMART.Services
{
    public class NotificationService : INotificationService
    {
        private readonly AppDbContext _context;
        private readonly IHubContext<NotificationHub> _hubContext;

        public NotificationService(AppDbContext context, IHubContext<NotificationHub> hubContext)
        {
            _context = context;
            _hubContext = hubContext;
        }

        public async Task CreateNotificationAsync(NotificationType type, int documentId, string title, string? message = null, int? userId = null, NotificationPriority priority = NotificationPriority.Normal)
        {
            var notification = new Notification
            {
                Type = type,
                DocumentId = documentId,
                Title = title,
                Message = message,
                UserId = userId,
                IsRead = false,
                Priority = priority,
                CreatedAt = DateTime.UtcNow
            };

            _context.Notifications.Add(notification);
            await _context.SaveChangesAsync();

            // Send via SignalR
            await SendNotificationViaSignalRAsync(notification.Id, userId);
        }

        public async Task CreateNotificationForUsersAsync(NotificationType type, int documentId, string title, string? message = null, List<int>? userIds = null, NotificationPriority priority = NotificationPriority.Normal)
        {
            if (userIds == null || !userIds.Any())
            {
                // If no specific users, create a notification for all users (UserId = null)
                await CreateNotificationAsync(type, documentId, title, message, null, priority);
                return;
            }

            // Create a notification for each user
            var notifications = userIds.Select(userId => new Notification
            {
                Type = type,
                DocumentId = documentId,
                Title = title,
                Message = message,
                UserId = userId,
                IsRead = false,
                Priority = priority,
                CreatedAt = DateTime.UtcNow
            }).ToList();

            _context.Notifications.AddRange(notifications);
            await _context.SaveChangesAsync();

            // Send via SignalR to each user
            foreach (var notification in notifications)
            {
                await SendNotificationViaSignalRAsync(notification.Id, notification.UserId);
            }
        }

        public async Task<int> GetUnreadCountAsync(int? userId = null)
        {
            var query = _context.Notifications.Where(n => !n.IsRead && !n.IsDeleted);

            if (userId.HasValue)
            {
                // Chỉ đếm thông báo chưa đọc của user hiện tại
                query = query.Where(n => n.UserId == userId.Value);
            }
            else
            {
                // Nếu không có userId, không có thông báo nào
                query = query.Where(n => false);
            }

            return await query.CountAsync();
        }

        public async Task<List<Notification>> GetNotificationsAsync(int? userId = null, int page = 1, int pageSize = 50, bool? isImportant = null, bool? isArchived = null)
        {
            var query = _context.Notifications.Where(n => !n.IsDeleted).AsQueryable();

            if (userId.HasValue)
            {
                // Chỉ lấy thông báo của user hiện tại, không lấy thông báo chung
                query = query.Where(n => n.UserId == userId.Value);
            }
            else
            {
                // Nếu không có userId, không trả về thông báo nào
                query = query.Where(n => false);
            }

            if (isImportant.HasValue)
            {
                query = query.Where(n => n.IsImportant == isImportant.Value);
            }

            if (isArchived.HasValue)
            {
                query = query.Where(n => n.IsArchived == isArchived.Value);
            }
            else
            {
                // By default, don't show archived
                query = query.Where(n => !n.IsArchived);
            }

            return await query
                .OrderByDescending(n => n.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
        }

        public async Task MarkAsReadAsync(int notificationId, int? userId = null)
        {
            var notification = await _context.Notifications.FindAsync(notificationId);
            
            if (notification != null)
            {
                // Chỉ user sở hữu thông báo mới đánh dấu được
                if (notification.UserId == userId)
                {
                    notification.IsRead = true;
                    await _context.SaveChangesAsync();
                }
            }
        }

        public async Task MarkAllAsReadAsync(int? userId = null)
        {
            var query = _context.Notifications.Where(n => !n.IsRead && !n.IsDeleted);

            if (userId.HasValue)
            {
                // Chỉ đánh dấu đã đọc thông báo của user hiện tại
                query = query.Where(n => n.UserId == userId.Value);
            }
            else
            {
                // Nếu không có userId, không có thông báo nào để đánh dấu
                query = query.Where(n => false);
            }

            var notifications = await query.ToListAsync();
            foreach (var notification in notifications)
            {
                notification.IsRead = true;
            }

            await _context.SaveChangesAsync();
        }

        public async Task MarkAsImportantAsync(int notificationId, int? userId = null)
        {
            var notification = await _context.Notifications.FindAsync(notificationId);
            
            if (notification != null && notification.UserId == userId)
            {
                notification.IsImportant = true;
                await _context.SaveChangesAsync();
            }
        }

        public async Task UnmarkAsImportantAsync(int notificationId, int? userId = null)
        {
            var notification = await _context.Notifications.FindAsync(notificationId);
            
            if (notification != null && notification.UserId == userId)
            {
                notification.IsImportant = false;
                await _context.SaveChangesAsync();
            }
        }

        public async Task DeleteNotificationAsync(int notificationId, int? userId = null)
        {
            var notification = await _context.Notifications.FindAsync(notificationId);
            
            if (notification != null && notification.UserId == userId)
            {
                notification.IsDeleted = true;
                notification.DeletedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }
        }

        public async Task DeleteMultipleNotificationsAsync(List<int> ids, int? userId = null)
        {
            var query = _context.Notifications.Where(n => ids.Contains(n.Id) && !n.IsDeleted);

            if (userId.HasValue)
            {
                // Chỉ lấy thông báo của user hiện tại
                query = query.Where(n => n.UserId == userId.Value);
            }
            else
            {
                query = query.Where(n => false);
            }

            var notifications = await query.ToListAsync();
            foreach (var notification in notifications)
            {
                notification.IsDeleted = true;
                notification.DeletedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();
        }

        public async Task ArchiveNotificationAsync(int notificationId, int? userId = null)
        {
            var notification = await _context.Notifications.FindAsync(notificationId);
            
            if (notification != null && notification.UserId == userId)
            {
                notification.IsArchived = true;
                await _context.SaveChangesAsync();
            }
        }

        public async Task RestoreNotificationAsync(int notificationId, int? userId = null)
        {
            var notification = await _context.Notifications.FindAsync(notificationId);
            
            if (notification != null && notification.UserId == userId)
            {
                notification.IsArchived = false;
                await _context.SaveChangesAsync();
            }
        }

        public async Task<List<Notification>> GetArchivedNotificationsAsync(int? userId = null, int page = 1, int pageSize = 50)
        {
            return await GetNotificationsAsync(userId, page, pageSize, null, true);
        }

        public async Task<List<Notification>> GetImportantNotificationsAsync(int? userId = null, int page = 1, int pageSize = 50)
        {
            return await GetNotificationsAsync(userId, page, pageSize, true, null);
        }

        public async Task AutoDeleteOldNotificationsAsync(int? userId = null, int daysOld = 90)
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-daysOld);
            var query = _context.Notifications
                .Where(n => !n.IsDeleted && n.CreatedAt < cutoffDate && n.IsRead && !n.IsImportant && !n.IsArchived);

            // Chỉ xóa thông báo của user cụ thể (nếu có userId)
            if (userId.HasValue)
            {
                query = query.Where(n => n.UserId == userId.Value);
            }
            else
            {
                // Nếu không có userId, không xóa thông báo nào
                query = query.Where(n => false);
            }

            var oldNotifications = await query.ToListAsync();

            foreach (var notification in oldNotifications)
            {
                notification.IsDeleted = true;
                notification.DeletedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();
        }

        public async Task SendNotificationViaSignalRAsync(int notificationId, int? userId = null)
        {
            var notification = await _context.Notifications
                .AsNoTracking()
                .FirstOrDefaultAsync(n => n.Id == notificationId);

            if (notification == null) return;

            var notificationData = new
            {
                id = notification.Id,
                type = notification.Type.ToString(),
                title = notification.Title,
                message = notification.Message,
                isRead = notification.IsRead,
                isImportant = notification.IsImportant,
                priority = notification.Priority.ToString(),
                createdAt = notification.CreatedAt,
                documentId = notification.DocumentId,
                detailUrl = GetDetailUrl(notification.Type, notification.DocumentId),
                timeAgo = GetTimeAgo(notification.CreatedAt)
            };

            if (userId.HasValue)
            {
                await _hubContext.Clients.Group($"user_{userId.Value}").SendAsync("ReceiveNotification", notificationData);
            }
            else
            {
                // Broadcast to all users
                await _hubContext.Clients.All.SendAsync("ReceiveNotification", notificationData);
            }
        }

        private string GetDetailUrl(NotificationType type, int documentId)
        {
            return type switch
            {
                NotificationType.Receipt => $"/StockReceipts/Details/{documentId}",
                NotificationType.Issue => $"/StockIssues/Details/{documentId}",
                NotificationType.Transfer => $"/StockTransfers/Details/{documentId}",
                NotificationType.PurchaseRequest => $"/PurchaseRequests/Details/{documentId}",
                NotificationType.ExpiryAlert => "/Materials/Expiring",
                NotificationType.LowStockAlert => "/Stocks/Index",
                NotificationType.UserRegistration => $"/Users/Details/{documentId}",
                NotificationType.RoleCreated => $"/Roles/Details/{documentId}",
                NotificationType.WarehouseCreated => $"/Warehouses/Details/{documentId}",
                NotificationType.UnconfirmedDocument => "/Notifications/Index",
                _ => "#"
            };
        }

        private string GetTimeAgo(DateTime dateTime)
        {
            var timeSpan = DateTime.UtcNow - dateTime.ToUniversalTime();
            
            if (timeSpan.TotalMinutes < 1)
                return "Vừa xong";
            if (timeSpan.TotalMinutes < 60)
                return $"{(int)timeSpan.TotalMinutes} phút trước";
            if (timeSpan.TotalHours < 24)
                return $"{(int)timeSpan.TotalHours} giờ trước";
            if (timeSpan.TotalDays < 7)
                return $"{(int)timeSpan.TotalDays} ngày trước";
            
            return dateTime.ToLocalTime().ToString("dd/MM/yyyy");
        }
    }
}














using MNBEMART.Models;

namespace MNBEMART.Services
{
    public interface INotificationService
    {
        Task CreateNotificationAsync(NotificationType type, int documentId, string title, string? message = null, int? userId = null, NotificationPriority priority = NotificationPriority.Normal);
        Task CreateNotificationForUsersAsync(NotificationType type, int documentId, string title, string? message = null, List<int>? userIds = null, NotificationPriority priority = NotificationPriority.Normal);
        Task<int> GetUnreadCountAsync(int? userId = null);
        Task<List<Notification>> GetNotificationsAsync(int? userId = null, int page = 1, int pageSize = 50, bool? isImportant = null, bool? isArchived = null);
        Task MarkAsReadAsync(int notificationId, int? userId = null);
        Task MarkAllAsReadAsync(int? userId = null);
        Task MarkAsImportantAsync(int notificationId, int? userId = null);
        Task UnmarkAsImportantAsync(int notificationId, int? userId = null);
        Task DeleteNotificationAsync(int notificationId, int? userId = null);
        Task DeleteMultipleNotificationsAsync(List<int> ids, int? userId = null);
        Task ArchiveNotificationAsync(int notificationId, int? userId = null);
        Task RestoreNotificationAsync(int notificationId, int? userId = null);
        Task<List<Notification>> GetArchivedNotificationsAsync(int? userId = null, int page = 1, int pageSize = 50);
        Task<List<Notification>> GetImportantNotificationsAsync(int? userId = null, int page = 1, int pageSize = 50);
        Task AutoDeleteOldNotificationsAsync(int? userId = null, int daysOld = 90);
        Task SendNotificationViaSignalRAsync(int notificationId, int? userId = null);
    }
}














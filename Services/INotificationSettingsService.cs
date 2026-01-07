using MNBEMART.Models;

namespace MNBEMART.Services
{
    public interface INotificationSettingsService
    {
        Task<NotificationSettings> GetSettingsAsync(int userId);
        Task UpdateSettingsAsync(int userId, NotificationSettings settings);
        NotificationSettings GetDefaultSettings();
    }
}




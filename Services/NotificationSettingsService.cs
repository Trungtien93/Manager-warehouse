using Microsoft.EntityFrameworkCore;
using MNBEMART.Data;
using MNBEMART.Models;
using System.Text.Json;

namespace MNBEMART.Services
{
    public class NotificationSettingsService : INotificationSettingsService
    {
        private readonly AppDbContext _context;

        public NotificationSettingsService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<NotificationSettings> GetSettingsAsync(int userId)
        {
            var settings = await _context.NotificationSettings
                .FirstOrDefaultAsync(s => s.UserId == userId);

            if (settings == null)
            {
                // Create default settings if not exists
                settings = GetDefaultSettings();
                settings.UserId = userId;
                _context.NotificationSettings.Add(settings);
                await _context.SaveChangesAsync();
            }

            return settings;
        }

        public async Task UpdateSettingsAsync(int userId, NotificationSettings settings)
        {
            var existing = await _context.NotificationSettings
                .FirstOrDefaultAsync(s => s.UserId == userId);

            if (existing == null)
            {
                settings.UserId = userId;
                settings.CreatedAt = DateTime.UtcNow;
                settings.UpdatedAt = DateTime.UtcNow;
                _context.NotificationSettings.Add(settings);
            }
            else
            {
                existing.EnableSound = settings.EnableSound;
                existing.EnableDesktopNotifications = settings.EnableDesktopNotifications;
                existing.EnableEmailNotifications = settings.EnableEmailNotifications;
                existing.SoundType = settings.SoundType;
                existing.UpdateFrequency = settings.UpdateFrequency;
                existing.EnabledTypes = settings.EnabledTypes;
                existing.EmailDigestFrequency = settings.EmailDigestFrequency;
                existing.UpdatedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();
        }

        public NotificationSettings GetDefaultSettings()
        {
            // Default: enable all notification types
            var allTypes = Enum.GetValues(typeof(NotificationType))
                .Cast<NotificationType>()
                .Select(t => t.ToString())
                .ToList();
            var enabledTypesJson = JsonSerializer.Serialize(allTypes);

            return new NotificationSettings
            {
                EnableSound = true,
                EnableDesktopNotifications = true,
                EnableEmailNotifications = false,
                SoundType = "default",
                UpdateFrequency = 30,
                EnabledTypes = enabledTypesJson,
                EmailDigestFrequency = EmailDigestFrequency.None,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
        }
    }
}


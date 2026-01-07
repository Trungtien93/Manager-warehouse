using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using MNBEMART.Data;
using MNBEMART.Models;
using MNBEMART.Services;

namespace MNBEMART.Services
{
    public class NotificationEmailDigestService : BackgroundService
    {
        private readonly ILogger<NotificationEmailDigestService> _logger;
        private readonly IServiceProvider _serviceProvider;

        public NotificationEmailDigestService(
            ILogger<NotificationEmailDigestService> logger,
            IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Notification Email Digest Background Service started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var now = DateTime.Now;
                    var targetTime = DateTime.Today.AddHours(8); // 8 AM

                    // If it's past 8 AM today, schedule for tomorrow
                    if (now > targetTime)
                    {
                        targetTime = targetTime.AddDays(1);
                    }

                    var delay = targetTime - now;
                    _logger.LogInformation($"Next email digest check scheduled at {targetTime:yyyy-MM-dd HH:mm:ss}");

                    await Task.Delay(delay, stoppingToken);

                    if (stoppingToken.IsCancellationRequested)
                        break;

                    _logger.LogInformation("Running scheduled email digest check...");

                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
                        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

                        // Get users with email digest enabled
                        var usersWithDigest = await context.NotificationSettings
                            .Where(s => s.EnableEmailNotifications && 
                                       s.EmailDigestFrequency != EmailDigestFrequency.None)
                            .Include(s => s.User)
                            .ToListAsync();

                        foreach (var settings in usersWithDigest)
                        {
                            try
                            {
                                var shouldSend = false;
                                var frequency = "Daily";
                                
                                if (settings.EmailDigestFrequency == EmailDigestFrequency.Daily)
                                {
                                    // Send daily digest
                                    shouldSend = true;
                                    frequency = "Daily";
                                }
                                else if (settings.EmailDigestFrequency == EmailDigestFrequency.Weekly)
                                {
                                    // Send weekly digest (only on Monday)
                                    if (now.DayOfWeek == DayOfWeek.Monday)
                                    {
                                        shouldSend = true;
                                        frequency = "Weekly";
                                    }
                                }

                                if (!shouldSend) continue;

                                // Get unread notifications for this user
                                var notifications = await notificationService.GetNotificationsAsync(
                                    settings.UserId, 
                                    page: 1, 
                                    pageSize: 100
                                );

                                // Filter unread notifications
                                var unreadNotifications = notifications.Where(n => !n.IsRead).ToList();

                                if (unreadNotifications.Any() && !string.IsNullOrEmpty(settings.User?.Email))
                                {
                                    await emailService.SendNotificationDigestAsync(
                                        settings.User.Email,
                                        unreadNotifications,
                                        frequency
                                    );

                                    _logger.LogInformation($"Sent {frequency} email digest to user {settings.UserId} with {unreadNotifications.Count} notifications");
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, $"Error sending email digest to user {settings.UserId}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in Notification Email Digest Background Service");
                    await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
                }
            }

            _logger.LogInformation("Notification Email Digest Background Service stopped");
        }
    }
}




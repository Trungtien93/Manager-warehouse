using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using MNBEMART.Data;
using MNBEMART.Models;
using MNBEMART.Services;

namespace MNBEMART.Services
{
    public class UnconfirmedDocumentBackgroundService : BackgroundService
    {
        private readonly ILogger<UnconfirmedDocumentBackgroundService> _logger;
        private readonly IServiceProvider _serviceProvider;

        public UnconfirmedDocumentBackgroundService(
            ILogger<UnconfirmedDocumentBackgroundService> logger,
            IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Unconfirmed Document Alert Background Service started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Run daily at 9 AM
                    var now = DateTime.Now;
                    var targetTime = DateTime.Today.AddHours(9);

                    if (now > targetTime)
                    {
                        targetTime = targetTime.AddDays(1);
                    }

                    var delay = targetTime - now;
                    _logger.LogInformation($"Next unconfirmed document check scheduled at {targetTime:yyyy-MM-dd HH:mm:ss}");

                    await Task.Delay(delay, stoppingToken);

                    if (stoppingToken.IsCancellationRequested)
                        break;

                    _logger.LogInformation("Running scheduled unconfirmed document check...");

                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

                        var threeDaysAgo = DateTime.Now.AddDays(-3);

                        // Check unconfirmed receipts (older than 3 days)
                        var unconfirmedReceipts = await context.StockReceipts
                            .Include(r => r.Warehouse)
                            .Include(r => r.CreatedBy)
                            .Where(r => r.Status == DocumentStatus.Moi && r.CreatedAt < threeDaysAgo)
                            .ToListAsync();

                        // Check unconfirmed issues
                        var unconfirmedIssues = await context.StockIssues
                            .Include(i => i.Warehouse)
                            .Include(i => i.CreatedBy)
                            .Where(i => i.Status == DocumentStatus.Moi && i.CreatedAt < threeDaysAgo)
                            .ToListAsync();

                        var totalUnconfirmed = unconfirmedReceipts.Count + unconfirmedIssues.Count;

                        if (totalUnconfirmed > 0)
                        {
                            // Get admin users
                            var adminUserIds = await context.Users
                                .Where(u => u.IsActive && u.Role != null && u.Role.ToLower() == "admin")
                                .Select(u => u.Id)
                                .ToListAsync();

                            if (adminUserIds.Any())
                            {
                                var title = $"Cảnh báo: {totalUnconfirmed} phiếu chưa xác nhận quá 3 ngày";
                                var message = $"Có {unconfirmedReceipts.Count} phiếu nhập và {unconfirmedIssues.Count} phiếu xuất chưa được xác nhận quá 3 ngày";

                                await notificationService.CreateNotificationForUsersAsync(
                                    NotificationType.UnconfirmedDocument,
                                    0,
                                    title,
                                    message,
                                    adminUserIds,
                                    NotificationPriority.High
                                );

                                _logger.LogInformation($"Created unconfirmed document alert for {adminUserIds.Count} users");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in Unconfirmed Document Alert Background Service");
                    await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
                }
            }

            _logger.LogInformation("Unconfirmed Document Alert Background Service stopped");
        }
    }
}




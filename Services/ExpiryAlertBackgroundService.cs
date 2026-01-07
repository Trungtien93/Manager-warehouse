using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using MNBEMART.Data;
using MNBEMART.Models;
using MNBEMART.Services;

namespace MNBEMART.Services
{
    public class ExpiryAlertBackgroundService : BackgroundService
    {
        private readonly ILogger<ExpiryAlertBackgroundService> _logger;
        private readonly IServiceProvider _serviceProvider;

        public ExpiryAlertBackgroundService(
            ILogger<ExpiryAlertBackgroundService> logger,
            IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Expiry Alert Background Service started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var now = DateTime.Now;
                    var targetTime = DateTime.Today.AddHours(8); // 8 AM today

                    // If it's past 8 AM today, schedule for tomorrow
                    if (now > targetTime)
                    {
                        targetTime = targetTime.AddDays(1);
                    }

                    var delay = targetTime - now;
                    _logger.LogInformation($"Next expiry alert check scheduled at {targetTime:yyyy-MM-dd HH:mm:ss}");

                    await Task.Delay(delay, stoppingToken);

                    if (stoppingToken.IsCancellationRequested)
                        break;

                    // Execute expiry check
                    _logger.LogInformation("Running scheduled expiry alert check...");
                    
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
                        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

                        var today = DateTime.Today;
                        var warningDate = today.AddDays(30);

                        // Get expired items
                        var expiredItems = await context.StockLots
                            .Include(l => l.Material)
                            .Include(l => l.Warehouse)
                            .Where(l => l.Quantity > 0 && l.ExpiryDate != null && l.ExpiryDate < today)
                            .Select(l => new ExpiryAlertItem
                            {
                                MaterialCode = l.Material.Code,
                                MaterialName = l.Material.Name,
                                LotNumber = l.LotNumber,
                                Quantity = l.Quantity,
                                Unit = l.Material.Unit,
                                ExpiryDate = l.ExpiryDate!.Value,
                                DaysRemaining = (int)(l.ExpiryDate.Value.Date - today).TotalDays,
                                WarehouseName = l.Warehouse.Name
                            })
                            .ToListAsync();

                        // Get expiring soon items (within 30 days)
                        var expiringSoonItems = await context.StockLots
                            .Include(l => l.Material)
                            .Include(l => l.Warehouse)
                            .Where(l => l.Quantity > 0 && l.ExpiryDate != null && 
                                       l.ExpiryDate >= today && l.ExpiryDate <= warningDate)
                            .Select(l => new ExpiryAlertItem
                            {
                                MaterialCode = l.Material.Code,
                                MaterialName = l.Material.Name,
                                LotNumber = l.LotNumber,
                                Quantity = l.Quantity,
                                Unit = l.Material.Unit,
                                ExpiryDate = l.ExpiryDate!.Value,
                                DaysRemaining = (int)(l.ExpiryDate.Value.Date - today).TotalDays,
                                WarehouseName = l.Warehouse.Name
                            })
                            .OrderBy(l => l.ExpiryDate)
                            .ToListAsync();

                        // Only send email if there are items to report
                        if (expiredItems.Any() || expiringSoonItems.Any())
                        {
                            await emailService.SendExpiryAlertEmailAsync(expiredItems, expiringSoonItems);
                            _logger.LogInformation($"Sent expiry alert email: {expiredItems.Count} expired, {expiringSoonItems.Count} expiring soon");

                            // Create in-app notifications for admins and warehouse managers
                            try
                            {
                                // Get Admin users
                                var adminUserIds = await context.Users
                                    .Where(u => u.IsActive && u.Role != null && u.Role.ToLower() == "admin")
                                    .Select(u => u.Id)
                                    .ToListAsync();

                                // Get warehouse managers (users with warehouse assignments or Warehouses management permission)
                                var warehouseManagerUserIds = await (from u in context.Users
                                                                    where u.IsActive
                                                                    join ur in context.UserRoles on u.Id equals ur.UserId
                                                                    join rp in context.RolePermissions on ur.RoleId equals rp.RoleId
                                                                    join p in context.Permissions on rp.PermissionId equals p.Id
                                                                    where p.Module == "Warehouses" && (rp.CanRead || rp.CanUpdate || rp.CanCreate)
                                                                    select u.Id)
                                                                    .Union(
                                                                        from u in context.Users
                                                                        where u.IsActive
                                                                        join uw in context.UserWarehouses on u.Id equals uw.UserId
                                                                        select u.Id
                                                                    )
                                                                    .Distinct()
                                                                    .ToListAsync();

                                // Combine and remove duplicates
                                var notifyUserIds = adminUserIds.Union(warehouseManagerUserIds).Distinct().ToList();

                                if (notifyUserIds.Any())
                                {
                                    var totalCount = expiredItems.Count + expiringSoonItems.Count;
                                    var title = $"Cảnh báo: {totalCount} lô hàng sắp hết hạn";
                                    var message = $"Có {expiredItems.Count} lô đã hết hạn và {expiringSoonItems.Count} lô sắp hết hạn trong 30 ngày tới";
                                    
                                    // Create notification for each user (using documentId = 0 for system alerts)
                                    await notificationService.CreateNotificationForUsersAsync(
                                        NotificationType.ExpiryAlert,
                                        0,
                                        title,
                                        message,
                                        notifyUserIds,
                                        expiredItems.Count > 0 ? NotificationPriority.High : NotificationPriority.Normal
                                    );
                                    
                                    _logger.LogInformation($"Created expiry alert notifications for {notifyUserIds.Count} users");
                                }
                            }
                            catch (Exception notifEx)
                            {
                                _logger.LogError(notifEx, "Error creating expiry alert notifications");
                            }
                        }
                        else
                        {
                            _logger.LogInformation("No expiring items found, skipping email");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in Expiry Alert Background Service");
                    // Wait 1 hour before retrying on error
                    await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
                }
            }

            _logger.LogInformation("Expiry Alert Background Service stopped");
        }
    }
}


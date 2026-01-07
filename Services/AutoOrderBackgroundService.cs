using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MNBEMART.Services
{
    public class AutoOrderBackgroundService : BackgroundService
    {
        private readonly ILogger<AutoOrderBackgroundService> _logger;
        private readonly IServiceProvider _serviceProvider;

        public AutoOrderBackgroundService(
            ILogger<AutoOrderBackgroundService> logger,
            IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Auto Order Background Service started");

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
                    _logger.LogInformation($"Next auto-check scheduled at {targetTime:yyyy-MM-dd HH:mm:ss}");

                    await Task.Delay(delay, stoppingToken);

                    if (stoppingToken.IsCancellationRequested)
                        break;

                    // Execute auto-check
                    _logger.LogInformation("Running scheduled auto-check for low stock...");
                    
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var autoOrderService = scope.ServiceProvider.GetRequiredService<IAutoOrderService>();
                        var createdIds = await autoOrderService.CheckLowStockAndCreateRequests();
                        
                        if (createdIds.Any())
                        {
                            _logger.LogInformation($"Created {createdIds.Count} purchase request(s): {string.Join(", ", createdIds)}");
                        }
                        else
                        {
                            _logger.LogInformation("No low stock items found");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in Auto Order Background Service");
                    // Wait 1 hour before retrying on error
                    await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
                }
            }

            _logger.LogInformation("Auto Order Background Service stopped");
        }
    }
}























































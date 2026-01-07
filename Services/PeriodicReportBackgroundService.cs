using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MNBEMART.Services
{
    public class PeriodicReportBackgroundService : BackgroundService
    {
        private readonly ILogger<PeriodicReportBackgroundService> _logger;
        private readonly IServiceProvider _serviceProvider;

        public PeriodicReportBackgroundService(
            ILogger<PeriodicReportBackgroundService> logger,
            IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Periodic Report Background Service started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var now = DateTime.Now;

                    // Daily report: 7 AM
                    var dailyTarget = DateTime.Today.AddHours(7);
                    if (now > dailyTarget)
                        dailyTarget = dailyTarget.AddDays(1);

                    // Weekly report: Monday, 7 AM
                    var nextMonday = DateTime.Today.AddDays(((int)DayOfWeek.Monday - (int)now.DayOfWeek + 7) % 7);
                    if (nextMonday == DateTime.Today && now.Hour >= 7)
                        nextMonday = nextMonday.AddDays(7);
                    var weeklyTarget = nextMonday.AddHours(7);

                    // Monthly report: 1st of month, 7 AM
                    var nextMonth = new DateTime(now.Year, now.Month, 1).AddMonths(1);
                    var monthlyTarget = nextMonth.AddHours(7);

                    // Find the next scheduled time
                    var nextTarget = new[] { dailyTarget, weeklyTarget, monthlyTarget }.Min();
                    var delay = nextTarget - now;

                    _logger.LogInformation($"Next report scheduled at {nextTarget:yyyy-MM-dd HH:mm:ss}");

                    await Task.Delay(delay, stoppingToken);

                    if (stoppingToken.IsCancellationRequested)
                        break;

                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var reportEmailService = scope.ServiceProvider.GetService<IReportEmailService>();

                        if (reportEmailService == null)
                        {
                            _logger.LogWarning("IReportEmailService not registered, skipping reports");
                            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
                            continue;
                        }

                        var currentTime = DateTime.Now;

                        // Daily report
                        if (currentTime.Date == dailyTarget.Date && currentTime.Hour == 7)
                        {
                            _logger.LogInformation("Sending daily report...");
                            try
                            {
                                await reportEmailService.SendDailyReportAsync();
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error sending daily report");
                            }
                        }

                        // Weekly report
                        if (currentTime.Date == weeklyTarget.Date && currentTime.Hour == 7 && currentTime.DayOfWeek == DayOfWeek.Monday)
                        {
                            _logger.LogInformation("Sending weekly report...");
                            try
                            {
                                await reportEmailService.SendWeeklyReportAsync();
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error sending weekly report");
                            }
                        }

                        // Monthly report
                        if (currentTime.Date == monthlyTarget.Date && currentTime.Hour == 7 && currentTime.Day == 1)
                        {
                            _logger.LogInformation("Sending monthly report...");
                            try
                            {
                                await reportEmailService.SendMonthlyReportAsync();
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error sending monthly report");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in Periodic Report Background Service");
                    await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
                }
            }

            _logger.LogInformation("Periodic Report Background Service stopped");
        }
    }
}


namespace MNBEMART.Services
{
    public interface IReportEmailService
    {
        Task SendDailyReportAsync();
        Task SendWeeklyReportAsync();
        Task SendMonthlyReportAsync();
    }
}


using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MNBEMART.Data;

namespace MNBEMART.Services
{
    public class ReportEmailService : IReportEmailService
    {
        private readonly AppDbContext _context;
        private readonly IEmailService _emailService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ReportEmailService> _logger;
        private readonly string _baseUrl;

        public ReportEmailService(
            AppDbContext context,
            IEmailService emailService,
            IConfiguration configuration,
            ILogger<ReportEmailService> logger)
        {
            _context = context;
            _emailService = emailService;
            _configuration = configuration;
            _logger = logger;
            _baseUrl = _configuration["Email:BaseUrl"] ?? "http://localhost:5073";
        }

        public async Task SendDailyReportAsync()
        {
            try
            {
                var today = DateTime.Today;
                var yesterday = today.AddDays(-1);

                // Get low stock count
                var lowStockCount = await _context.Stocks
                    .Include(s => s.Material)
                    .Where(s => s.Material.MinimumStock != null && s.Material.MinimumStock > 0)
                    .GroupBy(s => s.MaterialId)
                    .Select(g => new
                    {
                        MaterialId = g.Key,
                        Material = g.First().Material,
                        CurrentStock = g.Sum(x => x.Quantity)
                    })
                    .Where(x => x.Material.MinimumStock.HasValue && x.CurrentStock < x.Material.MinimumStock.Value)
                    .CountAsync();

                // Get expiring items count (30 days)
                var warningDate = today.AddDays(30);
                var expiringCount = await _context.StockLots
                    .Where(l => l.Quantity > 0 && l.ExpiryDate != null && l.ExpiryDate >= today && l.ExpiryDate <= warningDate)
                    .CountAsync();

                var expiredCount = await _context.StockLots
                    .Where(l => l.Quantity > 0 && l.ExpiryDate != null && l.ExpiryDate < today)
                    .CountAsync();

                // Get receipts/issues count yesterday
                var receiptsCount = await _context.StockReceipts
                    .Where(r => r.CreatedAt.Date == yesterday)
                    .CountAsync();

                var issuesCount = await _context.StockIssues
                    .Where(i => i.CreatedAt.Date == yesterday)
                    .CountAsync();

                var adminEmails = _configuration["Email:AdminEmails"]?.Split(',').Select(e => e.Trim()).Where(e => !string.IsNullOrEmpty(e)).ToList() 
                                ?? new List<string>();

                if (adminEmails.Count == 0)
                    return;

                var subject = $"Báo cáo hàng ngày - {today:dd/MM/yyyy} - BEMART";
                var body = $@"
                    <html>
                    <body style='font-family: Arial, sans-serif; line-height: 1.6; color: #333;'>
                        <div style='max-width: 600px; margin: 0 auto; padding: 20px;'>
                            <h2 style='color: #2563eb;'>Báo cáo hàng ngày - {today:dd/MM/yyyy}</h2>
                            <p>Xin chào Admin,</p>
                            <p>Dưới đây là báo cáo tổng hợp hoạt động kho ngày {yesterday:dd/MM/yyyy}:</p>
                            
                            <div style='background-color: #f8fafc; padding: 20px; border-radius: 5px; margin: 20px 0;'>
                                <h3 style='margin-top: 0; color: #1e40af;'>Tổng quan</h3>
                                <table style='width: 100%; border-collapse: collapse;'>
                                    <tr>
                                        <td style='padding: 8px;'><strong>Phiếu nhập (hôm qua):</strong></td>
                                        <td style='padding: 8px; text-align: right;'>{receiptsCount}</td>
                                    </tr>
                                    <tr>
                                        <td style='padding: 8px;'><strong>Phiếu xuất (hôm qua):</strong></td>
                                        <td style='padding: 8px; text-align: right;'>{issuesCount}</td>
                                    </tr>
                                    <tr>
                                        <td style='padding: 8px;'><strong>Vật tư tồn kho thấp:</strong></td>
                                        <td style='padding: 8px; text-align: right; color: #ef4444;'>{lowStockCount}</td>
                                    </tr>
                                    <tr>
                                        <td style='padding: 8px;'><strong>Lô đã hết hạn:</strong></td>
                                        <td style='padding: 8px; text-align: right; color: #ef4444;'>{expiredCount}</td>
                                    </tr>
                                    <tr>
                                        <td style='padding: 8px;'><strong>Lô sắp hết hạn (30 ngày):</strong></td>
                                        <td style='padding: 8px; text-align: right; color: #f59e0b;'>{expiringCount}</td>
                                    </tr>
                                </table>
                            </div>

                            <p style='text-align: center; margin: 30px 0;'>
                                <a href='{_baseUrl}/Home/Dashboard' 
                                   style='background-color: #2563eb; color: white; padding: 12px 30px; text-decoration: none; border-radius: 5px; display: inline-block;'>
                                    Xem Dashboard
                                </a>
                            </p>

                            <p style='margin-top: 30px; font-size: 12px; color: #666;'>
                                Đây là email tự động từ hệ thống BEMART.
                            </p>
                        </div>
                    </body>
                    </html>";

                foreach (var email in adminEmails)
                {
                    await _emailService.SendEmailAsync(email, subject, body);
                }

                _logger.LogInformation("Daily report sent successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending daily report");
            }
        }

        public async Task SendWeeklyReportAsync()
        {
            try
            {
                var today = DateTime.Today;
                var weekStart = today.AddDays(-(int)today.DayOfWeek); // Monday of this week
                var weekEnd = weekStart.AddDays(6); // Sunday

                // Get receipts/issues for the week
                var receipts = await _context.StockReceipts
                    .Where(r => r.CreatedAt.Date >= weekStart && r.CreatedAt.Date <= weekEnd)
                    .ToListAsync();

                var issues = await _context.StockIssues
                    .Where(i => i.CreatedAt.Date >= weekStart && i.CreatedAt.Date <= weekEnd)
                    .ToListAsync();

                var totalReceiptQty = receipts.Sum(r => r.Details?.Sum(d => (decimal)d.Quantity) ?? 0m);
                var totalIssueQty = issues.Sum(i => i.Details?.Sum(d => (decimal)d.Quantity) ?? 0m);

                var adminEmails = _configuration["Email:AdminEmails"]?.Split(',').Select(e => e.Trim()).Where(e => !string.IsNullOrEmpty(e)).ToList() 
                                ?? new List<string>();

                if (adminEmails.Count == 0)
                    return;

                var subject = $"Báo cáo tuần - Tuần {weekStart:dd/MM} đến {weekEnd:dd/MM/yyyy} - BEMART";
                var body = $@"
                    <html>
                    <body style='font-family: Arial, sans-serif; line-height: 1.6; color: #333;'>
                        <div style='max-width: 600px; margin: 0 auto; padding: 20px;'>
                            <h2 style='color: #2563eb;'>Báo cáo tuần</h2>
                            <p>Xin chào Admin,</p>
                            <p>Báo cáo tổng hợp tuần từ {weekStart:dd/MM/yyyy} đến {weekEnd:dd/MM/yyyy}:</p>
                            
                            <div style='background-color: #f8fafc; padding: 20px; border-radius: 5px; margin: 20px 0;'>
                                <h3 style='margin-top: 0; color: #1e40af;'>Hoạt động kho</h3>
                                <table style='width: 100%; border-collapse: collapse;'>
                                    <tr>
                                        <td style='padding: 8px;'><strong>Số phiếu nhập:</strong></td>
                                        <td style='padding: 8px; text-align: right;'>{receipts.Count}</td>
                                    </tr>
                                    <tr>
                                        <td style='padding: 8px;'><strong>Tổng số lượng nhập:</strong></td>
                                        <td style='padding: 8px; text-align: right;'>{totalReceiptQty:N0}</td>
                                    </tr>
                                    <tr>
                                        <td style='padding: 8px;'><strong>Số phiếu xuất:</strong></td>
                                        <td style='padding: 8px; text-align: right;'>{issues.Count}</td>
                                    </tr>
                                    <tr>
                                        <td style='padding: 8px;'><strong>Tổng số lượng xuất:</strong></td>
                                        <td style='padding: 8px; text-align: right;'>{totalIssueQty:N0}</td>
                                    </tr>
                                </table>
                            </div>

                            <p style='text-align: center; margin: 30px 0;'>
                                <a href='{_baseUrl}/Reports' 
                                   style='background-color: #2563eb; color: white; padding: 12px 30px; text-decoration: none; border-radius: 5px; display: inline-block;'>
                                    Xem báo cáo chi tiết
                                </a>
                            </p>

                            <p style='margin-top: 30px; font-size: 12px; color: #666;'>
                                Đây là email tự động từ hệ thống BEMART.
                            </p>
                        </div>
                    </body>
                    </html>";

                foreach (var email in adminEmails)
                {
                    await _emailService.SendEmailAsync(email, subject, body);
                }

                _logger.LogInformation("Weekly report sent successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending weekly report");
            }
        }

        public async Task SendMonthlyReportAsync()
        {
            try
            {
                var today = DateTime.Today;
                var monthStart = new DateTime(today.Year, today.Month, 1);
                var monthEnd = monthStart.AddMonths(1).AddDays(-1);

                // Get receipts/issues for the month
                var receipts = await _context.StockReceipts
                    .Include(r => r.Details)
                    .Where(r => r.CreatedAt.Date >= monthStart && r.CreatedAt.Date <= monthEnd)
                    .ToListAsync();

                var issues = await _context.StockIssues
                    .Include(i => i.Details)
                    .Where(i => i.CreatedAt.Date >= monthStart && i.CreatedAt.Date <= monthEnd)
                    .ToListAsync();

                var totalReceiptQty = receipts.Sum(r => r.Details?.Sum(d => (decimal)d.Quantity) ?? 0m);
                var totalReceiptValue = receipts.Sum(r => r.Details?.Sum(d => (decimal)d.Quantity * d.UnitPrice) ?? 0m);
                var totalIssueQty = issues.Sum(i => i.Details?.Sum(d => (decimal)d.Quantity) ?? 0m);
                var totalIssueValue = issues.Sum(i => i.Details?.Sum(d => (decimal)d.Quantity * d.UnitPrice) ?? 0m);

                var adminEmails = _configuration["Email:AdminEmails"]?.Split(',').Select(e => e.Trim()).Where(e => !string.IsNullOrEmpty(e)).ToList() 
                                ?? new List<string>();

                if (adminEmails.Count == 0)
                    return;

                var subject = $"Báo cáo tháng - {monthStart:MM/yyyy} - BEMART";
                var body = $@"
                    <html>
                    <body style='font-family: Arial, sans-serif; line-height: 1.6; color: #333;'>
                        <div style='max-width: 600px; margin: 0 auto; padding: 20px;'>
                            <h2 style='color: #2563eb;'>Báo cáo tháng {monthStart:MM/yyyy}</h2>
                            <p>Xin chào Admin,</p>
                            <p>Báo cáo tổng hợp tháng {monthStart:MM/yyyy}:</p>
                            
                            <div style='background-color: #f8fafc; padding: 20px; border-radius: 5px; margin: 20px 0;'>
                                <h3 style='margin-top: 0; color: #1e40af;'>Hoạt động kho</h3>
                                <table style='width: 100%; border-collapse: collapse;'>
                                    <tr>
                                        <td style='padding: 8px;'><strong>Số phiếu nhập:</strong></td>
                                        <td style='padding: 8px; text-align: right;'>{receipts.Count}</td>
                                    </tr>
                                    <tr>
                                        <td style='padding: 8px;'><strong>Tổng số lượng nhập:</strong></td>
                                        <td style='padding: 8px; text-align: right;'>{totalReceiptQty:N0}</td>
                                    </tr>
                                    <tr>
                                        <td style='padding: 8px;'><strong>Tổng giá trị nhập:</strong></td>
                                        <td style='padding: 8px; text-align: right;'>{totalReceiptValue:N0} VNĐ</td>
                                    </tr>
                                    <tr>
                                        <td style='padding: 8px;'><strong>Số phiếu xuất:</strong></td>
                                        <td style='padding: 8px; text-align: right;'>{issues.Count}</td>
                                    </tr>
                                    <tr>
                                        <td style='padding: 8px;'><strong>Tổng số lượng xuất:</strong></td>
                                        <td style='padding: 8px; text-align: right;'>{totalIssueQty:N0}</td>
                                    </tr>
                                    <tr>
                                        <td style='padding: 8px;'><strong>Tổng giá trị xuất:</strong></td>
                                        <td style='padding: 8px; text-align: right;'>{totalIssueValue:N0} VNĐ</td>
                                    </tr>
                                </table>
                            </div>

                            <p style='text-align: center; margin: 30px 0;'>
                                <a href='{_baseUrl}/Reports' 
                                   style='background-color: #2563eb; color: white; padding: 12px 30px; text-decoration: none; border-radius: 5px; display: inline-block;'>
                                    Xem báo cáo chi tiết
                                </a>
                            </p>

                            <p style='margin-top: 30px; font-size: 12px; color: #666;'>
                                Đây là email tự động từ hệ thống BEMART.
                            </p>
                        </div>
                    </body>
                    </html>";

                foreach (var email in adminEmails)
                {
                    await _emailService.SendEmailAsync(email, subject, body);
                }

                _logger.LogInformation("Monthly report sent successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending monthly report");
            }
        }
    }
}


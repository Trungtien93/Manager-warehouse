using System;
using System.Linq;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MNBEMART.Data;
using MNBEMART.Models;
using MNBEMART.Services;

namespace MNBEMART.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ChatController : ControllerBase
    {
        private readonly IChatService _chatService;
        private readonly IReportGenerationService _reportService;
        private readonly AppDbContext _db;
        private readonly ILogger<ChatController> _logger;

        public ChatController(IChatService chatService, IReportGenerationService reportService, AppDbContext db, ILogger<ChatController> logger)
        {
            _chatService = chatService;
            _reportService = reportService;
            _db = db;
            _logger = logger;
        }

        private int? GetCurrentUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(userIdClaim, out int userId) ? userId : null;
        }

        public class ChatRequest
        {
            public string Message { get; set; } = string.Empty;
        }

        [HttpPost("ask")]
        public async Task<IActionResult> Ask([FromBody] ChatRequest request)
        {
            try
            {
                if (request == null || string.IsNullOrWhiteSpace(request.Message))
                    return BadRequest(new { error = "Nội dung câu hỏi không được để trống." });

                var userId = GetCurrentUserId();
                if (!userId.HasValue)
                    return Unauthorized(new { error = "Người dùng chưa đăng nhập." });

                // Lưu tin nhắn của user vào database (không làm fail nếu lỗi)
                try
                {
                    await SaveMessageAsync(userId.Value, "user", request.Message);
                }
                catch (Exception saveEx)
                {
                    _logger?.LogWarning(saveEx, "Không thể lưu tin nhắn user vào database, tiếp tục xử lý");
                }

                string reply;
                try
                {
                    reply = await _chatService.AskAsync(request.Message, HttpContext.RequestAborted);
                }
                catch (Exception chatEx)
                {
                    _logger?.LogError(chatEx, "Lỗi khi gọi ChatService");
                    return StatusCode(500, new { error = "Không thể kết nối đến dịch vụ chatbot. Vui lòng thử lại sau." });
                }
                
                // Lưu phản hồi của bot vào database (không làm fail nếu lỗi)
                if (!string.IsNullOrWhiteSpace(reply))
                {
                    try
                    {
                        await SaveMessageAsync(userId.Value, "bot", reply);
                    }
                    catch (Exception saveEx)
                    {
                        _logger?.LogWarning(saveEx, "Không thể lưu phản hồi bot vào database");
                    }
                }
                
                // Kiểm tra nếu câu hỏi yêu cầu tạo báo cáo
                var message = request.Message.ToLowerInvariant();
                if (message.Contains("tạo báo cáo") || message.Contains("xuất báo cáo"))
                {
                    try
                    {
                        var reportData = await GenerateReportFromMessage(request.Message);
                        // Chỉ thêm report vào response khi có dữ liệu (reportData != null và có Rows)
                        if (reportData != null && reportData.Rows != null && reportData.Rows.Count > 0)
                        {
                            return Ok(new { reply, report = reportData });
                        }
                        // Nếu không có dữ liệu, chỉ trả về reply (AI đã tự trả lời là không có dữ liệu)
                        return Ok(new { reply });
                    }
                    catch (Exception reportEx)
                    {
                        _logger?.LogWarning(reportEx, "Không thể tạo báo cáo từ message");
                        // Trả về reply mà không có report nếu lỗi
                        return Ok(new { reply });
                    }
                }

                return Ok(new { reply });
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Lỗi không mong đợi trong Ask endpoint");
                return StatusCode(500, new { error = "Đã xảy ra lỗi khi xử lý câu hỏi. Vui lòng thử lại sau." });
            }
        }

        // GET: /api/chat/history - Lấy lịch sử chat của user
        [HttpGet("history")]
        public async Task<IActionResult> GetHistory([FromQuery] int limit = 100)
        {
            try
            {
                var userId = GetCurrentUserId();
                if (!userId.HasValue)
                    return Unauthorized(new { error = "Người dùng chưa đăng nhập." });

                var messages = await _db.ChatMessages
                    .AsNoTracking()
                    .Where(m => m.UserId == userId.Value)
                    .OrderBy(m => m.CreatedAt)
                    .Take(limit)
                    .Select(m => new { m.Role, m.Message, m.CreatedAt })
                    .ToListAsync();

                return Ok(messages);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Lỗi khi lấy lịch sử chat cho user {UserId}", GetCurrentUserId());
                // Trả về empty array thay vì lỗi để không làm gián đoạn UI
                return Ok(new object[0]);
            }
        }

        // POST: /api/chat/save - Lưu một message vào database (được gọi từ Ask endpoint, nhưng có thể dùng riêng)
        [HttpPost("save")]
        public async Task<IActionResult> SaveMessage([FromBody] SaveMessageRequest request)
        {
            try
            {
                if (request == null || string.IsNullOrWhiteSpace(request.Message))
                    return BadRequest(new { error = "Nội dung tin nhắn không được để trống." });

                var userId = GetCurrentUserId();
                if (!userId.HasValue)
                    return Unauthorized(new { error = "Người dùng chưa đăng nhập." });

                await SaveMessageAsync(userId.Value, request.Role ?? "user", request.Message);

                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Lỗi khi lưu message vào database cho user {UserId}", GetCurrentUserId());
                return StatusCode(500, new { error = "Không thể lưu tin nhắn. Vui lòng thử lại sau." });
            }
        }

        // DELETE: /api/chat/clear - Xóa lịch sử chat của user
        [HttpDelete("clear")]
        public async Task<IActionResult> ClearHistory()
        {
            try
            {
                var userId = GetCurrentUserId();
                if (!userId.HasValue)
                    return Unauthorized(new { error = "Người dùng chưa đăng nhập." });

                var messages = await _db.ChatMessages
                    .Where(m => m.UserId == userId.Value)
                    .ToListAsync();

                if (messages.Any())
                {
                    _db.ChatMessages.RemoveRange(messages);
                    await _db.SaveChangesAsync();
                }

                return Ok(new { success = true, message = "Đã xóa lịch sử chat." });
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Lỗi khi xóa lịch sử chat cho user {UserId}", GetCurrentUserId());
                return StatusCode(500, new { error = "Không thể xóa lịch sử chat. Vui lòng thử lại sau." });
            }
        }

        private async Task SaveMessageAsync(int userId, string role, string message)
        {
            if (userId <= 0)
                throw new ArgumentException("UserId phải lớn hơn 0", nameof(userId));

            if (string.IsNullOrWhiteSpace(message))
                throw new ArgumentException("Message không được để trống", nameof(message));

            // Giới hạn độ dài message
            if (message.Length > 5000)
            {
                message = message.Substring(0, 5000);
            }

            var chatMessage = new ChatMessage
            {
                UserId = userId,
                Role = role.ToLowerInvariant() == "bot" ? "bot" : "user",
                Message = message,
                CreatedAt = DateTime.UtcNow
            };

            try
            {
                _db.ChatMessages.Add(chatMessage);
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException dbEx)
            {
                _logger?.LogError(dbEx, "Lỗi database khi lưu chat message cho user {UserId}", userId);
                throw; // Re-throw để caller xử lý
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Lỗi không mong đợi khi lưu chat message cho user {UserId}", userId);
                throw; // Re-throw để caller xử lý
            }
        }

        [HttpPost("generate-report")]
        public async Task<IActionResult> GenerateReport([FromBody] GenerateReportRequest request)
        {
            if (request == null)
                return BadRequest(new { error = "Yêu cầu không hợp lệ." });

            try
            {
                var fromDate = !string.IsNullOrWhiteSpace(request.FromDate) ? DateTime.Parse(request.FromDate) : (DateTime?)null;
                var toDate = !string.IsNullOrWhiteSpace(request.ToDate) ? DateTime.Parse(request.ToDate) : (DateTime?)null;

                var reportType = request.ReportType.ToLowerInvariant();
                ReportData? reportData = null;

                if (reportType == "stock" || reportType == "tồn kho")
                {
                    reportData = await _reportService.GenerateStockReportAsync(fromDate, toDate, request.WarehouseId);
                }
                else if (reportType == "receipt" || reportType == "nhập kho" || reportType == "nhập")
                {
                    reportData = await _reportService.GenerateReceiptReportAsync(fromDate, toDate, request.WarehouseId);
                }
                else if (reportType == "issue" || reportType == "xuất kho" || reportType == "xuất")
                {
                    reportData = await _reportService.GenerateIssueReportAsync(fromDate, toDate, request.WarehouseId);
                }
                else if (reportType == "revenue" || reportType == "doanh thu")
                {
                    var groupBy = request.GroupBy ?? "day";
                    reportData = await _reportService.GenerateRevenueReportAsync(fromDate, toDate, request.WarehouseId, groupBy);
                }
                else if (reportType == "profitloss" || reportType == "profit" || reportType == "lợi nhuận" || reportType == "p&l")
                {
                    var groupBy = request.GroupBy ?? "day";
                    reportData = await _reportService.GenerateProfitLossReportAsync(fromDate, toDate, request.WarehouseId, groupBy);
                }
                else if (reportType == "turnoverrate" || reportType == "turnover" || reportType == "quay vòng" || reportType == "tỷ lệ quay vòng")
                {
                    reportData = await _reportService.GenerateTurnoverRateReportAsync(fromDate, toDate, request.WarehouseId);
                }

                if (reportData == null)
                    return BadRequest(new { error = "Loại báo cáo không hợp lệ." });

                return Ok(reportData);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = $"Lỗi khi tạo báo cáo: {ex.Message}" });
            }
        }

        private static (DateTime Start, DateTime End, string Label) ParseTimeExpression(string message)
        {
            var now = DateTime.Now;
            var today = now.Date;
            var text = message.ToLowerInvariant();

            // Khoảng thời gian: "từ ... đến ..."
            var fromMatch = Regex.Match(text, @"từ\s+(\d{1,2})[/-](\d{1,2})(?:[/-](\d{2,4}))?");
            var toMatch = Regex.Match(text, @"đến\s+(\d{1,2})[/-](\d{1,2})(?:[/-](\d{2,4}))?");
            if (fromMatch.Success && toMatch.Success)
            {
                var fromDay = int.Parse(fromMatch.Groups[1].Value);
                var fromMonth = int.Parse(fromMatch.Groups[2].Value);
                var fromYearStr = fromMatch.Groups[3].Value;
                var fromYear = string.IsNullOrEmpty(fromYearStr) ? now.Year : 
                    fromYearStr.Length == 2 ? 2000 + int.Parse(fromYearStr) : int.Parse(fromYearStr);

                var toDay = int.Parse(toMatch.Groups[1].Value);
                var toMonth = int.Parse(toMatch.Groups[2].Value);
                var toYearStr = toMatch.Groups[3].Value;
                var toYear = string.IsNullOrEmpty(toYearStr) ? now.Year : 
                    toYearStr.Length == 2 ? 2000 + int.Parse(toYearStr) : int.Parse(toYearStr);

                var start = new DateTime(fromYear, fromMonth, fromDay);
                var end = new DateTime(toYear, toMonth, toDay).Date.AddDays(1).AddTicks(-1);
                var label = $"từ {fromDay:D2}/{fromMonth:D2}/{fromYear} đến {toDay:D2}/{toMonth:D2}/{toYear}";
                return (start, end, label);
            }

            // "từ hôm qua đến hôm nay"
            if (text.Contains("từ hôm qua") && text.Contains("đến hôm nay"))
            {
                var start = today.AddDays(-1);
                var end = today.Date.AddDays(1).AddTicks(-1);
                return (start, end, "từ hôm qua đến hôm nay");
            }

            // Ngày cụ thể: "01/01/2025" hoặc "1-1-2025"
            var dateMatch = Regex.Match(text, @"(\d{1,2})[/-](\d{1,2})(?:[/-](\d{2,4}))?");
            if (dateMatch.Success && !text.Contains("từ") && !text.Contains("đến"))
            {
                var day = int.Parse(dateMatch.Groups[1].Value);
                var month = int.Parse(dateMatch.Groups[2].Value);
                var yearStr = dateMatch.Groups[3].Value;
                var year = string.IsNullOrEmpty(yearStr) ? now.Year : 
                    yearStr.Length == 2 ? 2000 + int.Parse(yearStr) : int.Parse(yearStr);

                var start = new DateTime(year, month, day);
                var end = start.Date.AddDays(1).AddTicks(-1);
                var label = $"{day:D2}/{month:D2}/{year}";
                return (start, end, label);
            }

            // Hôm nay
            if (text.Contains("hôm nay") || text.Contains("giờ") || text.Contains("bây giờ"))
            {
                var start = today;
                var end = today.Date.AddDays(1).AddTicks(-1);
                return (start, end, "hôm nay");
            }

            // Hôm qua
            if (text.Contains("hôm qua"))
            {
                var start = today.AddDays(-1);
                var end = today.Date.AddTicks(-1);
                return (start, end, "hôm qua");
            }

            // Ngày mai
            if (text.Contains("ngày mai"))
            {
                var start = today.AddDays(1);
                var end = today.AddDays(2).Date.AddTicks(-1);
                return (start, end, "ngày mai");
            }

            // Tuần này
            if (text.Contains("tuần này"))
            {
                var daysUntilMonday = ((int)today.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
                var start = today.AddDays(-daysUntilMonday);
                var end = today.Date.AddDays(1).AddTicks(-1);
                return (start, end, "tuần này");
            }

            // Tuần trước
            if (text.Contains("tuần trước"))
            {
                var daysUntilMonday = ((int)today.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
                var endOfLastWeek = today.AddDays(-daysUntilMonday - 1);
                var startOfLastWeek = endOfLastWeek.AddDays(-6);
                var end = endOfLastWeek.Date.AddDays(1).AddTicks(-1);
                return (startOfLastWeek, end, "tuần trước");
            }

            // Tuần sau
            if (text.Contains("tuần sau"))
            {
                var daysUntilMonday = ((int)today.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
                var daysToNextMonday = 7 - daysUntilMonday;
                var start = today.AddDays(daysToNextMonday);
                var end = start.AddDays(6).Date.AddDays(1).AddTicks(-1);
                return (start, end, "tuần sau");
            }

            // X tuần trước/sau
            var weekMatch = Regex.Match(text, @"(\d+)\s*tuần\s*(trước|sau)");
            if (weekMatch.Success)
            {
                var weeks = int.Parse(weekMatch.Groups[1].Value);
                var isBefore = weekMatch.Groups[2].Value == "trước";
                var daysUntilMonday = ((int)today.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
                
                if (isBefore)
                {
                    var endOfWeek = today.AddDays(-daysUntilMonday - (weeks - 1) * 7 - 1);
                    var start = endOfWeek.AddDays(-6);
                    var end = endOfWeek.Date.AddDays(1).AddTicks(-1);
                    return (start, end, $"{weeks} tuần trước");
                }
                else
                {
                    var daysToNextMonday = 7 - daysUntilMonday;
                    var start = today.AddDays(daysToNextMonday + (weeks - 1) * 7);
                    var end = start.AddDays(6).Date.AddDays(1).AddTicks(-1);
                    return (start, end, $"{weeks} tuần sau");
                }
            }

            // Tháng này
            if (text.Contains("tháng này"))
            {
                var start = new DateTime(now.Year, now.Month, 1);
                var end = today.Date.AddDays(1).AddTicks(-1);
                return (start, end, "tháng này");
            }

            // Tháng trước
            if (text.Contains("tháng trước"))
            {
                var lastMonth = now.AddMonths(-1);
                var start = new DateTime(lastMonth.Year, lastMonth.Month, 1);
                var end = start.AddMonths(1).AddTicks(-1);
                return (start, end, "tháng trước");
            }

            // Tháng sau
            if (text.Contains("tháng sau"))
            {
                var nextMonth = now.AddMonths(1);
                var start = new DateTime(nextMonth.Year, nextMonth.Month, 1);
                var end = start.AddMonths(1).AddTicks(-1);
                return (start, end, "tháng sau");
            }

            // Tháng X
            var monthMatch = Regex.Match(text, @"tháng\s*(\d{1,2})");
            if (monthMatch.Success && !text.Contains("này") && !text.Contains("trước") && !text.Contains("sau"))
            {
                var month = int.Parse(monthMatch.Groups[1].Value);
                var monthYearMatch = Regex.Match(text, "(20\\d{2})");
                var year = monthYearMatch.Success && int.TryParse(monthYearMatch.Value, out var y) ? y : now.Year;
                
                var start = new DateTime(year, month, 1);
                var end = start.AddMonths(1).AddTicks(-1);
                var label = $"tháng {month:D2}/{year}";
                return (start, end, label);
            }

            // X tháng trước/sau
            var monthAgoMatch = Regex.Match(text, @"(\d+)\s*tháng\s*(trước|sau)");
            if (monthAgoMatch.Success)
            {
                var months = int.Parse(monthAgoMatch.Groups[1].Value);
                var isBefore = monthAgoMatch.Groups[2].Value == "trước";
                var targetDate = isBefore ? now.AddMonths(-months) : now.AddMonths(months);
                var start = new DateTime(targetDate.Year, targetDate.Month, 1);
                var end = start.AddMonths(1).AddTicks(-1);
                return (start, end, $"{months} tháng {(isBefore ? "trước" : "sau")}");
            }

            // Năm này
            if (text.Contains("năm này"))
            {
                var start = new DateTime(now.Year, 1, 1);
                var end = today.Date.AddDays(1).AddTicks(-1);
                return (start, end, "năm này");
            }

            // Năm trước
            if (text.Contains("năm trước"))
            {
                var start = new DateTime(now.Year - 1, 1, 1);
                var end = new DateTime(now.Year - 1, 12, 31).Date.AddDays(1).AddTicks(-1);
                return (start, end, "năm trước");
            }

            // Năm X
            var yearMatch = Regex.Match(text, @"năm\s*(20\d{2})");
            if (yearMatch.Success && !text.Contains("này") && !text.Contains("trước"))
            {
                var year = int.Parse(yearMatch.Groups[1].Value);
                var start = new DateTime(year, 1, 1);
                var end = new DateTime(year, 12, 31).Date.AddDays(1).AddTicks(-1);
                return (start, end, $"năm {year}");
            }

            // X ngày trước/sau
            var dayAgoMatch = Regex.Match(text, @"(\d+)\s*ngày\s*(trước|sau|qua)");
            if (dayAgoMatch.Success)
            {
                var days = int.Parse(dayAgoMatch.Groups[1].Value);
                var isBefore = dayAgoMatch.Groups[2].Value != "sau";
                var targetDate = isBefore ? today.AddDays(-days) : today.AddDays(days);
                var start = targetDate;
                var end = targetDate.Date.AddDays(1).AddTicks(-1);
                return (start, end, $"{days} ngày {(isBefore ? "trước" : "sau")}");
            }

            // X năm trước/sau
            var yearAgoMatch = Regex.Match(text, @"(\d+)\s*năm\s*(trước|sau)");
            if (yearAgoMatch.Success)
            {
                var years = int.Parse(yearAgoMatch.Groups[1].Value);
                var isBefore = yearAgoMatch.Groups[2].Value == "trước";
                var year = isBefore ? now.Year - years : now.Year + years;
                var start = new DateTime(year, 1, 1);
                var end = new DateTime(year, 12, 31).Date.AddDays(1).AddTicks(-1);
                return (start, end, $"{years} năm {(isBefore ? "trước" : "sau")}");
            }

            // Gần đây, gần đây nhất, mới nhất - mặc định 3 ngày gần đây
            if (text.Contains("gần đây") || text.Contains("gần đây nhất") || text.Contains("mới nhất"))
            {
                var start = today.AddDays(-3);
                var end = today.Date.AddDays(1).AddTicks(-1);
                return (start, end, "3 ngày gần đây");
            }

            // Mặc định: tháng này (fallback)
            var defaultStart = new DateTime(now.Year, now.Month, 1);
            var defaultEnd = today.Date.AddDays(1).AddTicks(-1);
            return (defaultStart, defaultEnd, "tháng này");
        }

        private async Task<ReportData?> GenerateReportFromMessage(string message)
        {
            var text = message.ToLowerInvariant();
            
            // Parse thời gian sử dụng ParseTimeExpression
            var (startDate, endDate, _) = ParseTimeExpression(message);
            DateTime? fromDate = startDate;
            DateTime? toDate = endDate;

            string? groupBy = "day";
            if (text.Contains("theo năm") || text.Contains("theo tháng") || text.Contains("theo ngày"))
            {
                if (text.Contains("theo năm")) groupBy = "year";
                else if (text.Contains("theo tháng")) groupBy = "month";
                else if (text.Contains("theo ngày")) groupBy = "day";
            }

            ReportData? reportData = null;

            // Kiểm tra ưu tiên: "doanh thu" trước "xuất kho" vì "xuất báo cáo doanh thu" chứa cả "xuất" và "doanh thu"
            if (text.Contains("doanh thu") || text.Contains("revenue"))
            {
                // Kiểm tra nếu message yêu cầu chi tiết (có "chi tiết", "mã phiếu", "số phiếu", "chi tiết doanh thu")
                bool isDetail = text.Contains("chi tiết") || text.Contains("mã phiếu") || text.Contains("số phiếu") || text.Contains("chi tiết doanh thu");
                reportData = await _reportService.GenerateRevenueReportAsync(fromDate, toDate, null, groupBy, isDetail);
            }
            else if (text.Contains("tồn kho"))
            {
                // Parse warehouse name từ message nếu có
                int? warehouseId = null;
                var warehouses = await _db.Warehouses
                    .AsNoTracking()
                    .ToListAsync();
                
                foreach (var warehouse in warehouses)
                {
                    if (text.Contains(warehouse.Name.ToLowerInvariant()))
                    {
                        warehouseId = warehouse.Id;
                        break;
                    }
                }
                
                // Nếu không tìm thấy warehouse cụ thể hoặc có "tất cả kho", dùng null (tất cả kho)
                if (text.Contains("tất cả kho") || text.Contains("tất cả"))
                {
                    warehouseId = null;
                }
                
                reportData = await _reportService.GenerateStockReportAsync(fromDate, toDate, warehouseId);
            }
            else if (text.Contains("nhập kho") || text.Contains("nhập"))
            {
                reportData = await _reportService.GenerateReceiptReportAsync(fromDate, toDate);
            }
            else if (text.Contains("xuất kho") || (text.Contains("xuất") && !text.Contains("doanh thu") && !text.Contains("revenue")))
            {
                // Chỉ match "xuất kho" nếu không có "doanh thu" trong message
                // Vì "xuất báo cáo doanh thu" chứa cả "xuất" nhưng là báo cáo doanh thu, không phải xuất kho
                reportData = await _reportService.GenerateIssueReportAsync(fromDate, toDate);
            }
            else if (text.Contains("lợi nhuận") || text.Contains("profit") || text.Contains("p&l"))
            {
                reportData = await _reportService.GenerateProfitLossReportAsync(fromDate, toDate, null, groupBy);
            }
            else if (text.Contains("quay vòng") || text.Contains("turnover") || text.Contains("tỷ lệ quay vòng"))
            {
                reportData = await _reportService.GenerateTurnoverRateReportAsync(fromDate, toDate);
            }

            // Kiểm tra nếu không có dữ liệu, return null để không hiển thị bảng tải báo cáo
            if (reportData != null && (reportData.Rows == null || reportData.Rows.Count == 0))
            {
                return null;
            }

            return reportData;
        }

        public class GenerateReportRequest
        {
            public string ReportType { get; set; } = "";
            public string? FromDate { get; set; }
            public string? ToDate { get; set; }
            public int? WarehouseId { get; set; }
            public string? GroupBy { get; set; }
        }

        public class SaveMessageRequest
        {
            public string Role { get; set; } = "user";
            public string Message { get; set; } = "";
        }
    }
}

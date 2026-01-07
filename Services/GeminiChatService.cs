using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MNBEMART.Data;
using MNBEMART.Models;

namespace MNBEMART.Services
{
    public class GeminiChatService : IChatService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<GeminiChatService> _logger;
        private readonly AppDbContext _db;

        private record GeminiRequestContentPart(string Text);
        private record GeminiRequestContent(GeminiRequestContentPart[] Parts);
        private record GeminiRequest(GeminiRequestContent[] Contents);

        private record GeminiResponsePart(string? Text);
        private record GeminiResponseContent(GeminiResponsePart[]? Parts);
        private record GeminiResponseCandidate(GeminiResponseContent? Content);
        private record GeminiResponse(GeminiResponseCandidate[]? Candidates);

        private record GoogleError(string? Message, string? Status);
        private record GoogleErrorWrapper(GoogleError? Error);

        private enum ChatIntent
        {
            SmallTalk,
            TotalStockByWarehouse,
            ReceiptInMonth,
            ExpiringSoon,
            RevenueInMonth,
            ExpiredProducts,
            LowStock,
            GenerateReport,
            WarehouseEfficiency,
            ProcessImprovement,
            SqlQuery,
            WebQuestion,
            SystemInfo,
            DocumentDetail,
            Unknown
        }

        public GeminiChatService(
            HttpClient httpClient,
            IConfiguration configuration,
            ILogger<GeminiChatService> logger,
            AppDbContext db)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
            _db = db;
        }

        private ChatIntent DetectIntent(string message)
        {
            var text = message.ToLowerInvariant();

            // Chào hỏi, giới thiệu
            if (text.Contains("chào") || text.Contains("bạn là ai") || text.Contains("hướng dẫn"))
                return ChatIntent.SmallTalk;

            // Tổng tồn kho của từng kho
            if (text.Contains("tổng tồn kho") || (text.Contains("tồn kho") && text.Contains("từng kho")))
                return ChatIntent.TotalStockByWarehouse;

            // Hỏi về nhập kho trong 1 tháng cụ thể
            if (text.Contains("nhập kho") && text.Contains("tháng"))
                return ChatIntent.ReceiptInMonth;

            // Doanh thu - phát hiện với bất kỳ biểu thức thời gian nào - ƯU TIÊN TRƯỚC GenerateReport
            // Kiểm tra doanh thu trước để tránh match vào GenerateReport khi có "xuất báo cáo doanh thu"
            if (text.Contains("doanh thu") || text.Contains("revenue"))
            {
                // Nếu có "doanh thu" thì luôn return RevenueInMonth, không cần check thời gian
                return ChatIntent.RevenueInMonth;
            }

            // Hàng sắp hết hạn
            if (text.Contains("sắp hết hạn") || (text.Contains("hết hạn") && text.Contains("sắp")))
                return ChatIntent.ExpiringSoon;

            // Hàng đã hết hạn / quá hạn
            if (text.Contains("quá hạn") || (text.Contains("hết hạn") && !text.Contains("sắp")))
                return ChatIntent.ExpiredProducts;

            // Tồn kho thấp / sắp hết hàng
            if (text.Contains("tồn kho thấp") || text.Contains("sắp hết hàng") || text.Contains("gần hết hàng") || text.Contains("tồn ít"))
                return ChatIntent.LowStock;

            // Tạo báo cáo - chỉ match khi KHÔNG có "doanh thu" (vì đã check ở trên)
            if (text.Contains("tạo báo cáo") || text.Contains("xuất báo cáo") || 
                (text.Contains("báo cáo") && (text.Contains("pdf") || text.Contains("excel") || text.Contains("file"))) ||
                (text.Contains("báo cáo") && (text.Contains("tồn kho") || text.Contains("nhập kho") || text.Contains("xuất kho") ||
                 text.Contains("lợi nhuận") || text.Contains("quay vòng"))))
                return ChatIntent.GenerateReport;

            // Hiệu quả kho
            if (text.Contains("hiệu quả kho") || text.Contains("hiệu suất kho") || text.Contains("turnover") || text.Contains("tỷ lệ quay vòng"))
                return ChatIntent.WarehouseEfficiency;

            // Cải thiện quy trình
            if (text.Contains("cải thiện") || text.Contains("tối ưu") || text.Contains("quy trình") || text.Contains("bottleneck") || text.Contains("nghẽn"))
                return ChatIntent.ProcessImprovement;

            // SQL query questions
            if (text.Contains("truy vấn sql") || text.Contains("query sql") || text.Contains("select from") || 
                text.Contains("sql query") || text.Contains("chạy sql") || text.Contains("thực thi sql") ||
                text.Contains("câu lệnh sql") || (text.Contains("select") && (text.Contains("from") || text.Contains("where"))))
                return ChatIntent.SqlQuery;

            // Web application questions
            if (text.Contains("cách sử dụng") || text.Contains("tính năng") || text.Contains("web này") || 
                text.Contains("hệ thống này") || text.Contains("bemart") || text.Contains("làm gì") ||
                text.Contains("chức năng") || text.Contains("hướng dẫn sử dụng") || text.Contains("sử dụng như thế nào"))
                return ChatIntent.WebQuestion;

            // System information questions
            if (text.Contains("thông tin hệ thống") || text.Contains("phiên bản") || text.Contains("cấu hình") ||
                text.Contains("thông số") || text.Contains("thông tin về hệ thống") || text.Contains("hệ thống có gì"))
                return ChatIntent.SystemInfo;

            // Document/Entity detail questions - check for specific document codes or questions about entities
            // Pattern: "phiếu PX...", "phiếu PN...", "vật tư mã N...", "người tạo phiếu...", "ai tạo phiếu..."
            var pxMatch = Regex.Match(text, @"px\d+-\d+", RegexOptions.IgnoreCase);
            var pnMatch = Regex.Match(text, @"pn\d+-\d+", RegexOptions.IgnoreCase);
            var materialCodeMatch = Regex.Match(text, @"\b[mn]\d+\b", RegexOptions.IgnoreCase);
            
            if (pxMatch.Success || pnMatch.Success || materialCodeMatch.Success ||
                (text.Contains("phiếu") && (text.Contains("px") || text.Contains("pn") || text.Contains("số phiếu"))) ||
                (text.Contains("vật tư") && text.Contains("mã")) ||
                (text.Contains("nguyên liệu") && text.Contains("mã")) ||
                (text.Contains("người tạo") && text.Contains("phiếu")) ||
                (text.Contains("ai tạo") && text.Contains("phiếu")) ||
                (text.Contains("thông tin") && (text.Contains("phiếu") || text.Contains("vật tư"))) ||
                (text.Contains("chi tiết") && (text.Contains("phiếu") || text.Contains("vật tư"))))
            {
                return ChatIntent.DocumentDetail;
            }

            return ChatIntent.Unknown;
        }

        private async Task<string> BuildTotalStockByWarehouseContextAsync(CancellationToken ct)
        {
            var data = await _db.Stocks
                .Include(s => s.Warehouse)
                .GroupBy(s => new { s.WarehouseId, s.Warehouse.Name })
                .Select(g => new
                {
                    WarehouseId = g.Key.WarehouseId,
                    WarehouseName = g.Key.Name,
                    TotalQty = g.Sum(x => x.Quantity)
                })
                .OrderBy(x => x.WarehouseName)
                .ToListAsync(ct);

            if (!data.Any())
                return "Hiện hệ thống không có bản ghi tồn kho nào trong bảng Stocks.";

            var sb = new StringBuilder();
            sb.AppendLine("BÁO CÁO TỔNG TỒN KHO THEO TỪNG KHO (tính từ bảng Stocks)");
            sb.AppendLine("ID kho | Tên kho | Tổng số lượng tồn");

            foreach (var x in data)
            {
                var qtyText = x.TotalQty.ToString("0.###", CultureInfo.InvariantCulture);
                sb.AppendLine($"{x.WarehouseId} | {x.WarehouseName} | {qtyText}");
            }

            return sb.ToString();
        }

        private static (DateTime Start, DateTime End, string Label) ParseMonthFromMessage(string message)
        {
            var now = DateTime.Now;
            var text = message.ToLowerInvariant();

            // Tìm "tháng xx" trong câu hỏi
            var matchMonth = Regex.Match(text, "tháng\\s*(\\d{1,2})");
            int month = now.Month;
            if (matchMonth.Success && int.TryParse(matchMonth.Groups[1].Value, out var m) && m is >= 1 and <= 12)
            {
                month = m;
            }

            // Tìm năm nếu có (vd: 2025)
            var matchYear = Regex.Match(text, "(20\\d{2})");
            int year = matchYear.Success && int.TryParse(matchYear.Value, out var y) ? y : now.Year;

            var start = new DateTime(year, month, 1);
            var end = start.AddMonths(1).AddTicks(-1);
            var label = $"tháng {month:D2}/{year}";
            return (start, end, label);
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

            // Mặc định: tháng này (fallback để tương thích với code cũ)
            var defaultStart = new DateTime(now.Year, now.Month, 1);
            var defaultEnd = today.Date.AddDays(1).AddTicks(-1);
            return (defaultStart, defaultEnd, "tháng này");
        }

        private async Task<string> BuildReceiptInMonthContextAsync(string message, CancellationToken ct)
        {
            var (start, end, label) = ParseMonthFromMessage(message);

            var query = from h in _db.StockReceipts
                        where h.CreatedAt >= start && h.CreatedAt <= end
                        from d in h.Details
                        group new { h, d } by new { h.WarehouseId, h.Warehouse.Name } into g
                        select new
                        {
                            WarehouseId = g.Key.WarehouseId,
                            WarehouseName = g.Key.Name,
                            TotalQty = g.Sum(x => x.d.Quantity),
                            TotalValue = g.Sum(x => (decimal)x.d.Quantity * x.d.UnitPrice)
                        };

            var data = await query.OrderBy(x => x.WarehouseName).ToListAsync(ct);

            if (!data.Any())
                return $"Không tìm thấy phiếu nhập nào trong {label}.";

            var grandQty = data.Sum(x => x.TotalQty);
            var grandValue = data.Sum(x => x.TotalValue);

            var sb = new StringBuilder();
            sb.AppendLine($"BÁO CÁO NHẬP KHO THEO KHO TRONG {label}");
            sb.AppendLine("ID kho | Tên kho | Tổng số lượng nhập | Tổng giá trị nhập");

            foreach (var x in data)
            {
                var qtyText = x.TotalQty.ToString("0.###", CultureInfo.InvariantCulture);
                sb.AppendLine($"{x.WarehouseId} | {x.WarehouseName} | {qtyText} | {x.TotalValue:N0}");
            }

            var grandQtyText = grandQty.ToString("0.###", CultureInfo.InvariantCulture);

            sb.AppendLine();
            sb.AppendLine($"Tổng nhập toàn hệ thống: {grandQtyText} (giá trị: {grandValue:N0}).");

            return sb.ToString();
        }

        private async Task<string> BuildExpiringSoonContextAsync(CancellationToken ct)
        {
            var today = DateTime.Today;
            var threshold = today.AddDays(30); // định nghĩa "sắp hết hạn" = trong 30 ngày tới

            var lots = await _db.StockLots
                .Include(l => l.Warehouse)
                .Include(l => l.Material)
                .Where(l => l.ExpiryDate != null && l.ExpiryDate >= today && l.ExpiryDate <= threshold && l.Quantity > 0)
                .OrderBy(l => l.ExpiryDate)
                .ThenBy(l => l.Warehouse.Name)
                .ThenBy(l => l.Material.Code)
                .Take(100)
                .ToListAsync(ct);

            if (!lots.Any())
                return "Trong 30 ngày tới không có lô hàng nào sắp hết hạn (theo bảng StockLots).";

            var sb = new StringBuilder();
            sb.AppendLine("CÁC LÔ HÀNG SẮP HẾT HẠN TRONG 30 NGÀY TỚI (tối đa 100 dòng)");
            sb.AppendLine("Kho | Mã vật tư | Tên vật tư | Số lô | NSX | HSD | Số lượng tồn");

            foreach (var l in lots)
            {
                var qtyText = l.Quantity.ToString("0.###", CultureInfo.InvariantCulture);
                sb.AppendLine($"{l.Warehouse.Name} | {l.Material.Code} | {l.Material.Name} | {l.LotNumber} | " +
                              $"{l.ManufactureDate:dd/MM/yyyy} | {l.ExpiryDate:dd/MM/yyyy} | {qtyText}");
            }

            return sb.ToString();
        }

        private async Task<string> BuildRevenueContextAsync(string message, CancellationToken ct)
        {
            var (start, end, label) = ParseTimeExpression(message);

            // Query StockIssueDetails for revenue (selling price) - Revenue should use UnitPrice * Quantity
            var issueDetailsQuery = _db.StockIssueDetails
                .Include(d => d.StockIssue)
                    .ThenInclude(i => i.Warehouse)
                .AsNoTracking()
                .Where(d => d.StockIssue.Status == DocumentStatus.DaXuatHang
                         && d.StockIssue.CreatedAt.Date >= start.Date
                         && d.StockIssue.CreatedAt.Date <= end.Date);

            var data = await issueDetailsQuery
                .GroupBy(d => new { d.StockIssue.WarehouseId, d.StockIssue.Warehouse.Name })
                .Select(g => new
                {
                    WarehouseId = g.Key.WarehouseId,
                    WarehouseName = g.Key.Name,
                    Revenue = g.Sum(x => (decimal)x.Quantity * x.UnitPrice),
                    QtyOut = g.Sum(x => x.Quantity)
                })
                .OrderBy(x => x.WarehouseName)
                .ToListAsync(ct);

            if (!data.Any())
                return $"Không có dữ liệu doanh thu trong {label}.";

            var totalRevenue = data.Sum(x => x.Revenue);
            var totalQty = data.Sum(x => x.QtyOut);

            var sb = new StringBuilder();
            sb.AppendLine($"DOANH THU XUẤT KHO TRONG {label.ToUpper()} (tổng hợp từ StockIssueDetails)");
            sb.AppendLine("ID kho | Tên kho | Doanh thu | Số lượng xuất");

            foreach (var x in data)
            {
                var qtyText = x.QtyOut.ToString("0.###", CultureInfo.InvariantCulture);
                sb.AppendLine($"{x.WarehouseId} | {x.WarehouseName} | {x.Revenue:N0} | {qtyText}");
            }

            sb.AppendLine();
            sb.AppendLine($"Tổng doanh thu toàn hệ thống: {totalRevenue:N0}, tổng số lượng xuất: {totalQty:0.###}.");

            return sb.ToString();
        }

        // Giữ lại hàm cũ để tương thích với code khác có thể đang dùng
        private async Task<string> BuildRevenueInMonthContextAsync(string message, CancellationToken ct)
        {
            return await BuildRevenueContextAsync(message, ct);
        }

        private static (string? DocumentType, string? Code) ParseDocumentCode(string message)
        {
            var text = message.ToLowerInvariant();
            
            // Pattern: PX251202-0006 (phiếu xuất)
            var pxMatch = Regex.Match(text, @"(px\d+-\d+)", RegexOptions.IgnoreCase);
            if (pxMatch.Success)
            {
                return ("StockIssue", pxMatch.Groups[1].Value.ToUpper());
            }
            
            // Pattern: PN251202-0006 (phiếu nhập)
            var pnMatch = Regex.Match(text, @"(pn\d+-\d+)", RegexOptions.IgnoreCase);
            if (pnMatch.Success)
            {
                return ("StockReceipt", pnMatch.Groups[1].Value.ToUpper());
            }
            
            // Pattern: N108, M108 (mã vật tư)
            var materialMatch = Regex.Match(text, @"\b([mn]\d+)\b", RegexOptions.IgnoreCase);
            if (materialMatch.Success)
            {
                return ("Material", materialMatch.Groups[1].Value.ToUpper());
            }
            
            return (null, null);
        }

        private async Task<string> BuildStockIssueDetailContextAsync(string issueNumber, CancellationToken ct)
        {
            var issue = await _db.StockIssues
                .Include(i => i.CreatedBy)
                .Include(i => i.ApprovedBy)
                .Include(i => i.Warehouse)
                .Include(i => i.Details)
                    .ThenInclude(d => d.Material)
                .AsNoTracking()
                .FirstOrDefaultAsync(i => i.IssueNumber == issueNumber, ct);

            if (issue == null)
            {
                return $"Không tìm thấy phiếu xuất kho với số phiếu {issueNumber}.";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"THÔNG TIN CHI TIẾT PHIẾU XUẤT KHO: {issueNumber}");
            sb.AppendLine();
            sb.AppendLine($"Số phiếu: {issue.IssueNumber}");
            sb.AppendLine($"Ngày tạo: {issue.CreatedAt:dd/MM/yyyy HH:mm:ss}");
            sb.AppendLine($"Người tạo: {issue.CreatedBy?.FullName ?? "N/A"} (Username: {issue.CreatedBy?.Username ?? "N/A"})");
            sb.AppendLine($"Kho: {issue.Warehouse?.Name ?? "N/A"} (ID: {issue.WarehouseId})");
            sb.AppendLine($"Trạng thái: {issue.Status}");
            
            if (issue.ApprovedBy != null)
            {
                sb.AppendLine($"Người duyệt: {issue.ApprovedBy.FullName} (Username: {issue.ApprovedBy.Username})");
                if (issue.ApprovedAt.HasValue)
                {
                    sb.AppendLine($"Ngày duyệt: {issue.ApprovedAt.Value:dd/MM/yyyy HH:mm:ss}");
                }
            }
            
            if (!string.IsNullOrWhiteSpace(issue.ReceivedByName))
            {
                sb.AppendLine($"Người nhận: {issue.ReceivedByName}");
            }
            
            if (!string.IsNullOrWhiteSpace(issue.Note))
            {
                sb.AppendLine($"Ghi chú: {issue.Note}");
            }

            sb.AppendLine();
            sb.AppendLine("CHI TIẾT SẢN PHẨM:");
            sb.AppendLine("STT | Mã vật tư | Tên vật tư | Số lượng | Đơn vị | Đơn giá | Thành tiền");

            int stt = 1;
            decimal totalAmount = 0;
            foreach (var detail in issue.Details)
            {
                var amount = (decimal)detail.Quantity * detail.UnitPrice;
                totalAmount += amount;
                sb.AppendLine($"{stt} | {detail.Material?.Code ?? "N/A"} | {detail.Material?.Name ?? "N/A"} | " +
                             $"{detail.Quantity:0.###} | {detail.Unit ?? detail.Material?.Unit ?? "N/A"} | " +
                             $"{detail.UnitPrice:N0} | {amount:N0}");
                stt++;
            }

            sb.AppendLine();
            sb.AppendLine($"Tổng cộng: {issue.Details.Count} mặt hàng | Tổng thành tiền: {totalAmount:N0} đ");

            return sb.ToString();
        }

        private async Task<string> BuildStockReceiptDetailContextAsync(string receiptNumber, CancellationToken ct)
        {
            var receipt = await _db.StockReceipts
                .Include(r => r.CreatedBy)
                .Include(r => r.ApprovedBy)
                .Include(r => r.Warehouse)
                .Include(r => r.Details)
                    .ThenInclude(d => d.Material)
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.ReceiptNumber == receiptNumber, ct);

            if (receipt == null)
            {
                return $"Không tìm thấy phiếu nhập kho với số phiếu {receiptNumber}.";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"THÔNG TIN CHI TIẾT PHIẾU NHẬP KHO: {receiptNumber}");
            sb.AppendLine();
            sb.AppendLine($"Số phiếu: {receipt.ReceiptNumber ?? "N/A"}");
            sb.AppendLine($"Ngày tạo: {receipt.CreatedAt:dd/MM/yyyy HH:mm:ss}");
            sb.AppendLine($"Người tạo: {receipt.CreatedBy?.FullName ?? "N/A"} (Username: {receipt.CreatedBy?.Username ?? "N/A"})");
            sb.AppendLine($"Kho: {receipt.Warehouse?.Name ?? "N/A"} (ID: {receipt.WarehouseId})");
            sb.AppendLine($"Trạng thái: {receipt.Status}");
            
            if (receipt.ApprovedBy != null)
            {
                sb.AppendLine($"Người duyệt: {receipt.ApprovedBy.FullName} (Username: {receipt.ApprovedBy.Username})");
                if (receipt.ApprovedAt.HasValue)
                {
                    sb.AppendLine($"Ngày duyệt: {receipt.ApprovedAt.Value:dd/MM/yyyy HH:mm:ss}");
                }
            }
            
            if (!string.IsNullOrWhiteSpace(receipt.DeliveredByName))
            {
                sb.AppendLine($"Người giao: {receipt.DeliveredByName}");
            }
            
            if (!string.IsNullOrWhiteSpace(receipt.Note))
            {
                sb.AppendLine($"Ghi chú: {receipt.Note}");
            }

            sb.AppendLine();
            sb.AppendLine("CHI TIẾT SẢN PHẨM:");
            sb.AppendLine("STT | Mã vật tư | Tên vật tư | Số lượng | Đơn vị | Đơn giá | Thành tiền");

            int stt = 1;
            decimal totalAmount = 0;
            foreach (var detail in receipt.Details)
            {
                var amount = (decimal)detail.Quantity * detail.UnitPrice;
                totalAmount += amount;
                sb.AppendLine($"{stt} | {detail.Material?.Code ?? "N/A"} | {detail.Material?.Name ?? "N/A"} | " +
                             $"{detail.Quantity:0.###} | {detail.Unit ?? detail.Material?.Unit ?? "N/A"} | " +
                             $"{detail.UnitPrice:N0} | {amount:N0}");
                stt++;
            }

            sb.AppendLine();
            sb.AppendLine($"Tổng cộng: {receipt.Details.Count} mặt hàng | Tổng thành tiền: {totalAmount:N0} đ");

            return sb.ToString();
        }

        private async Task<string> BuildMaterialDetailContextAsync(string materialCode, CancellationToken ct)
        {
            var material = await _db.Materials
                .Include(m => m.Supplier)
                .Include(m => m.Warehouse)
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.Code == materialCode, ct);

            if (material == null)
            {
                return $"Không tìm thấy vật tư/nguyên liệu với mã {materialCode}.";
            }

            // Tính tồn kho hiện tại từ bảng Stocks
            var currentStock = await _db.Stocks
                .Include(s => s.Warehouse)
                .Where(s => s.MaterialId == material.Id)
                .AsNoTracking()
                .ToListAsync(ct);

            var sb = new StringBuilder();
            sb.AppendLine($"THÔNG TIN CHI TIẾT VẬT TƯ: {materialCode}");
            sb.AppendLine();
            sb.AppendLine($"Mã: {material.Code}");
            sb.AppendLine($"Tên: {material.Name}");
            sb.AppendLine($"Đơn vị: {material.Unit}");
            
            if (!string.IsNullOrWhiteSpace(material.Description))
            {
                sb.AppendLine($"Mô tả: {material.Description}");
            }
            
            if (!string.IsNullOrWhiteSpace(material.Specification))
            {
                sb.AppendLine($"Quy cách: {material.Specification}");
            }

            sb.AppendLine();
            sb.AppendLine("GIÁ CẢ:");
            if (material.PurchasePrice.HasValue)
            {
                sb.AppendLine($"Giá nhập: {material.PurchasePrice.Value:N0} đ");
            }
            if (material.SellingPrice.HasValue)
            {
                sb.AppendLine($"Giá bán: {material.SellingPrice.Value:N0} đ");
            }

            sb.AppendLine();
            sb.AppendLine("THÔNG TIN BỔ SUNG:");
            if (material.Supplier != null)
            {
                sb.AppendLine($"Nhà cung cấp: {material.Supplier.Name}");
            }
            if (material.Warehouse != null)
            {
                sb.AppendLine($"Kho: {material.Warehouse.Name}");
            }

            if (material.MinimumStock.HasValue || material.MaximumStock.HasValue)
            {
                sb.AppendLine();
                sb.AppendLine("CẢNH BÁO TỒN KHO:");
                if (material.MinimumStock.HasValue)
                {
                    sb.AppendLine($"Tồn tối thiểu: {material.MinimumStock.Value:0.###}");
                }
                if (material.MaximumStock.HasValue)
                {
                    sb.AppendLine($"Tồn tối đa: {material.MaximumStock.Value:0.###}");
                }
            }

            if (material.ManufactureDate.HasValue || material.ExpiryDate.HasValue)
            {
                sb.AppendLine();
                sb.AppendLine("THÔNG TIN HẠN SỬ DỤNG:");
                if (material.ManufactureDate.HasValue)
                {
                    sb.AppendLine($"Ngày sản xuất: {material.ManufactureDate.Value:dd/MM/yyyy}");
                }
                if (material.ExpiryDate.HasValue)
                {
                    sb.AppendLine($"Hạn sử dụng: {material.ExpiryDate.Value:dd/MM/yyyy}");
                }
            }

            if (currentStock.Any())
            {
                sb.AppendLine();
                sb.AppendLine("TỒN KHO HIỆN TẠI:");
                sb.AppendLine("Kho | Số lượng tồn");
                decimal totalStock = 0;
                foreach (var stock in currentStock)
                {
                    sb.AppendLine($"{stock.Warehouse?.Name ?? "N/A"} | {stock.Quantity:0.###}");
                    totalStock += (decimal)stock.Quantity;
                }
                sb.AppendLine($"TỔNG TỒN: {totalStock:0.###}");
            }
            else
            {
                sb.AppendLine();
                sb.AppendLine("TỒN KHO: 0 (chưa có tồn kho)");
            }

            return sb.ToString();
        }

        private async Task<string> BuildExpiredProductsContextAsync(CancellationToken ct)
        {
            var today = DateTime.Today;

            var lots = await _db.StockLots
                .Include(l => l.Warehouse)
                .Include(l => l.Material)
                .Where(l => l.ExpiryDate != null && l.ExpiryDate < today && l.Quantity > 0)
                .OrderBy(l => l.ExpiryDate)
                .ThenBy(l => l.Warehouse.Name)
                .ThenBy(l => l.Material.Code)
                .Take(100)
                .ToListAsync(ct);

            if (!lots.Any())
                return "Hiện tại không còn lô hàng nào đã hết hạn nhưng vẫn còn tồn (theo bảng StockLots).";

            var sb = new StringBuilder();
            sb.AppendLine("CÁC LÔ HÀNG ĐÃ HẾT HẠN NHƯNG VẪN CÒN TỒN (tối đa 100 dòng)");
            sb.AppendLine("Kho | Mã vật tư | Tên vật tư | Số lô | NSX | HSD | Số lượng tồn");

            foreach (var l in lots)
            {
                var qtyText = l.Quantity.ToString("0.###", CultureInfo.InvariantCulture);
                sb.AppendLine($"{l.Warehouse.Name} | {l.Material.Code} | {l.Material.Name} | {l.LotNumber} | " +
                              $"{l.ManufactureDate:dd/MM/yyyy} | {l.ExpiryDate:dd/MM/yyyy} | {qtyText}");
            }

            return sb.ToString();
        }

        private async Task<string> BuildLowStockContextAsync(CancellationToken ct)
        {
            const decimal threshold = 5m; // định nghĩa tồn kho thấp mặc định = <=5 đơn vị

            var items = await _db.Stocks
                .Include(s => s.Warehouse)
                .Include(s => s.Material)
                .Where(s => s.Quantity > 0 && s.Quantity <= threshold)
                .OrderBy(s => s.Quantity)
                .ThenBy(s => s.Warehouse.Name)
                .ThenBy(s => s.Material.Code)
                .Take(100)
                .ToListAsync(ct);

            if (!items.Any())
                return $"Không có mặt hàng nào có tồn kho thấp hơn hoặc bằng {threshold} (theo bảng Stocks).";

            var sb = new StringBuilder();
            sb.AppendLine($"CÁC MẶT HÀNG ĐANG CÓ TỒN KHO THẤP (<= {threshold})");
            sb.AppendLine("Kho | Mã vật tư | Tên vật tư | Số lượng tồn hiện tại");

            foreach (var s in items)
            {
                var qtyText = s.Quantity.ToString("0.###", CultureInfo.InvariantCulture);
                sb.AppendLine($"{s.Warehouse.Name} | {s.Material.Code} | {s.Material.Name} | {qtyText}");
            }

            return sb.ToString();
        }

        private async Task<string> BuildWarehouseEfficiencyContextAsync(CancellationToken ct)
        {
            var now = DateTime.Now;
            var lastMonth = now.AddMonths(-1);
            var start = new DateTime(lastMonth.Year, lastMonth.Month, 1);
            var end = start.AddMonths(1).AddTicks(-1);

            // Tính turnover rate và aging inventory
            var turnoverData = await _db.StockBalances
                .Include(b => b.Warehouse)
                .Where(b => b.Date >= start && b.Date <= end)
                .GroupBy(b => new { b.WarehouseId, b.Warehouse.Name })
                .Select(g => new
                {
                    WarehouseId = g.Key.WarehouseId,
                    WarehouseName = g.Key.Name,
                    TotalOutQty = g.Sum(x => x.OutQty),
                    TotalOutValue = g.Sum(x => x.OutValue),
                    AvgStock = g.Average(x => x.InQty - x.OutQty)
                })
                .ToListAsync(ct);

            var currentStock = await _db.Stocks
                .Include(s => s.Warehouse)
                .GroupBy(s => new { s.WarehouseId, s.Warehouse.Name })
                .Select(g => new
                {
                    WarehouseId = g.Key.WarehouseId,
                    WarehouseName = g.Key.Name,
                    TotalStock = g.Sum(x => x.Quantity)
                })
                .ToListAsync(ct);

            var sb = new StringBuilder();
            sb.AppendLine("PHÂN TÍCH HIỆU QUẢ KHO (tháng trước)");
            sb.AppendLine("ID kho | Tên kho | Tổng xuất (SL) | Tổng xuất (Giá trị) | Tồn trung bình | Tồn hiện tại");

            foreach (var wh in turnoverData)
            {
                var current = currentStock.FirstOrDefault(c => c.WarehouseId == wh.WarehouseId);
                var currentQty = current?.TotalStock ?? 0;
                var turnoverRate = wh.AvgStock > 0 ? (wh.TotalOutQty / wh.AvgStock) : 0;
                
                sb.AppendLine($"{wh.WarehouseId} | {wh.WarehouseName} | {wh.TotalOutQty:0.###} | {wh.TotalOutValue:N0} | {wh.AvgStock:0.###} | {currentQty:0.###}");
            }

            return sb.ToString();
        }

        private async Task<string> BuildProcessImprovementContextAsync(CancellationToken ct)
        {
            var now = DateTime.Now;
            var last3Months = now.AddMonths(-3);
            var start = new DateTime(last3Months.Year, last3Months.Month, 1);

            // Phân tích patterns nhập/xuất
            var receiptPatterns = await _db.StockReceipts
                .Where(r => r.CreatedAt >= start)
                .GroupBy(r => new { 
                    DayOfWeek = r.CreatedAt.DayOfWeek,
                    Hour = r.CreatedAt.Hour 
                })
                .Select(g => new
                {
                    DayOfWeek = g.Key.DayOfWeek,
                    Hour = g.Key.Hour,
                    Count = g.Count(),
                    AvgValue = g.Average(r => r.Details.Sum(d => (decimal)d.Quantity * d.UnitPrice))
                })
                .OrderByDescending(x => x.Count)
                .Take(10)
                .ToListAsync(ct);

            var issuePatterns = await _db.StockIssues
                .Where(i => i.CreatedAt >= start)
                .GroupBy(i => new { 
                    DayOfWeek = i.CreatedAt.DayOfWeek,
                    Hour = i.CreatedAt.Hour 
                })
                .Select(g => new
                {
                    DayOfWeek = g.Key.DayOfWeek,
                    Hour = g.Key.Hour,
                    Count = g.Count(),
                    AvgValue = g.Average(i => i.Details.Sum(d => (decimal)d.Quantity * d.UnitPrice))
                })
                .OrderByDescending(x => x.Count)
                .Take(10)
                .ToListAsync(ct);

            var sb = new StringBuilder();
            sb.AppendLine("PHÂN TÍCH PATTERNS NHẬP/XUẤT KHO (3 tháng gần đây)");
            sb.AppendLine();
            sb.AppendLine("TOP 10 THỜI ĐIỂM NHẬP KHO NHIỀU NHẤT:");
            sb.AppendLine("Thứ | Giờ | Số phiếu | Giá trị TB");

            foreach (var p in receiptPatterns)
            {
                sb.AppendLine($"{p.DayOfWeek} | {p.Hour}h | {p.Count} | {p.AvgValue:N0}");
            }

            sb.AppendLine();
            sb.AppendLine("TOP 10 THỜI ĐIỂM XUẤT KHO NHIỀU NHẤT:");
            sb.AppendLine("Thứ | Giờ | Số phiếu | Giá trị TB");

            foreach (var p in issuePatterns)
            {
                sb.AppendLine($"{p.DayOfWeek} | {p.Hour}h | {p.Count} | {p.AvgValue:N0}");
            }

            return sb.ToString();
        }

        private async Task<string> BuildReportGenerationContextAsync(string message, CancellationToken ct)
        {
            var text = message.ToLowerInvariant();
            var sb = new StringBuilder();
            sb.AppendLine("THÔNG TIN ĐỂ TẠO BÁO CÁO:");
            
            // Xác định loại báo cáo
            if (text.Contains("tồn kho"))
            {
                var stocks = await _db.Stocks
                    .Include(s => s.Warehouse)
                    .Include(s => s.Material)
                    .OrderBy(s => s.Warehouse.Name)
                    .ThenBy(s => s.Material.Code)
                    .Take(100)
                    .ToListAsync(ct);

                sb.AppendLine("LOẠI: Báo cáo tồn kho");
                sb.AppendLine("DỮ LIỆU (100 dòng đầu):");
                sb.AppendLine("Kho | Mã | Tên | Số lượng");

                foreach (var s in stocks)
                {
                    sb.AppendLine($"{s.Warehouse.Name} | {s.Material.Code} | {s.Material.Name} | {s.Quantity:0.###}");
                }
            }
            else if (text.Contains("nhập kho") || text.Contains("nhập"))
            {
                var (start, end, label) = ParseTimeExpression(message);
                var receipts = await _db.StockReceipts
                    .Include(r => r.Warehouse)
                    .Include(r => r.Details)
                    .ThenInclude(d => d.Material)
                    .Where(r => r.CreatedAt >= start && r.CreatedAt <= end)
                    .OrderBy(r => r.CreatedAt)
                    .Take(50)
                    .ToListAsync(ct);

                sb.AppendLine($"LOẠI: Báo cáo nhập kho {label}");
                sb.AppendLine("DỮ LIỆU (50 phiếu đầu):");
                sb.AppendLine("Ngày | Kho | Mã | Tên | SL | Đơn giá | Thành tiền");

                foreach (var r in receipts)
                {
                    foreach (var d in r.Details.Take(5))
                    {
                        sb.AppendLine($"{r.CreatedAt:dd/MM/yyyy} | {r.Warehouse.Name} | {d.Material.Code} | {d.Material.Name} | {d.Quantity} | {d.UnitPrice:N0} | {(decimal)d.Quantity * d.UnitPrice:N0}");
                    }
                }
            }
            else if (text.Contains("doanh thu") || text.Contains("revenue"))
            {
                // Báo cáo doanh thu - sử dụng BuildRevenueContextAsync logic
                var (start, end, label) = ParseTimeExpression(message);
                
                var issueDetailsQuery = _db.StockIssueDetails
                    .Include(d => d.StockIssue)
                        .ThenInclude(i => i.Warehouse)
                    .Include(d => d.Material)
                    .AsNoTracking()
                    .Where(d => d.StockIssue.Status == DocumentStatus.DaXuatHang
                             && d.StockIssue.CreatedAt.Date >= start.Date
                             && d.StockIssue.CreatedAt.Date <= end.Date);

                var details = await issueDetailsQuery
                    .OrderByDescending(d => d.StockIssue.CreatedAt)
                    .Take(50)
                    .ToListAsync(ct);

                sb.AppendLine($"LOẠI: Báo cáo doanh thu {label.ToUpper()}");
                sb.AppendLine("DỮ LIỆU (50 dòng gần nhất):");
                sb.AppendLine("Ngày | Số phiếu | Kho | Mã | Tên | SL | Đơn giá | Thành tiền");

                foreach (var d in details)
                {
                    var revenue = (decimal)d.Quantity * d.UnitPrice;
                    sb.AppendLine($"{d.StockIssue.CreatedAt:dd/MM/yyyy} | {d.StockIssue.IssueNumber ?? ""} | {d.StockIssue.Warehouse?.Name ?? ""} | {d.Material?.Code ?? ""} | {d.Material?.Name ?? ""} | {d.Quantity} | {d.UnitPrice:N0} | {revenue:N0}");
                }

                var totalRevenue = details.Sum(d => (decimal)d.Quantity * d.UnitPrice);
                var totalQty = details.Sum(d => d.Quantity);
                sb.AppendLine();
                sb.AppendLine($"Tổng: Doanh thu {totalRevenue:N0} đ | Số lượng {totalQty:0.###}");
            }
            else if (text.Contains("xuất kho") || (text.Contains("xuất") && !text.Contains("doanh thu")))
            {
                // Chỉ match "xuất kho" nếu không có "doanh thu"
                var (start, end, label) = ParseTimeExpression(message);
                var issues = await _db.StockIssues
                    .Include(i => i.Warehouse)
                    .Include(i => i.Details)
                    .ThenInclude(d => d.Material)
                    .Where(i => i.CreatedAt >= start && i.CreatedAt <= end)
                    .OrderBy(i => i.CreatedAt)
                    .Take(50)
                    .ToListAsync(ct);

                sb.AppendLine($"LOẠI: Báo cáo xuất kho {label}");
                sb.AppendLine("DỮ LIỆU (50 phiếu đầu):");
                sb.AppendLine("Ngày | Kho | Mã | Tên | SL | Đơn giá | Thành tiền");

                foreach (var i in issues)
                {
                    foreach (var d in i.Details.Take(5))
                    {
                        sb.AppendLine($"{i.CreatedAt:dd/MM/yyyy} | {i.Warehouse.Name} | {d.Material.Code} | {d.Material.Name} | {d.Quantity} | {d.UnitPrice:N0} | {(decimal)d.Quantity * d.UnitPrice:N0}");
                    }
                }
            }
            else
            {
                sb.AppendLine("LOẠI: Báo cáo tổng hợp");
                sb.AppendLine("Vui lòng chỉ định loại báo cáo cụ thể (tồn kho, nhập kho, xuất kho, doanh thu)");
            }

            return sb.ToString();
        }

        private async Task<string> BuildDocumentDetailContextAsync(string message, CancellationToken ct)
        {
            var (documentType, code) = ParseDocumentCode(message);
            
            if (string.IsNullOrEmpty(documentType) || string.IsNullOrEmpty(code))
            {
                return "Không tìm thấy mã phiếu hoặc mã vật tư trong câu hỏi. Vui lòng cung cấp số phiếu (ví dụ: PX251202-0006) hoặc mã vật tư (ví dụ: N108).";
            }

            switch (documentType)
            {
                case "StockIssue":
                    return await BuildStockIssueDetailContextAsync(code, ct);
                case "StockReceipt":
                    return await BuildStockReceiptDetailContextAsync(code, ct);
                case "Material":
                    return await BuildMaterialDetailContextAsync(code, ct);
                default:
                    return "Không hỗ trợ loại document này.";
            }
        }

        private string BuildDatabaseSchemaContext()
        {
            var sb = new StringBuilder();
            sb.AppendLine("CẤU TRÚC CƠ SỞ DỮ LIỆU BEMART:");
            sb.AppendLine();
            sb.AppendLine("CÁC BẢNG CHÍNH:");
            sb.AppendLine();
            sb.AppendLine("1. Users - Người dùng hệ thống");
            sb.AppendLine("   - Id (int), FullName, Username, Email, Role, IsActive, RegisteredAt");
            sb.AppendLine();
            sb.AppendLine("2. Warehouses - Kho hàng");
            sb.AppendLine("   - Id (int), Name, Address, IsActive");
            sb.AppendLine();
            sb.AppendLine("3. Materials - Nguyên liệu/Vật tư");
            sb.AppendLine("   - Id (int), Code, Name, Unit, Description, SupplierId, PurchasePrice, SellingPrice");
            sb.AppendLine("   - MinimumStock, MaximumStock, ReorderQuantity");
            sb.AppendLine();
            sb.AppendLine("4. Suppliers - Nhà cung cấp");
            sb.AppendLine("   - Id (int), Name, ContactInfo, Address");
            sb.AppendLine();
            sb.AppendLine("5. StockReceipts - Phiếu nhập kho");
            sb.AppendLine("   - Id (int), ReceiptNumber (string, nullable), WarehouseId, CreatedAt, CreatedById, Status, Note");
            sb.AppendLine("   - CreatedBy (User): Người tạo phiếu (quan hệ CreatedById -> Users.Id)");
            sb.AppendLine("   - ApprovedBy (User, nullable): Người duyệt phiếu (quan hệ ApprovedById -> Users.Id)");
            sb.AppendLine("   - Warehouse: Kho nhập hàng (quan hệ WarehouseId -> Warehouses.Id)");
            sb.AppendLine("   - DeliveredByName (string, nullable): Tên người giao hàng");
            sb.AppendLine("   - ApprovedAt (DateTime, nullable): Ngày giờ duyệt");
            sb.AppendLine("   - Quan hệ: Details (StockReceiptDetails) - Chi tiết nhập");
            sb.AppendLine();
            sb.AppendLine("6. StockReceiptDetails - Chi tiết phiếu nhập");
            sb.AppendLine("   - Id (int), ReceiptId, MaterialId, Quantity (double), UnitPrice (decimal)");
            sb.AppendLine("   - Material: Vật tư (quan hệ MaterialId -> Materials.Id)");
            sb.AppendLine();
            sb.AppendLine("7. StockIssues - Phiếu xuất kho");
            sb.AppendLine("   - Id (int), IssueNumber (string, required), WarehouseId, CreatedAt, CreatedById, Status, Note");
            sb.AppendLine("   - CreatedBy (User): Người tạo phiếu (quan hệ CreatedById -> Users.Id)");
            sb.AppendLine("   - ApprovedBy (User, nullable): Người duyệt phiếu (quan hệ ApprovedById -> Users.Id)");
            sb.AppendLine("   - Warehouse: Kho xuất hàng (quan hệ WarehouseId -> Warehouses.Id)");
            sb.AppendLine("   - ReceivedByName (string, nullable): Tên người nhận hàng");
            sb.AppendLine("   - ApprovedAt (DateTime, nullable): Ngày giờ duyệt");
            sb.AppendLine("   - Quan hệ: Details (StockIssueDetails) - Chi tiết xuất");
            sb.AppendLine();
            sb.AppendLine("8. StockIssueDetails - Chi tiết phiếu xuất");
            sb.AppendLine("   - Id (int), IssueId, MaterialId, Quantity (double), UnitPrice (decimal)");
            sb.AppendLine("   - UnitPrice: Giá bán (revenue) - dùng để tính doanh thu");
            sb.AppendLine("   - CostPrice (decimal, nullable): Giá vốn (COGS) - tự động tính khi xuất kho");
            sb.AppendLine("   - Material: Vật tư (quan hệ MaterialId -> Materials.Id)");
            sb.AppendLine();
            sb.AppendLine("9. StockTransfers - Phiếu chuyển kho");
            sb.AppendLine("   - Id (int), TransferNumber, FromWarehouseId, ToWarehouseId, CreatedAt, Status");
            sb.AppendLine("   - Quan hệ: Details (StockTransferDetails)");
            sb.AppendLine();
            sb.AppendLine("10. Stocks - Tồn kho hiện tại");
            sb.AppendLine("    - Id (int), WarehouseId, MaterialId, Quantity");
            sb.AppendLine();
            sb.AppendLine("11. StockLots - Quản lý lô hàng");
            sb.AppendLine("    - Id (int), LotNumber, MaterialId, WarehouseId, Quantity");
            sb.AppendLine("    - ManufactureDate, ExpiryDate");
            sb.AppendLine();
            sb.AppendLine("12. StockBalances - Cân đối kho (theo ngày)");
            sb.AppendLine("    - Id (int), WarehouseId, MaterialId, Date");
            sb.AppendLine("    - InQty, OutQty, InValue, OutValue");
            sb.AppendLine();
            sb.AppendLine("13. PurchaseRequests - Đề xuất đặt hàng");
            sb.AppendLine("    - Id (int), RequestNumber, Status, CreatedAt, CreatedById");
            sb.AppendLine("    - Quan hệ: Details (PurchaseRequestDetails)");
            sb.AppendLine();
            sb.AppendLine("14. Roles - Vai trò");
            sb.AppendLine("    - Id (int), Code, Name");
            sb.AppendLine();
            sb.AppendLine("15. Permissions - Quyền");
            sb.AppendLine("    - Id (int), Module, ActionKey, DisplayName");
            sb.AppendLine();
            sb.AppendLine("16. RolePermissions - Phân quyền");
            sb.AppendLine("    - RoleId, PermissionId, CanRead, CanCreate, CanUpdate, CanDelete, CanApprove");
            sb.AppendLine();
            sb.AppendLine("17. Notifications - Thông báo");
            sb.AppendLine("    - Id (int), UserId, Type, Title, Message, Priority, IsRead, CreatedAt");
            sb.AppendLine();
            sb.AppendLine("18. AuditLogs - Nhật ký thao tác");
            sb.AppendLine("    - Id (int), UserId, Action, EntityType, EntityId, Timestamp");
            sb.AppendLine();
            sb.AppendLine("QUAN HỆ CHÍNH:");
            sb.AppendLine("- Users -> UserRoles -> Roles");
            sb.AppendLine("- Users -> UserWarehouses -> Warehouses");
            sb.AppendLine("- StockReceipts.CreatedById -> Users.Id (người tạo phiếu nhập)");
            sb.AppendLine("- StockReceipts.ApprovedById -> Users.Id (người duyệt phiếu nhập)");
            sb.AppendLine("- StockIssues.CreatedById -> Users.Id (người tạo phiếu xuất)");
            sb.AppendLine("- StockIssues.ApprovedById -> Users.Id (người duyệt phiếu xuất)");
            sb.AppendLine("- StockReceipts -> StockReceiptDetails -> Materials");
            sb.AppendLine("- StockIssues -> StockIssueDetails -> Materials");
            sb.AppendLine("- StockTransfers -> StockTransferDetails -> Materials");
            sb.AppendLine("- Stocks -> Materials, Warehouses");
            sb.AppendLine("- StockLots -> Materials, Warehouses");
            sb.AppendLine("- Materials -> Suppliers, Warehouses");
            sb.AppendLine();
            sb.AppendLine("VÍ DỤ TRUY VẤN:");
            sb.AppendLine("- Tìm phiếu xuất: SELECT * FROM StockIssues WHERE IssueNumber = 'PX251202-0006'");
            sb.AppendLine("- Tìm người tạo phiếu: JOIN với Users qua CreatedById");
            sb.AppendLine("- Tìm chi tiết phiếu: JOIN với StockIssueDetails qua IssueId");
            sb.AppendLine("- Tìm vật tư: SELECT * FROM Materials WHERE Code = 'N108'");
            sb.AppendLine("- Tính doanh thu: SUM(StockIssueDetails.Quantity * StockIssueDetails.UnitPrice) WHERE Status = 3 (DaXuatHang)");

            return sb.ToString();
        }

        private async Task<string> ExecuteSafeSqlQueryAsync(string message, CancellationToken ct)
        {
            try
            {
                // Extract SQL query from message (look for SELECT statements)
                var sqlMatch = Regex.Match(message, @"select\s+.*?\s+from\s+[\w\[\]]+", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                
                if (!sqlMatch.Success)
                {
                    return "Không tìm thấy câu lệnh SQL SELECT hợp lệ trong câu hỏi. Vui lòng cung cấp câu lệnh SELECT rõ ràng.";
                }

                var sqlQuery = sqlMatch.Value.Trim();

                // Security: Only allow SELECT statements
                var upperSql = sqlQuery.ToUpperInvariant();
                var forbiddenKeywords = new[] { "INSERT", "UPDATE", "DELETE", "DROP", "ALTER", "CREATE", "TRUNCATE", "EXEC", "EXECUTE", "SP_", "XP_" };
                
                if (forbiddenKeywords.Any(keyword => upperSql.Contains(keyword)))
                {
                    return "Chỉ cho phép thực thi câu lệnh SELECT. Các câu lệnh khác (INSERT, UPDATE, DELETE, DROP, etc.) không được phép vì lý do bảo mật.";
                }

                // Limit result set
                if (!upperSql.Contains("TOP") && !upperSql.Contains("LIMIT"))
                {
                    // Add TOP 1000 if not present
                    var selectIndex = upperSql.IndexOf("SELECT");
                    if (selectIndex >= 0)
                    {
                        var afterSelect = sqlQuery.Substring(selectIndex + 6).TrimStart();
                        sqlQuery = "SELECT TOP 1000 " + afterSelect;
                    }
                }

                // Execute query using raw SQL (EF Core) - safer approach
                var connection = _db.Database.GetDbConnection();
                var wasOpen = connection.State == ConnectionState.Open;
                
                if (!wasOpen)
                {
                    await connection.OpenAsync(ct);
                }

                try
                {
                    using var command = connection.CreateCommand();
                    command.CommandText = sqlQuery;
                    command.CommandTimeout = 30; // 30 seconds timeout

                    using var reader = await command.ExecuteReaderAsync(ct);
                    
                    var results = new List<Dictionary<string, object>>();
                    var columnNames = new List<string>();

                    // Get column names
                    if (reader.HasRows)
                    {
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            columnNames.Add(reader.GetName(i));
                        }

                        int rowCount = 0;
                        const int maxRows = 1000;

                        while (await reader.ReadAsync(ct) && rowCount < maxRows)
                        {
                            var row = new Dictionary<string, object>();
                            for (int i = 0; i < reader.FieldCount; i++)
                            {
                                var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                                row[columnNames[i]] = value;
                            }
                            results.Add(row);
                            rowCount++;
                        }
                    }

                    if (!wasOpen)
                    {
                        await connection.CloseAsync();
                    }

                    if (!results.Any())
                    {
                        return "Câu lệnh SQL đã được thực thi nhưng không trả về kết quả nào.";
                    }

                    // Format results
                    var sb = new StringBuilder();
                    sb.AppendLine($"KẾT QUẢ TRUY VẤN SQL ({results.Count} dòng):");
                    sb.AppendLine();
                    sb.AppendLine(string.Join(" | ", columnNames));
                    sb.AppendLine(new string('-', Math.Min(200, columnNames.Sum(c => c.Length) + columnNames.Count * 3)));

                    foreach (var row in results.Take(100)) // Limit display to 100 rows
                    {
                        var values = columnNames.Select(col => 
                        {
                            var val = row[col];
                            if (val == null) return "NULL";
                            if (val is DateTime dt) return dt.ToString("dd/MM/yyyy HH:mm:ss");
                            if (val is decimal dec) return dec.ToString("N2");
                            return val.ToString();
                        });
                        sb.AppendLine(string.Join(" | ", values));
                    }

                    if (results.Count > 100)
                    {
                        sb.AppendLine();
                        sb.AppendLine($"(Hiển thị 100/{results.Count} dòng đầu tiên)");
                    }

                    return sb.ToString();
                }
                finally
                {
                    if (!wasOpen && connection.State == ConnectionState.Open)
                    {
                        await connection.CloseAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing SQL query: {Query}", message);
                return $"Lỗi khi thực thi SQL: {ex.Message}";
            }
        }

        private string BuildWebApplicationContext()
        {
            var sb = new StringBuilder();
            sb.AppendLine("THÔNG TIN VỀ HỆ THỐNG BEMART:");
            sb.AppendLine();
            sb.AppendLine("BEMART là hệ thống quản lý kho hàng toàn diện với các tính năng chính:");
            sb.AppendLine();
            sb.AppendLine("1. QUẢN LÝ CƠ BẢN:");
            sb.AppendLine("   - Quản lý nguyên liệu/vật tư (Materials): Thêm, sửa, xóa, tìm kiếm");
            sb.AppendLine("   - Quản lý kho hàng (Warehouses): Tạo và quản lý nhiều kho");
            sb.AppendLine("   - Quản lý nhà cung cấp (Suppliers): Danh sách và thông tin liên hệ");
            sb.AppendLine("   - Quản lý tồn kho (Stocks): Xem tồn kho theo kho và vật tư");
            sb.AppendLine();
            sb.AppendLine("2. NGHIỆP VỤ KHO:");
            sb.AppendLine("   - Nhập kho (Stock Receipts): Tạo phiếu nhập, quản lý lô hàng");
            sb.AppendLine("   - Xuất kho (Stock Issues): Tạo phiếu xuất, phân bổ lô");
            sb.AppendLine("   - Chuyển kho (Stock Transfers): Chuyển hàng giữa các kho");
            sb.AppendLine("   - Quản lý lô (Stock Lots): Theo dõi NSX, HSD, số lượng theo lô");
            sb.AppendLine();
            sb.AppendLine("3. BÁO CÁO & PHÂN TÍCH:");
            sb.AppendLine("   - Báo cáo tồn kho: Tồn kho hiện tại, tồn kho thấp");
            sb.AppendLine("   - Báo cáo nhập xuất: Theo kho, theo thời gian");
            sb.AppendLine("   - Báo cáo doanh thu: Doanh thu xuất kho");
            sb.AppendLine("   - Báo cáo lợi nhuận (P&L)");
            sb.AppendLine("   - Tỷ lệ quay vòng hàng tồn kho");
            sb.AppendLine();
            sb.AppendLine("4. ĐỀ XUẤT & PHÊ DUYỆT:");
            sb.AppendLine("   - Đề xuất đặt hàng (Purchase Requests): Tạo và phê duyệt");
            sb.AppendLine("   - Quy trình phê duyệt nhiều cấp");
            sb.AppendLine();
            sb.AppendLine("5. HỆ THỐNG:");
            sb.AppendLine("   - Quản lý người dùng: Tạo, duyệt, phân quyền");
            sb.AppendLine("   - Phân quyền chi tiết: Theo module và hành động (Xem, Thêm, Sửa, Xóa, Duyệt)");
            sb.AppendLine("   - Thông báo: Desktop notifications, email, âm thanh");
            sb.AppendLine("   - Nhật ký thao tác (Audit Logs): Ghi lại mọi thao tác");
            sb.AppendLine("   - Quản lý tài liệu: Đính kèm file cho phiếu nhập/xuất");
            sb.AppendLine();
            sb.AppendLine("6. TÍNH NĂNG NÂNG CAO:");
            sb.AppendLine("   - Tự động đặt hàng khi tồn kho thấp");
            sb.AppendLine("   - Tối ưu chuyển kho dựa trên khoảng cách");
            sb.AppendLine("   - Cảnh báo hết hạn sử dụng");
            sb.AppendLine("   - Dự báo nhu cầu");
            sb.AppendLine();
            sb.AppendLine("7. QUYỀN TRUY CẬP:");
            sb.AppendLine("   - Admin: Toàn quyền quản lý hệ thống");
            sb.AppendLine("   - User: Quyền hạn chế theo phân quyền");
            sb.AppendLine("   - Phân quyền theo module: Materials, Warehouses, Receipts, Issues, Reports, etc.");
            sb.AppendLine();
            sb.AppendLine("8. HƯỚNG DẪN SỬ DỤNG:");
            sb.AppendLine("   - Menu chính nằm ở sidebar bên trái");
            sb.AppendLine("   - Mỗi module có các chức năng: Xem danh sách, Thêm mới, Sửa, Xóa");
            sb.AppendLine("   - Sử dụng chatbot này để hỏi về dữ liệu và tính năng");
            sb.AppendLine("   - Có thể truy vấn SQL trực tiếp bằng cách hỏi 'SELECT ... FROM ...'");

            return sb.ToString();
        }

        private string BuildSystemInfoContext()
        {
            var sb = new StringBuilder();
            sb.AppendLine("THÔNG TIN HỆ THỐNG BEMART:");
            sb.AppendLine();
            sb.AppendLine("CÔNG NGHỆ:");
            sb.AppendLine("- Framework: ASP.NET Core (C#)");
            sb.AppendLine("- Database: SQL Server");
            sb.AppendLine("- ORM: Entity Framework Core");
            sb.AppendLine("- Frontend: HTML, CSS (Tailwind), JavaScript");
            sb.AppendLine("- AI Chatbot: Google Gemini API");
            sb.AppendLine();
            sb.AppendLine("KIẾN TRÚC:");
            sb.AppendLine("- MVC Pattern");
            sb.AppendLine("- Repository Pattern với DbContext");
            sb.AppendLine("- Service Layer cho business logic");
            sb.AppendLine("- Permission-based authorization");
            sb.AppendLine();
            sb.AppendLine("TÍNH NĂNG BẢO MẬT:");
            sb.AppendLine("- Authentication: Cookie-based");
            sb.AppendLine("- Authorization: Role và Permission-based");
            sb.AppendLine("- SQL Injection protection: Parameterized queries");
            sb.AppendLine("- Audit logging: Ghi lại mọi thao tác");
            sb.AppendLine();
            sb.AppendLine("DỮ LIỆU:");
            sb.AppendLine("- Hỗ trợ đa kho");
            sb.AppendLine("- Quản lý lô hàng với NSX/HSD");
            sb.AppendLine("- Tính giá xuất kho: Weighted Average, FIFO, LIFO");
            sb.AppendLine("- Cân đối kho theo ngày");

            return sb.ToString();
        }

        public async Task<string> AskAsync(string message, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(message))
                return "Vui lòng nhập nội dung câu hỏi.";

            var apiKey = _configuration["Gemini:ApiKey"];
            var model = _configuration["Gemini:Model"];

            if (string.IsNullOrWhiteSpace(model))
            {
                return "Chưa cấu hình model cho Gemini (Gemini:Model). Vui lòng cập nhật trong appsettings.json.";
            }

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                _logger.LogWarning("Gemini API key is not configured.");
                return "Chưa cấu hình khoá API cho Gemini. Vui lòng liên hệ quản trị hệ thống.";
            }

            // Dùng endpoint v1 chính thức của Google Generative Language API.
            // Ví dụ:
            //   "Gemini:Model": "gemini-2.5-flash"           => gọi: v1/models/gemini-2.5-flash:generateContent
            //   "Gemini:Model": "models/gemini-2.5-flash"   => giữ nguyên, không thêm "models/" lần nữa.
            var modelPath = model.StartsWith("models/", StringComparison.OrdinalIgnoreCase)
                ? model
                : $"models/{model}";

            var url = $"https://generativelanguage.googleapis.com/v1/{modelPath}:generateContent?key={apiKey}";

            // Xác định intent + build context từ DB nếu cần
            var intent = DetectIntent(message);
            string context = string.Empty;
            string schemaContext = string.Empty;
            bool includeSchema = false;

            try
            {
                switch (intent)
                {
                    case ChatIntent.TotalStockByWarehouse:
                        context = await BuildTotalStockByWarehouseContextAsync(cancellationToken);
                        includeSchema = true;
                        break;
                    case ChatIntent.ReceiptInMonth:
                        context = await BuildReceiptInMonthContextAsync(message, cancellationToken);
                        includeSchema = true;
                        break;
                    case ChatIntent.ExpiringSoon:
                        context = await BuildExpiringSoonContextAsync(cancellationToken);
                        includeSchema = true;
                        break;
                    case ChatIntent.RevenueInMonth:
                        context = await BuildRevenueContextAsync(message, cancellationToken);
                        includeSchema = true;
                        break;
                    case ChatIntent.ExpiredProducts:
                        context = await BuildExpiredProductsContextAsync(cancellationToken);
                        includeSchema = true;
                        break;
                    case ChatIntent.LowStock:
                        context = await BuildLowStockContextAsync(cancellationToken);
                        includeSchema = true;
                        break;
                    case ChatIntent.WarehouseEfficiency:
                        context = await BuildWarehouseEfficiencyContextAsync(cancellationToken);
                        includeSchema = true;
                        break;
                    case ChatIntent.ProcessImprovement:
                        context = await BuildProcessImprovementContextAsync(cancellationToken);
                        includeSchema = true;
                        break;
                    case ChatIntent.GenerateReport:
                        context = await BuildReportGenerationContextAsync(message, cancellationToken);
                        includeSchema = true;
                        break;
                    case ChatIntent.SqlQuery:
                        // Execute SQL query and include results
                        context = await ExecuteSafeSqlQueryAsync(message, cancellationToken);
                        includeSchema = true;
                        break;
                    case ChatIntent.WebQuestion:
                        context = BuildWebApplicationContext();
                        break;
                    case ChatIntent.SystemInfo:
                        context = BuildSystemInfoContext();
                        break;
                    case ChatIntent.DocumentDetail:
                        context = await BuildDocumentDetailContextAsync(message, cancellationToken);
                        includeSchema = true;
                        break;
                    case ChatIntent.SmallTalk:
                    case ChatIntent.Unknown:
                    default:
                        // For unknown questions, include schema if it seems data-related
                        var text = message.ToLowerInvariant();
                        if (text.Contains("bảng") || text.Contains("table") || text.Contains("dữ liệu") || 
                            text.Contains("data") || text.Contains("query") || text.Contains("truy vấn"))
                        {
                            includeSchema = true;
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error building warehouse context for chatbot");
                // vẫn tiếp tục gọi AI nhưng báo rõ là có lỗi khi lấy dữ liệu hệ thống
                context = "(Lỗi khi lấy dữ liệu từ hệ thống kho. Vui lòng kiểm tra log ứng dụng.)";
            }

            // Build schema context if needed
            if (includeSchema)
            {
                schemaContext = BuildDatabaseSchemaContext();
            }

            var systemPersona = "" +
                "Bạn là trợ lý AI BEMART, chuyên gia quản lý kho và hệ thống BEMART.\n" +
                "\n" +
                "PHONG CÁCH:\n" +
                "- Giọng điệu thân thiện, nhanh nhạy, nhưng trả lời súc tích, tự nhiên, giống người thật nói chuyện.\n" +
                "- Luôn ưu tiên độ chính xác, không bịa số liệu, không tự nhân/chia hay làm tròn số lượng so với dữ liệu hệ thống.\n" +
                "- Khi nói về số liệu, hãy giữ đúng con số trong dữ liệu (ví dụ dữ liệu là 3 thì trả lời là 3, không viết thành 3.000).\n" +
                "- Không dùng định dạng Markdown như **in đậm**, *, #, gạch đầu dòng; trả lời ở dạng văn bản thuần.\n" +
                "- Nếu không có đủ dữ liệu trong ngữ cảnh hệ thống, hãy nói rõ là không đủ dữ liệu trong hệ thống để trả lời chính xác.\n" +
                "\n" +
                "KHẢ NĂNG:\n" +
                "- Trả lời câu hỏi về dữ liệu kho hàng, nhập/xuất, tồn kho, hết hạn, doanh thu.\n" +
                "- Hỗ trợ truy vấn doanh thu với các biểu thức thời gian linh hoạt: hôm nay, hôm qua, tuần này/tuần trước, tháng này/tháng trước, năm này/năm trước, gần đây (3 ngày), hoặc khoảng thời gian cụ thể (từ ... đến ...).\n" +
                "- Trả lời câu hỏi chi tiết về phiếu: người tạo phiếu (CreatedBy), ngày tạo, kho, trạng thái, chi tiết sản phẩm, người duyệt...\n" +
                "- Trả lời câu hỏi về vật tư/nguyên liệu: thông tin chi tiết theo mã, tồn kho, giá, nhà cung cấp...\n" +
                "- Ví dụ: 'người tạo phiếu PX251202-0006 là ai', 'phiếu PX251202-0006 có những gì', 'thông tin vật tư mã N108'.\n" +
                "- Trả lời câu hỏi về cách sử dụng hệ thống BEMART, các tính năng, hướng dẫn.\n" +
                "- Hỗ trợ truy vấn SQL: Khi người dùng yêu cầu truy vấn SQL, hệ thống sẽ tự động thực thi câu lệnh SELECT an toàn và trả về kết quả.\n" +
                "- Giải thích cấu trúc database, các bảng và quan hệ giữa chúng.\n" +
                "- Cung cấp thông tin về hệ thống, công nghệ, kiến trúc.\n" +
                "\n" +
                "TRUY VẤN SQL:\n" +
                "- Chỉ hỗ trợ câu lệnh SELECT (không cho phép INSERT, UPDATE, DELETE, DROP, etc.).\n" +
                "- Kết quả được giới hạn tối đa 1000 dòng.\n" +
                "- Sử dụng tên bảng và cột chính xác từ schema database.\n" +
                "- Có thể JOIN các bảng liên quan để lấy dữ liệu phức tạp.\n";

            var promptParts = new List<string>
            {
                systemPersona
            };

            // Add database schema if needed
            if (!string.IsNullOrEmpty(schemaContext))
            {
                promptParts.Add("");
                promptParts.Add("CẤU TRÚC CƠ SỞ DỮ LIỆU:");
                promptParts.Add(schemaContext);
            }

            // Add context data
            if (!string.IsNullOrEmpty(context))
            {
                promptParts.Add("");
                promptParts.Add("THÔNG TIN NGỮ CẢNH TỪ HỆ THỐNG:");
                promptParts.Add(context);
            }

            promptParts.Add("");
            promptParts.Add($"LOẠI CÂU HỎI HỆ THỐNG NHẬN DIỆN: {intent}");
            promptParts.Add("");
            promptParts.Add("CÂU HỎI NGƯỜI DÙNG:");
            promptParts.Add(message);
            promptParts.Add("");
            promptParts.Add("YÊU CẦU TRẢ LỜI:");
            promptParts.Add("- Trả lời bằng tiếng Việt.");
            
            if (intent == ChatIntent.SqlQuery)
            {
                promptParts.Add("- Đây là câu hỏi về SQL. Nếu đã có kết quả truy vấn trong THÔNG TIN NGỮ CẢNH, hãy phân tích và giải thích kết quả.");
                promptParts.Add("- Nếu câu hỏi yêu cầu tạo SQL query mới, hãy đưa ra câu lệnh SELECT hợp lệ dựa trên cấu trúc database đã cho.");
            }
            else if (intent == ChatIntent.WebQuestion)
            {
                promptParts.Add("- Đây là câu hỏi về cách sử dụng hệ thống BEMART. Hãy giải thích rõ ràng, dễ hiểu.");
                promptParts.Add("- Tham khảo thông tin trong THÔNG TIN NGỮ CẢNH để trả lời chính xác về các tính năng.");
            }
            else if (intent == ChatIntent.SystemInfo)
            {
                promptParts.Add("- Đây là câu hỏi về thông tin hệ thống. Hãy cung cấp thông tin chi tiết về công nghệ, kiến trúc, bảo mật.");
            }
            else if (intent == ChatIntent.DocumentDetail)
            {
                promptParts.Add("- Đây là câu hỏi về thông tin chi tiết của một phiếu, vật tư, hoặc entity cụ thể.");
                promptParts.Add("- Sử dụng thông tin chi tiết trong phần THÔNG TIN NGỮ CẢNH để trả lời câu hỏi.");
                promptParts.Add("- Trả lời một cách tự nhiên, dễ hiểu, sử dụng thông tin từ context nhưng trình bày theo cách tự nhiên.");
                promptParts.Add("- Nếu có thông tin người tạo phiếu, hãy nêu tên đầy đủ (FullName) của người đó.");
                promptParts.Add("- Nếu có chi tiết sản phẩm, liệt kê đầy đủ với số lượng, giá, thành tiền.");
            }
            else
            {
                promptParts.Add("- Nếu câu hỏi liên quan đến kho, nhập/xuất, tồn, hết hạn... thì ưu tiên sử dụng số liệu trong phần THÔNG TIN NGỮ CẢNH.");
                promptParts.Add("- Có thể phân tích, so sánh, nhận xét dựa trên số liệu đã cho.");
            }
            
            promptParts.Add("- Nếu dữ liệu không đủ hoặc không liên quan, hãy nói rõ và gợi ý người dùng xem đúng báo cáo trong hệ thống BEMART hoặc hỏi lại với câu hỏi cụ thể hơn.");

            var fullPrompt = string.Join("\n", promptParts);

            var request = new GeminiRequest(new[]
            {
                new GeminiRequestContent(new[]
                {
                    new GeminiRequestContentPart(fullPrompt)
                })
            });

            var json = JsonSerializer.Serialize(request, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            try
            {
                using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Gemini API error: {Status} - {Body}", response.StatusCode, body);

                    try
                    {
                        var err = JsonSerializer.Deserialize<GoogleErrorWrapper>(body, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });
                        if (err?.Error != null)
                        {
                            var msg = $"Lỗi AI ({err.Error.Status}): {err.Error.Message}";
                            return msg;
                        }
                    }
                    catch
                    {
                        // ignore parse error, fall back to generic message
                    }

                    return "Không gọi được AI, vui lòng thử lại sau.";
                }

                var data = JsonSerializer.Deserialize<GeminiResponse>(body, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                var text = data?.Candidates?
                    .FirstOrDefault()?
                    .Content?.Parts?
                    .FirstOrDefault()?
                    .Text;

                return string.IsNullOrWhiteSpace(text)
                    ? "AI chưa trả lời, vui lòng thử lại." 
                    : text.Trim();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling Gemini API");
                return "Có lỗi xảy ra khi gọi AI. Vui lòng thử lại sau.";
            }
        }
    }
}

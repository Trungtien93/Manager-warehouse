using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using MNBEMART.Data;
using MNBEMART.Models;
using MNBEMART.ViewModels;
using MNBEMART.Filters;
using System.Text;
using MNBEMART.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Caching.Memory;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace MNBEMART.Controllers
{
    [Authorize]
    public class ReportsController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IExcelService _excel;
        private readonly IMemoryCache _cache;
        
        [ActivatorUtilitiesConstructor]
        public ReportsController(AppDbContext db, IExcelService excel, IMemoryCache cache)
        {
            _db = db;
            _excel = excel;
            _cache = cache;
        }

        // ===== Helper: chuẩn hoá khoảng ngày [from..to] =====
        private static (DateTime from, DateTime to) NormRange(DateTime? from, DateTime? to)
        {
            var now = DateTime.Now;
            var f = from?.Date ?? new DateTime(now.Year, now.Month, 1);
            var t = to?.Date   ?? f.AddMonths(1).AddDays(-1);
            return (f, t);
        }

        // ===== Helper: Cache warehouse name =====
        private async Task<string> GetWarehouseNameAsync(int? warehouseId)
        {
            if (!warehouseId.HasValue) return "Tất cả kho";
            
            var cacheKey = $"warehouse_name_{warehouseId.Value}";
            if (_cache.TryGetValue(cacheKey, out string? cachedName) && cachedName != null)
            {
                return cachedName;
            }

            var warehouse = await _db.Warehouses
                .AsNoTracking()
                .Where(w => w.Id == warehouseId.Value)
                .Select(w => w.Name)
                .FirstOrDefaultAsync();
            
            var name = warehouse ?? "Tất cả kho";
            
            // Cache for 30 minutes
            _cache.Set(cacheKey, name, TimeSpan.FromMinutes(30));
            
            return name;
        }

        // ===== Helper: Cache warehouses list =====
        private async Task<List<SelectListItem>> GetCachedWarehousesAsync(int? selectedId = null)
        {
            const string cacheKey = "warehouses_selectlist";
            if (_cache.TryGetValue(cacheKey, out List<SelectListItem>? cached) && cached != null)
            {
                // Update selected item if needed
                if (selectedId.HasValue)
                {
                    cached.ForEach(item => item.Selected = item.Value == selectedId.Value.ToString());
                }
                return cached;
            }

            var warehouses = await _db.Warehouses
                .AsNoTracking()
                .OrderBy(w => w.Name)
                .Select(w => new SelectListItem
                {
                    Value = w.Id.ToString(),
                    Text = w.Name
                })
                .ToListAsync();
            
            // Cache for 30 minutes
            _cache.Set(cacheKey, warehouses, TimeSpan.FromMinutes(30));
            
            // Set selected if needed
            if (selectedId.HasValue)
            {
                warehouses.ForEach(item => item.Selected = item.Value == selectedId.Value.ToString());
            }
            
            return warehouses;
        }

        // ==========================================================================================
        // 1) DOANH THU — lấy theo OutValue trong StockBalance (phiếu đã xuất)
        //    GroupBy: day | month | year    + lọc kho (tuỳ chọn)
        // ==========================================================================================
        [HttpGet]
[RequirePermission("Reports","Read")]
public async Task<IActionResult> Revenue([FromQuery] ReportFilterVM f)
        {
            var (from, to) = NormRange(f.From, f.To);
            f.GroupBy = string.IsNullOrWhiteSpace(f.GroupBy) ? "day" : f.GroupBy.ToLowerInvariant();

            ViewBag.Warehouses = new SelectList(
                await GetCachedWarehousesAsync(f.WarehouseId),
                "Value", "Text", f.WarehouseId?.ToString());
            ViewBag.Filter = f;

            // Query StockIssueDetails for revenue (selling price) - Revenue should use UnitPrice, not cost price
            var issueDetailsQuery = _db.StockIssueDetails
                .Include(d => d.StockIssue)
                .AsNoTracking()
                .Where(d => d.StockIssue.Status == DocumentStatus.DaXuatHang
                         && d.StockIssue.CreatedAt.Date >= from
                         && d.StockIssue.CreatedAt.Date <= to);

            if (f.WarehouseId.HasValue)
                issueDetailsQuery = issueDetailsQuery.Where(d => d.StockIssue.WarehouseId == f.WarehouseId.Value);

            // Check if any data exists
            var hasAnyData = await issueDetailsQuery.AnyAsync();
            ViewBag.HasData = hasAnyData;
            
            if (!hasAnyData)
            {
                ViewBag.Message = "Không tìm thấy dữ liệu xuất kho trong khoảng thời gian đã chọn. Vui lòng kiểm tra xem đã có phiếu xuất kho được xử lý (trạng thái 'Đã xuất hàng') chưa.";
            }
            else
            {
                // Check if there's any actual revenue (UnitPrice > 0)
                var hasRevenue = await issueDetailsQuery.AnyAsync(d => d.UnitPrice > 0);
                if (!hasRevenue)
                {
                    ViewBag.Message = "Có dữ liệu trong khoảng thời gian này nhưng chưa có doanh thu. Vui lòng kiểm tra lại giá bán (UnitPrice) trong các phiếu xuất kho.";
                }
            }

            // Luôn chuẩn bị dữ liệu theo ngày cho Calendar (độc lập GroupBy)
            // Use CreatedAt date from StockIssue, and calculate revenue as UnitPrice * Quantity
            var calRows = await issueDetailsQuery
                .GroupBy(d => d.StockIssue.CreatedAt.Date)
                .Select(g => new { 
                    Day = g.Key, 
                    Total = g.Sum(z => (decimal)z.Quantity * z.UnitPrice), 
                    Qty = g.Sum(z => (decimal)z.Quantity) 
                })
                .OrderBy(x => x.Day)
                .ToListAsync();
            ViewBag.CalendarDaily = calRows.Select(x => new RevenueRowVM {
                Period = x.Day.ToString("yyyy-MM-dd"),
                TotalRevenue = x.Total,
                QtyOut = x.Qty
            }).ToList();

            switch (f.GroupBy)
            {
case "year":
                {
                    var rows = await issueDetailsQuery
                        .GroupBy(d => d.StockIssue.CreatedAt.Year)
                        .Select(g => new { 
                            Year = g.Key, 
                            Total = g.Sum(z => (decimal)z.Quantity * z.UnitPrice), 
                            Qty = g.Sum(z => (decimal)z.Quantity) 
                        })
                        .OrderBy(x => x.Year)
                        .ToListAsync();

                    var data = rows.Select(x => new RevenueRowVM {
                        Period = x.Year.ToString(),
                        TotalRevenue = x.Total,
                        QtyOut = x.Qty
                    }).ToList();

                    // tổng cộng
                    ViewBag.Total = data.Sum(x => x.TotalRevenue);
                    ViewBag.TotalQty = data.Sum(x => x.QtyOut);
                    return View(data);
                }
case "month":
                {
                    var rows = await issueDetailsQuery
                        .GroupBy(d => new { d.StockIssue.CreatedAt.Year, d.StockIssue.CreatedAt.Month })
                        .Select(g => new { 
                            g.Key.Year, 
                            g.Key.Month, 
                            Total = g.Sum(z => (decimal)z.Quantity * z.UnitPrice), 
                            Qty = g.Sum(z => (decimal)z.Quantity) 
                        })
                        .OrderBy(x => x.Year).ThenBy(x => x.Month)
                        .ToListAsync();

                    var data = rows.Select(x => new RevenueRowVM {
                        Period = $"{x.Year}-{x.Month:00}",
                        TotalRevenue = x.Total,
                        QtyOut = x.Qty
                    }).ToList();

                    ViewBag.Total = data.Sum(x => x.TotalRevenue);
                    ViewBag.TotalQty = data.Sum(x => x.QtyOut);
                    return View(data);
                }
default: // day
                {
                    var rows = await issueDetailsQuery
                        .GroupBy(d => d.StockIssue.CreatedAt.Date)
                        .Select(g => new { 
                            Day = g.Key, 
                            Total = g.Sum(z => (decimal)z.Quantity * z.UnitPrice), 
                            Qty = g.Sum(z => (decimal)z.Quantity) 
                        })
                        .OrderBy(x => x.Day)
                        .ToListAsync();

                    var data = rows.Select(x => new RevenueRowVM {
                        Period = x.Day.ToString("yyyy-MM-dd"),
                        TotalRevenue = x.Total,
                        QtyOut = x.Qty
                    }).ToList();

                    ViewBag.Total = data.Sum(x => x.TotalRevenue);
                    ViewBag.TotalQty = data.Sum(x => x.QtyOut);
                    return View(data);
                }
            }
        }

        // Excel Export for Revenue
        [HttpGet]
        [RequirePermission("Reports","Read")]
        public async Task<IActionResult> RevenueExcel([FromQuery] ReportFilterVM f)
        {
            var (from, to) = NormRange(f.From, f.To);
            f.GroupBy = string.IsNullOrWhiteSpace(f.GroupBy) ? "day" : f.GroupBy.ToLowerInvariant();

            // Query StockIssueDetails for revenue (selling price)
            var issueDetailsQuery = _db.StockIssueDetails
                .Include(d => d.StockIssue)
                .AsNoTracking()
                .Where(d => d.StockIssue.Status == DocumentStatus.DaXuatHang
                         && d.StockIssue.CreatedAt.Date >= from
                         && d.StockIssue.CreatedAt.Date <= to);

            if (f.WarehouseId.HasValue)
                issueDetailsQuery = issueDetailsQuery.Where(d => d.StockIssue.WarehouseId == f.WarehouseId.Value);

            var rows = new List<RevenueRowVM>();
            switch (f.GroupBy)
            {
                case "year":
                {
                    var y = await issueDetailsQuery
                        .GroupBy(d => d.StockIssue.CreatedAt.Year)
                        .Select(g => new { 
                            Year = g.Key, 
                            Total = g.Sum(z => (decimal)z.Quantity * z.UnitPrice), 
                            Qty = g.Sum(z => (decimal)z.Quantity) 
                        })
                        .OrderBy(x => x.Year)
                        .ToListAsync();
                    rows = y.Select(x => new RevenueRowVM { Period = x.Year.ToString(), TotalRevenue = x.Total, QtyOut = x.Qty }).ToList();
                    break;
                }
                case "month":
                {
                    var m = await issueDetailsQuery
                        .GroupBy(d => new { d.StockIssue.CreatedAt.Year, d.StockIssue.CreatedAt.Month })
                        .Select(g => new { 
                            g.Key.Year, 
                            g.Key.Month, 
                            Total = g.Sum(z => (decimal)z.Quantity * z.UnitPrice), 
                            Qty = g.Sum(z => (decimal)z.Quantity) 
                        })
                        .OrderBy(x => x.Year).ThenBy(x => x.Month)
                        .ToListAsync();
                    rows = m.Select(x => new RevenueRowVM { Period = $"{x.Year}-{x.Month:00}", TotalRevenue = x.Total, QtyOut = x.Qty }).ToList();
                    break;
                }
                default:
                {
                    var d = await issueDetailsQuery
                        .GroupBy(d => d.StockIssue.CreatedAt.Date)
                        .Select(g => new { 
                            Day = g.Key, 
                            Total = g.Sum(z => (decimal)z.Quantity * z.UnitPrice), 
                            Qty = g.Sum(z => (decimal)z.Quantity) 
                        })
                        .OrderBy(x => x.Day)
                        .ToListAsync();
                    rows = d.Select(x => new RevenueRowVM { Period = x.Day.ToString("yyyy-MM-dd"), TotalRevenue = x.Total, QtyOut = x.Qty }).ToList();
                    break;
                }
            }

            // Export to Excel using ExcelService
            var excelData = rows.Select(r => new {
                Period = r.Period,
                TotalRevenue = r.TotalRevenue,
                QtyOut = r.QtyOut
            });

            var headers = new[] { "Kỳ", "Doanh thu", "Số lượng xuất" };
            var bytes = _excel.ExportToExcel(excelData, "Doanh thu", headers);
            var fileName = $"DoanhThu_{from:yyyyMMdd}_{to:yyyyMMdd}.xlsx";
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }

        // CSV Export for Revenue (keep for backward compatibility)
        [HttpGet]
        [RequirePermission("Reports","Read")]
        public async Task<IActionResult> RevenueExport([FromQuery] ReportFilterVM f)
        {
            var (from, to) = NormRange(f.From, f.To);
            f.GroupBy = string.IsNullOrWhiteSpace(f.GroupBy) ? "day" : f.GroupBy.ToLowerInvariant();

            // Query StockIssueDetails for revenue (selling price)
            var issueDetailsQuery = _db.StockIssueDetails
                .Include(d => d.StockIssue)
                .AsNoTracking()
                .Where(d => d.StockIssue.Status == DocumentStatus.DaXuatHang
                         && d.StockIssue.CreatedAt.Date >= from
                         && d.StockIssue.CreatedAt.Date <= to);

            if (f.WarehouseId.HasValue)
                issueDetailsQuery = issueDetailsQuery.Where(d => d.StockIssue.WarehouseId == f.WarehouseId.Value);

            var rows = new List<RevenueRowVM>();
            switch (f.GroupBy)
            {
                case "year":
                {
                    var y = await issueDetailsQuery
                        .GroupBy(d => d.StockIssue.CreatedAt.Year)
                        .Select(g => new { 
                            Year = g.Key, 
                            Total = g.Sum(z => (decimal)z.Quantity * z.UnitPrice), 
                            Qty = g.Sum(z => (decimal)z.Quantity) 
                        })
                        .OrderBy(x => x.Year)
                        .ToListAsync();
                    rows = y.Select(x => new RevenueRowVM { Period = x.Year.ToString(), TotalRevenue = x.Total, QtyOut = x.Qty }).ToList();
                    break;
                }
                case "month":
                {
                    var m = await issueDetailsQuery
                        .GroupBy(d => new { d.StockIssue.CreatedAt.Year, d.StockIssue.CreatedAt.Month })
                        .Select(g => new { 
                            g.Key.Year, 
                            g.Key.Month, 
                            Total = g.Sum(z => (decimal)z.Quantity * z.UnitPrice), 
                            Qty = g.Sum(z => (decimal)z.Quantity) 
                        })
                        .OrderBy(x => x.Year).ThenBy(x => x.Month)
                        .ToListAsync();
                    rows = m.Select(x => new RevenueRowVM { Period = $"{x.Year}-{x.Month:00}", TotalRevenue = x.Total, QtyOut = x.Qty }).ToList();
                    break;
                }
                default:
                {
                    var d = await issueDetailsQuery
                        .GroupBy(d => d.StockIssue.CreatedAt.Date)
                        .Select(g => new { 
                            Day = g.Key, 
                            Total = g.Sum(z => (decimal)z.Quantity * z.UnitPrice), 
                            Qty = g.Sum(z => (decimal)z.Quantity) 
                        })
                        .OrderBy(x => x.Day)
                        .ToListAsync();
                    rows = d.Select(x => new RevenueRowVM { Period = x.Day.ToString("yyyy-MM-dd"), TotalRevenue = x.Total, QtyOut = x.Qty }).ToList();
                    break;
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine("Period,TotalRevenue,QtyOut");
            foreach (var r in rows)
                sb.AppendLine($"{r.Period},{r.TotalRevenue},{r.QtyOut}");

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            var fileName = $"revenue_{from:yyyyMMdd}_{to:yyyyMMdd}.csv";
            return File(bytes, "text/csv", fileName);
        }

        // PDF Export for Revenue
        [HttpGet]
        [RequirePermission("Reports", "Read")]
        public async Task<IActionResult> ExportPdfRevenue([FromQuery] ReportFilterVM f)
        {
            var (from, to) = NormRange(f.From, f.To);
            f.GroupBy = string.IsNullOrWhiteSpace(f.GroupBy) ? "day" : f.GroupBy.ToLowerInvariant();

            // Query StockIssueDetails for revenue (selling price)
            var issueDetailsQuery = _db.StockIssueDetails
                .Include(d => d.StockIssue)
                .AsNoTracking()
                .Where(d => d.StockIssue.Status == DocumentStatus.DaXuatHang
                         && d.StockIssue.CreatedAt.Date >= from
                         && d.StockIssue.CreatedAt.Date <= to);

            if (f.WarehouseId.HasValue)
                issueDetailsQuery = issueDetailsQuery.Where(d => d.StockIssue.WarehouseId == f.WarehouseId.Value);

            var rows = new List<RevenueRowVM>();
            string groupByLabel = "Ngày";
            switch (f.GroupBy)
            {
                case "year":
                {
                    var y = await issueDetailsQuery
                        .GroupBy(d => d.StockIssue.CreatedAt.Year)
                        .Select(g => new { 
                            Year = g.Key, 
                            Total = g.Sum(z => (decimal)z.Quantity * z.UnitPrice), 
                            Qty = g.Sum(z => (decimal)z.Quantity) 
                        })
                        .OrderBy(x => x.Year)
                        .ToListAsync();
                    rows = y.Select(x => new RevenueRowVM { Period = x.Year.ToString(), TotalRevenue = x.Total, QtyOut = x.Qty }).ToList();
                    groupByLabel = "Năm";
                    break;
                }
                case "month":
                {
                    var m = await issueDetailsQuery
                        .GroupBy(d => new { d.StockIssue.CreatedAt.Year, d.StockIssue.CreatedAt.Month })
                        .Select(g => new { 
                            g.Key.Year, 
                            g.Key.Month, 
                            Total = g.Sum(z => (decimal)z.Quantity * z.UnitPrice), 
                            Qty = g.Sum(z => (decimal)z.Quantity) 
                        })
                        .OrderBy(x => x.Year).ThenBy(x => x.Month)
                        .ToListAsync();
                    rows = m.Select(x => new RevenueRowVM { Period = $"{x.Year}-{x.Month:00}", TotalRevenue = x.Total, QtyOut = x.Qty }).ToList();
                    groupByLabel = "Tháng";
                    break;
                }
                default:
                {
                    var d = await issueDetailsQuery
                        .GroupBy(d => d.StockIssue.CreatedAt.Date)
                        .Select(g => new { 
                            Day = g.Key, 
                            Total = g.Sum(z => (decimal)z.Quantity * z.UnitPrice), 
                            Qty = g.Sum(z => (decimal)z.Quantity) 
                        })
                        .OrderBy(x => x.Day)
                        .ToListAsync();
                    rows = d.Select(x => new RevenueRowVM { Period = x.Day.ToString("yyyy-MM-dd"), TotalRevenue = x.Total, QtyOut = x.Qty }).ToList();
                    groupByLabel = "Ngày";
                    break;
                }
            }

            decimal totalRevenue = rows.Sum(r => r.TotalRevenue);
            decimal totalQty = rows.Sum(r => r.QtyOut);

            string warehouseName = await GetWarehouseNameAsync(f.WarehouseId);

            // Tạo PDF
            QuestPDF.Settings.License = LicenseType.Community;
            var document = QuestPDF.Fluent.Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(2f, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Times New Roman"));

                    page.Content()
                        .Column(column =>
                        {
                            // Header - Form number và thông tin đơn vị
                            column.Item().Row(row =>
                            {
                                // Bên trái: Đơn vị, Mã QHNS
                                row.RelativeItem().Column(leftCol =>
                                {
                                    leftCol.Item().Text("Đơn vị: ").FontSize(10).FontColor(Colors.Black).FontFamily("Times New Roman");
                                    leftCol.Item().PaddingTop(4).Text("Mã QHNS: ").FontSize(10).FontColor(Colors.Black).FontFamily("Times New Roman");
                                });

                                // Bên phải: Mẫu số C30-HD
                                row.RelativeItem().Column(rightCol =>
                                {
                                    rightCol.Item().AlignRight().Text("Mẫu số C30 - HD")
                                        .FontSize(10)
                                        .Bold()
                                        .FontColor(Colors.Black)
                                        .FontFamily("Times New Roman");
                                    rightCol.Item().PaddingTop(2).AlignRight().Text("(Ban hành kèm theo thông tư 107/2017/TT-BTC")
                                        .FontSize(8)
                                        .Italic()
                                        .FontColor(Colors.Grey.Darken1)
                                        .FontFamily("Times New Roman");
                                    rightCol.Item().AlignRight().Text("ngày 24/11/2017)")
                                        .FontSize(8)
                                        .Italic()
                                        .FontColor(Colors.Grey.Darken1)
                                        .FontFamily("Times New Roman");
                                });
                            });

                            column.Item().PaddingTop(15);

                            // Tiêu đề chính
                            column.Item().AlignCenter().Text("BÁO CÁO DOANH THU")
                                .FontSize(20)
                                .Bold()
                                .FontColor(Colors.Black)
                                .FontFamily("Times New Roman");

                            column.Item().PaddingTop(8);
                            column.Item().AlignCenter().Text($"Ngày xuất: {DateTime.Now:dd/MM/yyyy HH:mm}")
                                .FontSize(10)
                                .FontColor(Colors.Grey.Darken2)
                                .FontFamily("Times New Roman");

                            column.Item().PaddingTop(20);

                            // Thông tin bộ lọc
                            column.Item().PaddingBottom(20).Column(filterCol =>
                            {
                                filterCol.Item().PaddingBottom(6).Row(row =>
                                {
                                    row.AutoItem().Text("Kho: ").FontSize(10).FontColor(Colors.Black).FontFamily("Times New Roman");
                                    row.RelativeItem().Text(warehouseName).FontSize(10).Bold().FontColor(Colors.Black).FontFamily("Times New Roman");
                                });

                                filterCol.Item().PaddingBottom(6).Row(row =>
                                {
                                    row.AutoItem().Text("Từ ngày: ").FontSize(10).FontColor(Colors.Black).FontFamily("Times New Roman");
                                    row.RelativeItem().Text($"{from:dd/MM/yyyy} đến {to:dd/MM/yyyy}").FontSize(10).Bold().FontColor(Colors.Black).FontFamily("Times New Roman");
                                });

                                filterCol.Item().Row(row =>
                                {
                                    row.AutoItem().Text("Nhóm theo: ").FontSize(10).FontColor(Colors.Black).FontFamily("Times New Roman");
                                    row.RelativeItem().Text(groupByLabel).FontSize(10).Bold().FontColor(Colors.Black).FontFamily("Times New Roman");
                                });
                            });

                            // Bảng dữ liệu
                            column.Item().Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.RelativeColumn(2f); // Kỳ
                                    columns.RelativeColumn(2f); // Doanh thu
                                    columns.RelativeColumn(1.5f); // Số lượng xuất
                                });

                                table.Header(header =>
                                {
                                    header.Cell().Element(HeaderCellStyle).Text(groupByLabel).Bold().FontSize(11).FontFamily("Times New Roman");
                                    header.Cell().Element(HeaderCellStyle).AlignRight().Text("Doanh thu").Bold().FontSize(11).FontFamily("Times New Roman");
                                    header.Cell().Element(HeaderCellStyle).AlignRight().Text("Số lượng xuất").Bold().FontSize(11).FontFamily("Times New Roman");
                                });

                                int rowIndex = 0;
                                foreach (var r in rows)
                                {
                                    bool isEvenRow = rowIndex % 2 == 0;
                                    table.Cell().Element(c => DataCellStyle(c, isEvenRow)).Text(r.Period).FontSize(10).FontFamily("Times New Roman");
                                    table.Cell().Element(c => DataCellStyle(c, isEvenRow)).AlignRight().Text(FormatMoney(r.TotalRevenue)).FontSize(10).FontFamily("Times New Roman").Bold();
                                    table.Cell().Element(c => DataCellStyle(c, isEvenRow)).AlignRight().Text(FormatQty(r.QtyOut)).FontSize(10).FontFamily("Times New Roman");
                                    rowIndex++;
                                }
                            });

                            column.Item().PaddingTop(25).PaddingBottom(15).LineHorizontal(1).LineColor(Colors.Black);

                            // Tổng hợp
                            column.Item().PaddingTop(10).Column(summaryCol =>
                            {
                                summaryCol.Item().PaddingBottom(8).Row(row =>
                                {
                                    row.AutoItem().Text("Tổng số dòng: ").FontSize(11).FontColor(Colors.Black).FontFamily("Times New Roman");
                                    row.RelativeItem().Text(rows.Count.ToString()).FontSize(11).Bold().FontColor(Colors.Black).FontFamily("Times New Roman");
                                });

                                summaryCol.Item().PaddingBottom(8).Row(row =>
                                {
                                    row.AutoItem().Text("Tổng số lượng xuất: ").FontSize(11).FontColor(Colors.Black).FontFamily("Times New Roman");
                                    row.RelativeItem().Text(FormatQty(totalQty)).FontSize(11).Bold().FontFamily("Times New Roman").FontColor(Colors.Black);
                                });

                                summaryCol.Item().Row(row =>
                                {
                                    row.AutoItem().Text("Tổng doanh thu: ").FontSize(11).FontColor(Colors.Black).FontFamily("Times New Roman");
                                    row.RelativeItem().Text(FormatMoney(totalRevenue)).FontSize(12).Bold().FontFamily("Times New Roman").FontColor(Colors.Black);
                                });
                            });
                        });

                    page.Footer()
                        .PaddingTop(10)
                        .BorderTop(1)
                        .BorderColor(Colors.Grey.Lighten1)
                        .AlignCenter()
                        .Text(x =>
                        {
                            x.Span("Trang ").FontSize(9).FontColor(Colors.Grey.Medium).FontFamily("Times New Roman");
                            x.CurrentPageNumber().FontSize(9).FontColor(Colors.Grey.Darken1).Bold().FontFamily("Times New Roman");
                            x.Span(" / ").FontSize(9).FontColor(Colors.Grey.Medium).FontFamily("Times New Roman");
                            x.TotalPages().FontSize(9).FontColor(Colors.Grey.Darken1).Bold().FontFamily("Times New Roman");
                        });
                });
            });

            var pdfBytes = document.GeneratePdf();
            var fileName = $"DoanhThu_{from:yyyyMMdd}_{to:yyyyMMdd}.pdf";
            return File(pdfBytes, "application/pdf", fileName);
        }

        // ==========================================================================================
        // BÁO CÁO LỢI NHUẬN (PROFIT & LOSS)
        [HttpGet]
        [RequirePermission("Reports", "Read")]
        public async Task<IActionResult> ProfitLoss([FromQuery] ReportFilterVM f)
        {
            var (from, to) = NormRange(f.From, f.To);
            f.GroupBy = string.IsNullOrWhiteSpace(f.GroupBy) ? "day" : f.GroupBy.ToLowerInvariant();

            ViewBag.Warehouses = new SelectList(
                await GetCachedWarehousesAsync(f.WarehouseId),
                "Value", "Text", f.WarehouseId?.ToString());
            ViewBag.Filter = f;

            // Tính Revenue từ StockBalances (OutValue)
            var revenueQuery = _db.StockBalances.AsNoTracking()
                .Where(x => x.Date >= from && x.Date <= to);

            if (f.WarehouseId.HasValue)
                revenueQuery = revenueQuery.Where(x => x.WarehouseId == f.WarehouseId.Value);

            // Tính COGS từ StockIssueDetails (đã xuất hàng)
            var cogsQuery = _db.StockIssueDetails
                .AsNoTracking()
                .Include(d => d.StockIssue)
                .Where(d => d.StockIssue.Status == DocumentStatus.DaXuatHang
                         && d.StockIssue.CreatedAt.Date >= from
                         && d.StockIssue.CreatedAt.Date <= to);

            if (f.WarehouseId.HasValue)
                cogsQuery = cogsQuery.Where(d => d.StockIssue.WarehouseId == f.WarehouseId.Value);

            var data = new List<ProfitLossReportVM>();

            switch (f.GroupBy)
            {
                case "year":
                {
                    var revenueData = await revenueQuery
                        .GroupBy(x => x.Date.Year)
                        .Select(g => new { Year = g.Key, Revenue = g.Sum(z => z.OutValue) })
                        .ToListAsync();

                    var cogsData = await cogsQuery
                        .GroupBy(d => d.StockIssue.CreatedAt.Year)
                        .Select(g => new { Year = g.Key, COGS = g.Sum(z => (z.CostPrice ?? z.UnitPrice) * (decimal)z.Quantity) })
                        .ToListAsync();

                    var years = revenueData.Select(r => r.Year)
                        .Union(cogsData.Select(c => c.Year))
                        .OrderBy(y => y)
                        .ToList();

                    foreach (var year in years)
                    {
                        var revenue = revenueData.FirstOrDefault(r => r.Year == year)?.Revenue ?? 0m;
                        var cogs = cogsData.FirstOrDefault(c => c.Year == year)?.COGS ?? 0m;
                        data.Add(new ProfitLossReportVM
                        {
                            Period = year.ToString(),
                            Revenue = revenue,
                            COGS = cogs
                        });
                    }
                    break;
                }
                case "month":
                {
                    var revenueData = await revenueQuery
                        .GroupBy(x => new { x.Date.Year, x.Date.Month })
                        .Select(g => new { g.Key.Year, g.Key.Month, Revenue = g.Sum(z => z.OutValue) })
                        .OrderBy(x => x.Year).ThenBy(x => x.Month)
                        .ToListAsync();

                    var cogsData = await cogsQuery
                        .GroupBy(d => new { d.StockIssue.CreatedAt.Year, d.StockIssue.CreatedAt.Month })
                        .Select(g => new { g.Key.Year, g.Key.Month, COGS = g.Sum(z => (z.CostPrice ?? z.UnitPrice) * (decimal)z.Quantity) })
                        .OrderBy(x => x.Year).ThenBy(x => x.Month)
                        .ToListAsync();

                    var periods = revenueData.Select(r => new { r.Year, r.Month })
                        .Union(cogsData.Select(c => new { c.Year, c.Month }))
                        .OrderBy(p => p.Year).ThenBy(p => p.Month)
                        .Distinct()
                        .ToList();

                    foreach (var period in periods)
                    {
                        var revenue = revenueData.FirstOrDefault(r => r.Year == period.Year && r.Month == period.Month)?.Revenue ?? 0m;
                        var cogs = cogsData.FirstOrDefault(c => c.Year == period.Year && c.Month == period.Month)?.COGS ?? 0m;
                        data.Add(new ProfitLossReportVM
                        {
                            Period = $"{period.Year}-{period.Month:00}",
                            Revenue = revenue,
                            COGS = cogs
                        });
                    }
                    break;
                }
                default: // day
                {
                    var revenueData = await revenueQuery
                        .GroupBy(x => x.Date.Date)
                        .Select(g => new { Day = g.Key, Revenue = g.Sum(z => z.OutValue) })
                        .OrderBy(x => x.Day)
                        .ToListAsync();

                    var cogsData = await cogsQuery
                        .GroupBy(d => d.StockIssue.CreatedAt.Date)
                        .Select(g => new { Day = g.Key, COGS = g.Sum(z => (z.CostPrice ?? z.UnitPrice) * (decimal)z.Quantity) })
                        .OrderBy(x => x.Day)
                        .ToListAsync();

                    var days = revenueData.Select(r => r.Day)
                        .Union(cogsData.Select(c => c.Day))
                        .OrderBy(d => d)
                        .Distinct()
                        .ToList();

                    foreach (var day in days)
                    {
                        var revenue = revenueData.FirstOrDefault(r => r.Day == day)?.Revenue ?? 0m;
                        var cogs = cogsData.FirstOrDefault(c => c.Day == day)?.COGS ?? 0m;
                        data.Add(new ProfitLossReportVM
                        {
                            Period = day.ToString("yyyy-MM-dd"),
                            Revenue = revenue,
                            COGS = cogs
                        });
                    }
                    break;
                }
            }

            ViewBag.TotalRevenue = data.Sum(x => x.Revenue);
            ViewBag.TotalCOGS = data.Sum(x => x.COGS);
            ViewBag.TotalGrossProfit = data.Sum(x => x.GrossProfit);
            ViewBag.TotalNetProfit = data.Sum(x => x.NetProfit);

            return View(data);
        }

        // Excel Export for ProfitLoss
        [HttpGet]
        [RequirePermission("Reports", "Read")]
        public async Task<IActionResult> ProfitLossExcel([FromQuery] ReportFilterVM f)
        {
            var (from, to) = NormRange(f.From, f.To);
            f.GroupBy = string.IsNullOrWhiteSpace(f.GroupBy) ? "day" : f.GroupBy.ToLowerInvariant();

            var revenueQuery = _db.StockBalances.AsNoTracking()
                .Where(x => x.Date >= from && x.Date <= to);

            if (f.WarehouseId.HasValue)
                revenueQuery = revenueQuery.Where(x => x.WarehouseId == f.WarehouseId.Value);

            var cogsQuery = _db.StockIssueDetails
                .AsNoTracking()
                .Include(d => d.StockIssue)
                .Where(d => d.StockIssue.Status == DocumentStatus.DaXuatHang
                         && d.StockIssue.CreatedAt.Date >= from
                         && d.StockIssue.CreatedAt.Date <= to);

            if (f.WarehouseId.HasValue)
                cogsQuery = cogsQuery.Where(d => d.StockIssue.WarehouseId == f.WarehouseId.Value);

            var data = new List<ProfitLossReportVM>();

            switch (f.GroupBy)
            {
                case "year":
                {
                    var revenueData = await revenueQuery
                        .GroupBy(x => x.Date.Year)
                        .Select(g => new { Year = g.Key, Revenue = g.Sum(z => z.OutValue) })
                        .ToListAsync();

                    var cogsData = await cogsQuery
                        .GroupBy(d => d.StockIssue.CreatedAt.Year)
                        .Select(g => new { Year = g.Key, COGS = g.Sum(z => (z.CostPrice ?? z.UnitPrice) * (decimal)z.Quantity) })
                        .ToListAsync();

                    var years = revenueData.Select(r => r.Year)
                        .Union(cogsData.Select(c => c.Year))
                        .OrderBy(y => y)
                        .ToList();

                    foreach (var year in years)
                    {
                        var revenue = revenueData.FirstOrDefault(r => r.Year == year)?.Revenue ?? 0m;
                        var cogs = cogsData.FirstOrDefault(c => c.Year == year)?.COGS ?? 0m;
                        data.Add(new ProfitLossReportVM
                        {
                            Period = year.ToString(),
                            Revenue = revenue,
                            COGS = cogs
                        });
                    }
                    break;
                }
                case "month":
                {
                    var revenueData = await revenueQuery
                        .GroupBy(x => new { x.Date.Year, x.Date.Month })
                        .Select(g => new { g.Key.Year, g.Key.Month, Revenue = g.Sum(z => z.OutValue) })
                        .OrderBy(x => x.Year).ThenBy(x => x.Month)
                        .ToListAsync();

                    var cogsData = await cogsQuery
                        .GroupBy(d => new { d.StockIssue.CreatedAt.Year, d.StockIssue.CreatedAt.Month })
                        .Select(g => new { g.Key.Year, g.Key.Month, COGS = g.Sum(z => (z.CostPrice ?? z.UnitPrice) * (decimal)z.Quantity) })
                        .OrderBy(x => x.Year).ThenBy(x => x.Month)
                        .ToListAsync();

                    var periods = revenueData.Select(r => new { r.Year, r.Month })
                        .Union(cogsData.Select(c => new { c.Year, c.Month }))
                        .OrderBy(p => p.Year).ThenBy(p => p.Month)
                        .Distinct()
                        .ToList();

                    foreach (var period in periods)
                    {
                        var revenue = revenueData.FirstOrDefault(r => r.Year == period.Year && r.Month == period.Month)?.Revenue ?? 0m;
                        var cogs = cogsData.FirstOrDefault(c => c.Year == period.Year && c.Month == period.Month)?.COGS ?? 0m;
                        data.Add(new ProfitLossReportVM
                        {
                            Period = $"{period.Year}-{period.Month:00}",
                            Revenue = revenue,
                            COGS = cogs
                        });
                    }
                    break;
                }
                default:
                {
                    var revenueData = await revenueQuery
                        .GroupBy(x => x.Date.Date)
                        .Select(g => new { Day = g.Key, Revenue = g.Sum(z => z.OutValue) })
                        .OrderBy(x => x.Day)
                        .ToListAsync();

                    var cogsData = await cogsQuery
                        .GroupBy(d => d.StockIssue.CreatedAt.Date)
                        .Select(g => new { Day = g.Key, COGS = g.Sum(z => (z.CostPrice ?? z.UnitPrice) * (decimal)z.Quantity) })
                        .OrderBy(x => x.Day)
                        .ToListAsync();

                    var days = revenueData.Select(r => r.Day)
                        .Union(cogsData.Select(c => c.Day))
                        .OrderBy(d => d)
                        .Distinct()
                        .ToList();

                    foreach (var day in days)
                    {
                        var revenue = revenueData.FirstOrDefault(r => r.Day == day)?.Revenue ?? 0m;
                        var cogs = cogsData.FirstOrDefault(c => c.Day == day)?.COGS ?? 0m;
                        data.Add(new ProfitLossReportVM
                        {
                            Period = day.ToString("yyyy-MM-dd"),
                            Revenue = revenue,
                            COGS = cogs
                        });
                    }
                    break;
                }
            }

            var excelData = data.Select(r => new
            {
                Ky = r.Period,
                DoanhThu = r.Revenue,
                GiaVonCOGS = r.COGS,
                LoiNhuanGop = r.GrossProfit,
                TySuatLNGop = r.GrossProfitMargin,
                LoiNhuanRong = r.NetProfit,
                TySuatLNRong = r.NetProfitMargin
            });

            var headers = new[] { "Kỳ", "Doanh thu", "Giá vốn (COGS)", "Lợi nhuận gộp", "Tỷ suất LN gộp (%)", "Lợi nhuận ròng", "Tỷ suất LN ròng (%)" };
            var bytes = _excel.ExportToExcel(excelData, "P&L", headers);
            var fileName = $"P&L_{from:yyyyMMdd}_{to:yyyyMMdd}.xlsx";
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }

        // PDF Export for ProfitLoss
        [HttpGet]
        [RequirePermission("Reports", "Read")]
        public async Task<IActionResult> ExportPdfProfitLoss([FromQuery] ReportFilterVM f)
        {
            var (from, to) = NormRange(f.From, f.To);
            f.GroupBy = string.IsNullOrWhiteSpace(f.GroupBy) ? "day" : f.GroupBy.ToLowerInvariant();

            var revenueQuery = _db.StockBalances.AsNoTracking()
                .Where(x => x.Date >= from && x.Date <= to);

            if (f.WarehouseId.HasValue)
                revenueQuery = revenueQuery.Where(x => x.WarehouseId == f.WarehouseId.Value);

            var cogsQuery = _db.StockIssueDetails
                .AsNoTracking()
                .Include(d => d.StockIssue)
                .Where(d => d.StockIssue.Status == DocumentStatus.DaXuatHang
                         && d.StockIssue.CreatedAt.Date >= from
                         && d.StockIssue.CreatedAt.Date <= to);

            if (f.WarehouseId.HasValue)
                cogsQuery = cogsQuery.Where(d => d.StockIssue.WarehouseId == f.WarehouseId.Value);

            var data = new List<ProfitLossReportVM>();

            switch (f.GroupBy)
            {
                case "year":
                {
                    var revenueData = await revenueQuery
                        .GroupBy(x => x.Date.Year)
                        .Select(g => new { Year = g.Key, Revenue = g.Sum(z => z.OutValue) })
                        .ToListAsync();

                    var cogsData = await cogsQuery
                        .GroupBy(d => d.StockIssue.CreatedAt.Year)
                        .Select(g => new { Year = g.Key, COGS = g.Sum(z => (z.CostPrice ?? z.UnitPrice) * (decimal)z.Quantity) })
                        .ToListAsync();

                    var years = revenueData.Select(r => r.Year)
                        .Union(cogsData.Select(c => c.Year))
                        .OrderBy(y => y)
                        .ToList();

                    foreach (var year in years)
                    {
                        var revenue = revenueData.FirstOrDefault(r => r.Year == year)?.Revenue ?? 0m;
                        var cogs = cogsData.FirstOrDefault(c => c.Year == year)?.COGS ?? 0m;
                        data.Add(new ProfitLossReportVM
                        {
                            Period = year.ToString(),
                            Revenue = revenue,
                            COGS = cogs
                        });
                    }
                    break;
                }
                case "month":
                {
                    var revenueData = await revenueQuery
                        .GroupBy(x => new { x.Date.Year, x.Date.Month })
                        .Select(g => new { g.Key.Year, g.Key.Month, Revenue = g.Sum(z => z.OutValue) })
                        .OrderBy(x => x.Year).ThenBy(x => x.Month)
                        .ToListAsync();

                    var cogsData = await cogsQuery
                        .GroupBy(d => new { d.StockIssue.CreatedAt.Year, d.StockIssue.CreatedAt.Month })
                        .Select(g => new { g.Key.Year, g.Key.Month, COGS = g.Sum(z => (z.CostPrice ?? z.UnitPrice) * (decimal)z.Quantity) })
                        .OrderBy(x => x.Year).ThenBy(x => x.Month)
                        .ToListAsync();

                    var periods = revenueData.Select(r => new { r.Year, r.Month })
                        .Union(cogsData.Select(c => new { c.Year, c.Month }))
                        .OrderBy(p => p.Year).ThenBy(p => p.Month)
                        .Distinct()
                        .ToList();

                    foreach (var period in periods)
                    {
                        var revenue = revenueData.FirstOrDefault(r => r.Year == period.Year && r.Month == period.Month)?.Revenue ?? 0m;
                        var cogs = cogsData.FirstOrDefault(c => c.Year == period.Year && c.Month == period.Month)?.COGS ?? 0m;
                        data.Add(new ProfitLossReportVM
                        {
                            Period = $"{period.Year}-{period.Month:00}",
                            Revenue = revenue,
                            COGS = cogs
                        });
                    }
                    break;
                }
                default:
                {
                    var revenueData = await revenueQuery
                        .GroupBy(x => x.Date.Date)
                        .Select(g => new { Day = g.Key, Revenue = g.Sum(z => z.OutValue) })
                        .OrderBy(x => x.Day)
                        .ToListAsync();

                    var cogsData = await cogsQuery
                        .GroupBy(d => d.StockIssue.CreatedAt.Date)
                        .Select(g => new { Day = g.Key, COGS = g.Sum(z => (z.CostPrice ?? z.UnitPrice) * (decimal)z.Quantity) })
                        .OrderBy(x => x.Day)
                        .ToListAsync();

                    var days = revenueData.Select(r => r.Day)
                        .Union(cogsData.Select(c => c.Day))
                        .OrderBy(d => d)
                        .Distinct()
                        .ToList();

                    foreach (var day in days)
                    {
                        var revenue = revenueData.FirstOrDefault(r => r.Day == day)?.Revenue ?? 0m;
                        var cogs = cogsData.FirstOrDefault(c => c.Day == day)?.COGS ?? 0m;
                        data.Add(new ProfitLossReportVM
                        {
                            Period = day.ToString("yyyy-MM-dd"),
                            Revenue = revenue,
                            COGS = cogs
                        });
                    }
                    break;
                }
            }

            decimal totalRevenue = data.Sum(r => r.Revenue);
            decimal totalCOGS = data.Sum(r => r.COGS);
            decimal totalGrossProfit = data.Sum(r => r.GrossProfit);
            decimal totalNetProfit = data.Sum(r => r.NetProfit);
            decimal avgGrossMargin = totalRevenue > 0 ? (totalGrossProfit / totalRevenue) * 100 : 0;
            decimal avgNetMargin = totalRevenue > 0 ? (totalNetProfit / totalRevenue) * 100 : 0;

            string warehouseName = await GetWarehouseNameAsync(f.WarehouseId);

            string groupByLabel = f.GroupBy == "year" ? "Năm" : f.GroupBy == "month" ? "Tháng" : "Ngày";

            // Tạo PDF
            QuestPDF.Settings.License = LicenseType.Community;
            var document = QuestPDF.Fluent.Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(2f, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Times New Roman"));

                    page.Content()
                        .Column(column =>
                        {
                            // Header - Form number và thông tin đơn vị
                            column.Item().Row(row =>
                            {
                                // Bên trái: Đơn vị, Mã QHNS
                                row.RelativeItem().Column(leftCol =>
                                {
                                    leftCol.Item().Text("Đơn vị: ").FontSize(10).FontColor(Colors.Black).FontFamily("Times New Roman");
                                    leftCol.Item().PaddingTop(4).Text("Mã QHNS: ").FontSize(10).FontColor(Colors.Black).FontFamily("Times New Roman");
                                });

                                // Bên phải: Mẫu số C30-HD
                                row.RelativeItem().Column(rightCol =>
                                {
                                    rightCol.Item().AlignRight().Text("Mẫu số C30 - HD")
                                        .FontSize(10)
                                        .Bold()
                                        .FontColor(Colors.Black)
                                        .FontFamily("Times New Roman");
                                    rightCol.Item().PaddingTop(2).AlignRight().Text("(Ban hành kèm theo thông tư 107/2017/TT-BTC")
                                        .FontSize(8)
                                        .Italic()
                                        .FontColor(Colors.Grey.Darken1)
                                        .FontFamily("Times New Roman");
                                    rightCol.Item().AlignRight().Text("ngày 24/11/2017)")
                                        .FontSize(8)
                                        .Italic()
                                        .FontColor(Colors.Grey.Darken1)
                                        .FontFamily("Times New Roman");
                                });
                            });

                            column.Item().PaddingTop(15);

                            // Tiêu đề chính
                            column.Item().AlignCenter().Text("BÁO CÁO LỢI NHUẬN (P&L)")
                                .FontSize(20)
                                .Bold()
                                .FontColor(Colors.Black)
                                .FontFamily("Times New Roman");

                            column.Item().PaddingTop(8);
                            column.Item().AlignCenter().Text($"Ngày xuất: {DateTime.Now:dd/MM/yyyy HH:mm}")
                                .FontSize(10)
                                .FontColor(Colors.Grey.Darken2)
                                .FontFamily("Times New Roman");

                            column.Item().PaddingTop(20);

                            // Thông tin bộ lọc
                            column.Item().PaddingBottom(20).Column(filterCol =>
                            {
                                filterCol.Item().PaddingBottom(6).Row(row =>
                                {
                                    row.AutoItem().Text("Kho: ").FontSize(10).FontColor(Colors.Black).FontFamily("Times New Roman");
                                    row.RelativeItem().Text(warehouseName).FontSize(10).Bold().FontColor(Colors.Black).FontFamily("Times New Roman");
                                });

                                filterCol.Item().PaddingBottom(6).Row(row =>
                                {
                                    row.AutoItem().Text("Từ ngày: ").FontSize(10).FontColor(Colors.Black).FontFamily("Times New Roman");
                                    row.RelativeItem().Text($"{from:dd/MM/yyyy} đến {to:dd/MM/yyyy}").FontSize(10).Bold().FontColor(Colors.Black).FontFamily("Times New Roman");
                                });

                                filterCol.Item().Row(row =>
                                {
                                    row.AutoItem().Text("Nhóm theo: ").FontSize(10).FontColor(Colors.Black).FontFamily("Times New Roman");
                                    row.RelativeItem().Text(groupByLabel).FontSize(10).Bold().FontColor(Colors.Black).FontFamily("Times New Roman");
                                });
                            });

                            // Bảng dữ liệu
                            column.Item().Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.RelativeColumn(1.2f); // Kỳ
                                    columns.RelativeColumn(1.5f); // Doanh thu
                                    columns.RelativeColumn(1.5f); // COGS
                                    columns.RelativeColumn(1.5f); // Lợi nhuận gộp
                                    columns.RelativeColumn(1f); // Tỷ suất LN gộp
                                    columns.RelativeColumn(1.5f); // Lợi nhuận ròng
                                    columns.RelativeColumn(1f); // Tỷ suất LN ròng
                                });

                                table.Header(header =>
                                {
                                    header.Cell().Element(HeaderCellStyle).Text("Kỳ").Bold().FontSize(11).FontFamily("Times New Roman");
                                    header.Cell().Element(HeaderCellStyle).AlignRight().Text("Doanh thu").Bold().FontSize(11).FontFamily("Times New Roman");
                                    header.Cell().Element(HeaderCellStyle).AlignRight().Text("Giá vốn (COGS)").Bold().FontSize(11).FontFamily("Times New Roman");
                                    header.Cell().Element(HeaderCellStyle).AlignRight().Text("Lợi nhuận gộp").Bold().FontSize(11).FontFamily("Times New Roman");
                                    header.Cell().Element(HeaderCellStyle).AlignRight().Text("Tỷ suất LN gộp (%)").Bold().FontSize(11).FontFamily("Times New Roman");
                                    header.Cell().Element(HeaderCellStyle).AlignRight().Text("Lợi nhuận ròng").Bold().FontSize(11).FontFamily("Times New Roman");
                                    header.Cell().Element(HeaderCellStyle).AlignRight().Text("Tỷ suất LN ròng (%)").Bold().FontSize(11).FontFamily("Times New Roman");
                                });

                                int rowIndex = 0;
                                foreach (var r in data)
                                {
                                    bool isEvenRow = rowIndex % 2 == 0;
                                    table.Cell().Element(c => DataCellStyle(c, isEvenRow)).Text(r.Period).FontSize(10).FontFamily("Times New Roman");
                                    table.Cell().Element(c => DataCellStyle(c, isEvenRow)).AlignRight().Text(FormatMoney(r.Revenue)).FontSize(10).FontFamily("Times New Roman");
                                    table.Cell().Element(c => DataCellStyle(c, isEvenRow)).AlignRight().Text(FormatMoney(r.COGS)).FontSize(10).FontFamily("Times New Roman");
                                    table.Cell().Element(c => DataCellStyle(c, isEvenRow)).AlignRight().Text(FormatMoney(r.GrossProfit)).FontSize(10).FontFamily("Times New Roman").Bold();
                                    table.Cell().Element(c => DataCellStyle(c, isEvenRow)).AlignRight().Text(r.GrossProfitMargin.ToString("N2") + "%").FontSize(10).FontFamily("Times New Roman");
                                    table.Cell().Element(c => DataCellStyle(c, isEvenRow)).AlignRight().Text(FormatMoney(r.NetProfit)).FontSize(10).FontFamily("Times New Roman").Bold();
                                    table.Cell().Element(c => DataCellStyle(c, isEvenRow)).AlignRight().Text(r.NetProfitMargin.ToString("N2") + "%").FontSize(10).FontFamily("Times New Roman");
                                    rowIndex++;
                                }
                            });

                            column.Item().PaddingTop(25).PaddingBottom(15).LineHorizontal(1).LineColor(Colors.Black);

                            // Tổng hợp
                            column.Item().PaddingTop(10).Column(summaryCol =>
                            {
                                summaryCol.Item().PaddingBottom(8).Row(row =>
                                {
                                    row.AutoItem().Text("Tổng số dòng: ").FontSize(11).FontColor(Colors.Black).FontFamily("Times New Roman");
                                    row.RelativeItem().Text(data.Count.ToString()).FontSize(11).Bold().FontColor(Colors.Black).FontFamily("Times New Roman");
                                });

                                summaryCol.Item().PaddingBottom(8).Row(row =>
                                {
                                    row.AutoItem().Text("Tổng doanh thu: ").FontSize(11).FontColor(Colors.Black).FontFamily("Times New Roman");
                                    row.RelativeItem().Text(FormatMoney(totalRevenue)).FontSize(11).Bold().FontFamily("Times New Roman").FontColor(Colors.Black);
                                });

                                summaryCol.Item().PaddingBottom(8).Row(row =>
                                {
                                    row.AutoItem().Text("Tổng giá vốn (COGS): ").FontSize(11).FontColor(Colors.Black).FontFamily("Times New Roman");
                                    row.RelativeItem().Text(FormatMoney(totalCOGS)).FontSize(11).Bold().FontFamily("Times New Roman").FontColor(Colors.Black);
                                });

                                summaryCol.Item().PaddingBottom(8).Row(row =>
                                {
                                    row.AutoItem().Text("Tổng lợi nhuận gộp: ").FontSize(11).FontColor(Colors.Black).FontFamily("Times New Roman");
                                    row.RelativeItem().Text(FormatMoney(totalGrossProfit)).FontSize(11).Bold().FontFamily("Times New Roman").FontColor(Colors.Black);
                                });

                                summaryCol.Item().PaddingBottom(8).Row(row =>
                                {
                                    row.AutoItem().Text("Tỷ suất lợi nhuận gộp trung bình: ").FontSize(11).FontColor(Colors.Black).FontFamily("Times New Roman");
                                    row.RelativeItem().Text(avgGrossMargin.ToString("N2") + "%").FontSize(11).Bold().FontFamily("Times New Roman").FontColor(Colors.Black);
                                });

                                summaryCol.Item().PaddingBottom(8).Row(row =>
                                {
                                    row.AutoItem().Text("Tổng lợi nhuận ròng: ").FontSize(11).FontColor(Colors.Black).FontFamily("Times New Roman");
                                    row.RelativeItem().Text(FormatMoney(totalNetProfit)).FontSize(12).Bold().FontFamily("Times New Roman").FontColor(Colors.Black);
                                });

                                summaryCol.Item().Row(row =>
                                {
                                    row.AutoItem().Text("Tỷ suất lợi nhuận ròng trung bình: ").FontSize(11).FontColor(Colors.Black).FontFamily("Times New Roman");
                                    row.RelativeItem().Text(avgNetMargin.ToString("N2") + "%").FontSize(11).Bold().FontFamily("Times New Roman").FontColor(Colors.Black);
                                });
                            });
                        });

                    page.Footer()
                        .PaddingTop(10)
                        .BorderTop(1)
                        .BorderColor(Colors.Grey.Lighten1)
                        .AlignCenter()
                        .Text(x =>
                        {
                            x.Span("Trang ").FontSize(9).FontColor(Colors.Grey.Medium).FontFamily("Times New Roman");
                            x.CurrentPageNumber().FontSize(9).FontColor(Colors.Grey.Darken1).Bold().FontFamily("Times New Roman");
                            x.Span(" / ").FontSize(9).FontColor(Colors.Grey.Medium).FontFamily("Times New Roman");
                            x.TotalPages().FontSize(9).FontColor(Colors.Grey.Darken1).Bold().FontFamily("Times New Roman");
                        });
                });
            });

            var pdfBytes = document.GeneratePdf();
            var fileName = $"P&L_{from:yyyyMMdd}_{to:yyyyMMdd}.pdf";
            return File(pdfBytes, "application/pdf", fileName);
        }

        // ==========================================================================================
        // BÁO CÁO TỶ LỆ QUAY VÒNG HÀNG TỒN KHO (TURNOVER RATE)
        [HttpGet]
        [RequirePermission("Reports", "Read")]
        public async Task<IActionResult> TurnoverRate([FromQuery] ReportFilterVM f)
        {
            var (from, to) = NormRange(f.From, f.To);
            int daysInPeriod = (to - from).Days + 1;

            ViewBag.Warehouses = new SelectList(
                await GetCachedWarehousesAsync(f.WarehouseId),
                "Value", "Text", f.WarehouseId?.ToString());
            ViewBag.Filter = f;

            // Lấy danh sách materials có phát sinh xuất trong kỳ
            var issuedQuery = _db.StockIssueDetails
                .AsNoTracking()
                .Include(d => d.StockIssue)
                .Include(d => d.Material)
                .Where(d => d.StockIssue.Status == DocumentStatus.DaXuatHang
                         && d.StockIssue.CreatedAt.Date >= from
                         && d.StockIssue.CreatedAt.Date <= to);

            if (f.WarehouseId.HasValue)
                issuedQuery = issuedQuery.Where(d => d.StockIssue.WarehouseId == f.WarehouseId.Value);

            // Group by MaterialId và WarehouseId để tính TotalIssued
            var issuedData = await issuedQuery
                .GroupBy(d => new { d.MaterialId, WarehouseId = d.StockIssue.WarehouseId })
                .Select(g => new
                {
                    g.Key.MaterialId,
                    g.Key.WarehouseId,
                    TotalIssued = g.Sum(d => (decimal)d.Quantity),
                    MaterialCode = g.First().Material.Code,
                    MaterialName = g.First().Material.Name,
                    Unit = g.First().Material.Unit
                })
                .ToListAsync();

            var data = new List<TurnoverRateVM>();

            foreach (var item in issuedData)
            {
                // Lấy tồn cuối kỳ từ StockLots
                var endStock = await _db.StockLots
                    .AsNoTracking()
                    .Where(l => l.MaterialId == item.MaterialId 
                            && l.WarehouseId == item.WarehouseId 
                            && l.Quantity > 0)
                    .SumAsync(l => l.Quantity);

                // Tính InQty và OutQty trong kỳ từ StockBalance
                var balanceInPeriod = await _db.StockBalances
                    .AsNoTracking()
                    .Where(b => b.MaterialId == item.MaterialId
                             && b.WarehouseId == item.WarehouseId
                             && b.Date >= from
                             && b.Date <= to)
                    .GroupBy(b => 1)
                    .Select(g => new
                    {
                        TotalInQty = g.Sum(b => b.InQty),
                        TotalOutQty = g.Sum(b => b.OutQty)
                    })
                    .FirstOrDefaultAsync();

                decimal inQty = balanceInPeriod?.TotalInQty ?? 0m;
                decimal outQty = balanceInPeriod?.TotalOutQty ?? 0m;

                // Tồn đầu kỳ = Tồn cuối - InQty + OutQty
                decimal beginStock = endStock - inQty + outQty;
                if (beginStock < 0) beginStock = 0;

                // AverageStock = (Tồn đầu + Tồn cuối) / 2
                decimal averageStock = (beginStock + endStock) / 2;

                // Tính TurnoverRate và DaysToTurnover
                decimal turnoverRate = averageStock > 0 ? item.TotalIssued / averageStock : 0;
                decimal daysToTurnover = turnoverRate > 0 ? daysInPeriod / turnoverRate : 999;

                // Lấy WarehouseName
                var warehouse = await _db.Warehouses.FindAsync(item.WarehouseId);
                string warehouseName = warehouse?.Name ?? "N/A";

                data.Add(new TurnoverRateVM
                {
                    MaterialId = item.MaterialId,
                    MaterialCode = item.MaterialCode,
                    MaterialName = item.MaterialName,
                    Unit = item.Unit,
                    WarehouseName = warehouseName,
                    AverageStock = Math.Round(averageStock, 2),
                    TotalIssued = Math.Round(item.TotalIssued, 2),
                    DaysToTurnover = Math.Round(daysToTurnover, 1)
                });
            }

            // Order by TurnoverRate DESC (fastest first)
            data = data.OrderByDescending(d => d.TurnoverRate).ToList();

            ViewBag.TotalMaterials = data.Count;
            ViewBag.FastCount = data.Count(d => d.Category == "Fast");
            ViewBag.MediumCount = data.Count(d => d.Category == "Medium");
            ViewBag.SlowCount = data.Count(d => d.Category == "Slow");

            return View(data);
        }

        // Excel Export for TurnoverRate
        [HttpGet]
        [RequirePermission("Reports", "Read")]
        public async Task<IActionResult> TurnoverRateExcel([FromQuery] ReportFilterVM f)
        {
            var (from, to) = NormRange(f.From, f.To);
            int daysInPeriod = (to - from).Days + 1;

            var issuedQuery = _db.StockIssueDetails
                .AsNoTracking()
                .Include(d => d.StockIssue)
                .Include(d => d.Material)
                .Where(d => d.StockIssue.Status == DocumentStatus.DaXuatHang
                         && d.StockIssue.CreatedAt.Date >= from
                         && d.StockIssue.CreatedAt.Date <= to);

            if (f.WarehouseId.HasValue)
                issuedQuery = issuedQuery.Where(d => d.StockIssue.WarehouseId == f.WarehouseId.Value);

            var issuedData = await issuedQuery
                .GroupBy(d => new { d.MaterialId, WarehouseId = d.StockIssue.WarehouseId })
                .Select(g => new
                {
                    g.Key.MaterialId,
                    g.Key.WarehouseId,
                    TotalIssued = g.Sum(d => (decimal)d.Quantity),
                    MaterialCode = g.First().Material.Code,
                    MaterialName = g.First().Material.Name,
                    Unit = g.First().Material.Unit
                })
                .ToListAsync();

            var data = new List<TurnoverRateVM>();

            foreach (var item in issuedData)
            {
                var endStock = await _db.StockLots
                    .AsNoTracking()
                    .Where(l => l.MaterialId == item.MaterialId 
                            && l.WarehouseId == item.WarehouseId 
                            && l.Quantity > 0)
                    .SumAsync(l => l.Quantity);

                var balanceInPeriod = await _db.StockBalances
                    .AsNoTracking()
                    .Where(b => b.MaterialId == item.MaterialId
                             && b.WarehouseId == item.WarehouseId
                             && b.Date >= from
                             && b.Date <= to)
                    .GroupBy(b => 1)
                    .Select(g => new
                    {
                        TotalInQty = g.Sum(b => b.InQty),
                        TotalOutQty = g.Sum(b => b.OutQty)
                    })
                    .FirstOrDefaultAsync();

                decimal inQty = balanceInPeriod?.TotalInQty ?? 0m;
                decimal outQty = balanceInPeriod?.TotalOutQty ?? 0m;
                decimal beginStock = Math.Max(0, endStock - inQty + outQty);
                decimal averageStock = (beginStock + endStock) / 2;
                decimal turnoverRate = averageStock > 0 ? item.TotalIssued / averageStock : 0;
                decimal daysToTurnover = turnoverRate > 0 ? daysInPeriod / turnoverRate : 999;

                var warehouse = await _db.Warehouses.FindAsync(item.WarehouseId);
                string warehouseName = warehouse?.Name ?? "N/A";

                data.Add(new TurnoverRateVM
                {
                    MaterialId = item.MaterialId,
                    MaterialCode = item.MaterialCode,
                    MaterialName = item.MaterialName,
                    Unit = item.Unit,
                    WarehouseName = warehouseName,
                    AverageStock = Math.Round(averageStock, 2),
                    TotalIssued = Math.Round(item.TotalIssued, 2),
                    DaysToTurnover = Math.Round(daysToTurnover, 1)
                });
            }

            data = data.OrderByDescending(d => d.TurnoverRate).ToList();

            var excelData = data.Select(r => new
            {
                MaNL = r.MaterialCode,
                TenNL = r.MaterialName,
                Kho = r.WarehouseName,
                TonTB = r.AverageStock,
                TongXuat = r.TotalIssued,
                TurnoverRate = r.TurnoverRate,
                SoNgayQuayVong = r.DaysToTurnover,
                PhanLoai = r.Category
            });

            var headers = new[] { "Mã NL", "Tên NL", "Kho", "Tồn TB", "Tổng xuất", "Turnover Rate", "Số ngày quay vòng", "Phân loại" };
            var bytes = _excel.ExportToExcel(excelData, "Turnover Rate", headers);
            var fileName = $"TurnoverRate_{from:yyyyMMdd}_{to:yyyyMMdd}.xlsx";
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }

        // PDF Export for TurnoverRate
        [HttpGet]
        [RequirePermission("Reports", "Read")]
        public async Task<IActionResult> ExportPdfTurnoverRate([FromQuery] ReportFilterVM f)
        {
            var (from, to) = NormRange(f.From, f.To);
            int daysInPeriod = (to - from).Days + 1;

            var issuedQuery = _db.StockIssueDetails
                .AsNoTracking()
                .Include(d => d.StockIssue)
                .Include(d => d.Material)
                .Where(d => d.StockIssue.Status == DocumentStatus.DaXuatHang
                         && d.StockIssue.CreatedAt.Date >= from
                         && d.StockIssue.CreatedAt.Date <= to);

            if (f.WarehouseId.HasValue)
                issuedQuery = issuedQuery.Where(d => d.StockIssue.WarehouseId == f.WarehouseId.Value);

            var issuedData = await issuedQuery
                .GroupBy(d => new { d.MaterialId, WarehouseId = d.StockIssue.WarehouseId })
                .Select(g => new
                {
                    g.Key.MaterialId,
                    g.Key.WarehouseId,
                    TotalIssued = g.Sum(d => (decimal)d.Quantity),
                    MaterialCode = g.First().Material.Code,
                    MaterialName = g.First().Material.Name,
                    Unit = g.First().Material.Unit
                })
                .ToListAsync();

            var data = new List<TurnoverRateVM>();

            foreach (var item in issuedData)
            {
                var endStock = await _db.StockLots
                    .AsNoTracking()
                    .Where(l => l.MaterialId == item.MaterialId 
                            && l.WarehouseId == item.WarehouseId 
                            && l.Quantity > 0)
                    .SumAsync(l => l.Quantity);

                var balanceInPeriod = await _db.StockBalances
                    .AsNoTracking()
                    .Where(b => b.MaterialId == item.MaterialId
                             && b.WarehouseId == item.WarehouseId
                             && b.Date >= from
                             && b.Date <= to)
                    .GroupBy(b => 1)
                    .Select(g => new
                    {
                        TotalInQty = g.Sum(b => b.InQty),
                        TotalOutQty = g.Sum(b => b.OutQty)
                    })
                    .FirstOrDefaultAsync();

                decimal inQty = balanceInPeriod?.TotalInQty ?? 0m;
                decimal outQty = balanceInPeriod?.TotalOutQty ?? 0m;
                decimal beginStock = Math.Max(0, endStock - inQty + outQty);
                decimal averageStock = (beginStock + endStock) / 2;
                decimal turnoverRate = averageStock > 0 ? item.TotalIssued / averageStock : 0;
                decimal daysToTurnover = turnoverRate > 0 ? daysInPeriod / turnoverRate : 999;

                var itemWarehouse = await _db.Warehouses.FindAsync(item.WarehouseId);
                string itemWarehouseName = itemWarehouse?.Name ?? "N/A";

                data.Add(new TurnoverRateVM
                {
                    MaterialId = item.MaterialId,
                    MaterialCode = item.MaterialCode,
                    MaterialName = item.MaterialName,
                    Unit = item.Unit,
                    WarehouseName = itemWarehouseName,
                    AverageStock = Math.Round(averageStock, 2),
                    TotalIssued = Math.Round(item.TotalIssued, 2),
                    DaysToTurnover = Math.Round(daysToTurnover, 1)
                });
            }

            data = data.OrderByDescending(d => d.TurnoverRate).ToList();

            int totalMaterials = data.Count;
            int fastCount = data.Count(d => d.Category == "Fast");
            int mediumCount = data.Count(d => d.Category == "Medium");
            int slowCount = data.Count(d => d.Category == "Slow");

            string warehouseName = await GetWarehouseNameAsync(f.WarehouseId);

            // Tạo PDF
            QuestPDF.Settings.License = LicenseType.Community;
            var document = QuestPDF.Fluent.Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(2f, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Times New Roman"));

                    page.Content()
                        .Column(column =>
                        {
                            // Header - Form number và thông tin đơn vị
                            column.Item().Row(row =>
                            {
                                // Bên trái: Đơn vị, Mã QHNS
                                row.RelativeItem().Column(leftCol =>
                                {
                                    leftCol.Item().Text("Đơn vị: ").FontSize(10).FontColor(Colors.Black).FontFamily("Times New Roman");
                                    leftCol.Item().PaddingTop(4).Text("Mã QHNS: ").FontSize(10).FontColor(Colors.Black).FontFamily("Times New Roman");
                                });

                                // Bên phải: Mẫu số C30-HD
                                row.RelativeItem().Column(rightCol =>
                                {
                                    rightCol.Item().AlignRight().Text("Mẫu số C30 - HD")
                                        .FontSize(10)
                                        .Bold()
                                        .FontColor(Colors.Black)
                                        .FontFamily("Times New Roman");
                                    rightCol.Item().PaddingTop(2).AlignRight().Text("(Ban hành kèm theo thông tư 107/2017/TT-BTC")
                                        .FontSize(8)
                                        .Italic()
                                        .FontColor(Colors.Grey.Darken1)
                                        .FontFamily("Times New Roman");
                                    rightCol.Item().AlignRight().Text("ngày 24/11/2017)")
                                        .FontSize(8)
                                        .Italic()
                                        .FontColor(Colors.Grey.Darken1)
                                        .FontFamily("Times New Roman");
                                });
                            });

                            column.Item().PaddingTop(15);

                            // Tiêu đề chính
                            column.Item().AlignCenter().Text("BÁO CÁO TỶ LỆ QUAY VÒNG HÀNG TỒN KHO")
                                .FontSize(20)
                                .Bold()
                                .FontColor(Colors.Black)
                                .FontFamily("Times New Roman");

                            column.Item().PaddingTop(8);
                            column.Item().AlignCenter().Text($"Ngày xuất: {DateTime.Now:dd/MM/yyyy HH:mm}")
                                .FontSize(10)
                                .FontColor(Colors.Grey.Darken2)
                                .FontFamily("Times New Roman");

                            column.Item().PaddingTop(20);

                            // Thông tin bộ lọc
                            column.Item().PaddingBottom(20).Column(filterCol =>
                            {
                                filterCol.Item().PaddingBottom(6).Row(row =>
                                {
                                    row.AutoItem().Text("Kho: ").FontSize(10).FontColor(Colors.Black).FontFamily("Times New Roman");
                                    row.RelativeItem().Text(warehouseName).FontSize(10).Bold().FontColor(Colors.Black).FontFamily("Times New Roman");
                                });

                                filterCol.Item().Row(row =>
                                {
                                    row.AutoItem().Text("Từ ngày: ").FontSize(10).FontColor(Colors.Black).FontFamily("Times New Roman");
                                    row.RelativeItem().Text($"{from:dd/MM/yyyy} đến {to:dd/MM/yyyy}").FontSize(10).Bold().FontColor(Colors.Black).FontFamily("Times New Roman");
                                });
                            });

                            // Bảng dữ liệu
                            column.Item().Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.RelativeColumn(1.2f); // Mã NL
                                    columns.RelativeColumn(2f); // Tên NL
                                    columns.RelativeColumn(1.2f); // Kho
                                    columns.RelativeColumn(1f); // Tồn TB
                                    columns.RelativeColumn(1f); // Tổng xuất
                                    columns.RelativeColumn(1f); // Turnover Rate
                                    columns.RelativeColumn(1.2f); // Số ngày quay vòng
                                    columns.RelativeColumn(1f); // Phân loại
                                });

                                table.Header(header =>
                                {
                                    header.Cell().Element(HeaderCellStyle).Text("Mã NL").Bold().FontSize(11).FontFamily("Times New Roman");
                                    header.Cell().Element(HeaderCellStyle).Text("Tên nguyên liệu").Bold().FontSize(11).FontFamily("Times New Roman");
                                    header.Cell().Element(HeaderCellStyle).Text("Kho").Bold().FontSize(11).FontFamily("Times New Roman");
                                    header.Cell().Element(HeaderCellStyle).AlignRight().Text("Tồn TB").Bold().FontSize(11).FontFamily("Times New Roman");
                                    header.Cell().Element(HeaderCellStyle).AlignRight().Text("Tổng xuất").Bold().FontSize(11).FontFamily("Times New Roman");
                                    header.Cell().Element(HeaderCellStyle).AlignRight().Text("Tỷ lệ quay vòng").Bold().FontSize(11).FontFamily("Times New Roman");
                                    header.Cell().Element(HeaderCellStyle).AlignRight().Text("Số ngày quay vòng").Bold().FontSize(11).FontFamily("Times New Roman");
                                    header.Cell().Element(HeaderCellStyle).AlignCenter().Text("Phân loại").Bold().FontSize(11).FontFamily("Times New Roman");
                                });

                                int rowIndex = 0;
                                foreach (var r in data)
                                {
                                    bool isEvenRow = rowIndex % 2 == 0;
                                    string categoryColor = r.Category == "Fast" ? Colors.Green.Darken2 : r.Category == "Medium" ? Colors.Orange.Darken2 : Colors.Red.Darken2;
                                    string categoryText = r.Category == "Fast" ? "Nhanh" : r.Category == "Medium" ? "Trung bình" : "Chậm";
                                    
                                    table.Cell().Element(c => DataCellStyle(c, isEvenRow)).Text(r.MaterialCode).FontSize(10).FontFamily("Times New Roman");
                                    table.Cell().Element(c => DataCellStyle(c, isEvenRow)).Text(r.MaterialName).FontSize(10).FontFamily("Times New Roman");
                                    table.Cell().Element(c => DataCellStyle(c, isEvenRow)).Text(r.WarehouseName ?? "Không có").FontSize(10).FontFamily("Times New Roman");
                                    table.Cell().Element(c => DataCellStyle(c, isEvenRow)).AlignRight().Text(FormatQty(r.AverageStock)).FontSize(10).FontFamily("Times New Roman");
                                    table.Cell().Element(c => DataCellStyle(c, isEvenRow)).AlignRight().Text(FormatQty(r.TotalIssued)).FontSize(10).FontFamily("Times New Roman");
                                    table.Cell().Element(c => DataCellStyle(c, isEvenRow)).AlignRight().Text(r.TurnoverRate.ToString("N2")).FontSize(10).FontFamily("Times New Roman").Bold();
                                    table.Cell().Element(c => DataCellStyle(c, isEvenRow)).AlignRight().Text(r.DaysToTurnover == 999 ? "Không có" : r.DaysToTurnover.ToString("N1")).FontSize(10).FontFamily("Times New Roman");
                                    table.Cell().Element(c => DataCellStyle(c, isEvenRow)).AlignCenter().Text(categoryText).FontSize(10).Bold().FontColor(categoryColor).FontFamily("Times New Roman");
                                    rowIndex++;
                                }
                            });

                            column.Item().PaddingTop(25).PaddingBottom(15).LineHorizontal(1).LineColor(Colors.Black);

                            // Tổng hợp
                            column.Item().PaddingTop(10).Column(summaryCol =>
                            {
                                summaryCol.Item().PaddingBottom(8).Row(row =>
                                {
                                    row.AutoItem().Text("Tổng số nguyên liệu: ").FontSize(11).FontColor(Colors.Black).FontFamily("Times New Roman");
                                    row.RelativeItem().Text(totalMaterials.ToString()).FontSize(11).Bold().FontColor(Colors.Black).FontFamily("Times New Roman");
                                });

                                summaryCol.Item().PaddingBottom(8).Row(row =>
                                {
                                    row.AutoItem().Text("Quay vòng nhanh (>12): ").FontSize(11).FontColor(Colors.Black).FontFamily("Times New Roman");
                                    row.RelativeItem().Text(fastCount.ToString()).FontSize(11).Bold().FontColor(Colors.Black).FontFamily("Times New Roman");
                                });

                                summaryCol.Item().PaddingBottom(8).Row(row =>
                                {
                                    row.AutoItem().Text("Quay vòng trung bình (4-12): ").FontSize(11).FontColor(Colors.Black).FontFamily("Times New Roman");
                                    row.RelativeItem().Text(mediumCount.ToString()).FontSize(11).Bold().FontColor(Colors.Black).FontFamily("Times New Roman");
                                });

                                summaryCol.Item().Row(row =>
                                {
                                    row.AutoItem().Text("Quay vòng chậm (<4): ").FontSize(11).FontColor(Colors.Black).FontFamily("Times New Roman");
                                    row.RelativeItem().Text(slowCount.ToString()).FontSize(11).Bold().FontColor(Colors.Black).FontFamily("Times New Roman");
                                });
                            });
                        });

                    page.Footer()
                        .PaddingTop(10)
                        .BorderTop(1)
                        .BorderColor(Colors.Grey.Lighten1)
                        .AlignCenter()
                        .Text(x =>
                        {
                            x.Span("Trang ").FontSize(9).FontColor(Colors.Grey.Medium).FontFamily("Times New Roman");
                            x.CurrentPageNumber().FontSize(9).FontColor(Colors.Grey.Darken1).Bold().FontFamily("Times New Roman");
                            x.Span(" / ").FontSize(9).FontColor(Colors.Grey.Medium).FontFamily("Times New Roman");
                            x.TotalPages().FontSize(9).FontColor(Colors.Grey.Darken1).Bold().FontFamily("Times New Roman");
                        });
                });
            });

            var pdfBytes = document.GeneratePdf();
            var fileName = $"TurnoverRate_{from:yyyyMMdd}_{to:yyyyMMdd}.pdf";
            return File(pdfBytes, "application/pdf", fileName);
        }

        // ==========================================================================================
        // 2) TỔNG HỢP NHẬP / XUẤT theo vật tư trong kỳ — từ StockBalance
        //    Có Qty + Value, lọc theo kho (tuỳ chọn)
        // ==========================================================================================
        [HttpGet]
[RequirePermission("Reports","Read")]
public async Task<IActionResult> Movements([FromQuery] ReportFilterVM f)
        {
            var now = DateTime.Now;
            // ✅ KHÔNG dùng .Value
            var from = f.From?.Date ?? now.Date.AddDays(-7);
            var to   = f.To?.Date   ?? now.Date;

            // Cập nhật filter để hiển thị lại trên View
            f.From = from;
            f.To = to;

            ViewBag.Warehouses = new SelectList(
                await GetCachedWarehousesAsync(f.WarehouseId),
                "Value", "Text", f.WarehouseId?.ToString());
            ViewBag.Filter = f;

            // Nên .Date để tránh dính phần thời gian
            var q = _db.StockBalances.AsNoTracking()
                .Where(x => x.Date.Date >= from && x.Date.Date <= to);

            if (f.WarehouseId.HasValue)
                q = q.Where(x => x.WarehouseId == f.WarehouseId.Value);

            var agg = await q.GroupBy(x => x.MaterialId)
                .Select(g => new {
                    MaterialId = g.Key,
                    QtyIn      = g.Sum(z => z.InQty),
                    QtyOut     = g.Sum(z => z.OutQty),
                    InValue    = g.Sum(z => z.InValue),
                    OutValue   = g.Sum(z => z.OutValue)
                })
                .ToListAsync();

            var mats = await _db.Materials.AsNoTracking()
                .Select(m => new { m.Id, m.Code, m.Name, m.Unit })
                .ToListAsync();

            var data = (from a in agg
                        join m in mats on a.MaterialId equals m.Id
                        orderby (a.QtyIn + a.QtyOut) descending
                        select new MovementRowVM {
                            MaterialId   = m.Id,
                            MaterialName = $"{m.Code} - {m.Name}",
                            QtyIn        = a.QtyIn,
                            QtyOut       = a.QtyOut
                            // Nếu muốn hiện giá trị:
                            // InValue = a.InValue, OutValue = a.OutValue
                        }).ToList();

            return View(data);
        }

        // Excel Export for Movements
        [HttpGet]
        [RequirePermission("Reports","Read")]
        public async Task<IActionResult> MovementsExcel([FromQuery] ReportFilterVM f)
        {
            try
            {
                var now = DateTime.Now;
                var from = f.From?.Date ?? now.Date.AddDays(-7);
                var to   = f.To?.Date   ?? now.Date;

                var q = _db.StockBalances.AsNoTracking()
                    .Where(x => x.Date.Date >= from && x.Date.Date <= to);

                if (f.WarehouseId.HasValue)
                    q = q.Where(x => x.WarehouseId == f.WarehouseId.Value);

                var agg = await q.GroupBy(x => x.MaterialId)
                    .Select(g => new {
                        MaterialId = g.Key,
                        QtyIn      = g.Sum(z => z.InQty),
                        QtyOut     = g.Sum(z => z.OutQty),
                        InValue    = g.Sum(z => z.InValue),
                        OutValue   = g.Sum(z => z.OutValue)
                    })
                    .ToListAsync();

                var headers = new[] { "Mã vật tư", "Tên vật tư", "Đơn vị", "SL nhập", "SL xuất", "GT nhập", "GT xuất" };
                var fileName = $"NhapXuat_{from:yyyyMMdd}_{to:yyyyMMdd}.xlsx";

                if (agg == null || !agg.Any())
                {
                    // Trả về file Excel rỗng với header - tạo anonymous object với properties tương ứng headers
                    var emptyData = new[] { new {
                        MaVatTu = "",
                        TenVatTu = "",
                        DonVi = "",
                        SoLuongNhap = 0m,
                        SoLuongXuat = 0m,
                        GiaTriNhap = 0m,
                        GiaTriXuat = 0m
                    } }.Where(x => false).ToList(); // Empty list nhưng có type đúng
                    var emptyBytes = _excel.ExportToExcel(emptyData, "Nhập xuất", headers);
                    return File(emptyBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
                }

                var mats = await _db.Materials.AsNoTracking()
                    .Select(m => new { m.Id, m.Code, m.Name, m.Unit })
                    .ToListAsync();

                // Dùng join trực tiếp giống như method Movements để đảm bảo dữ liệu được lấy đúng
                var excelData = (from a in agg
                            join m in mats on a.MaterialId equals m.Id
                            orderby (a.QtyIn + a.QtyOut) descending
                            select new {
                                MaVatTu = m.Code ?? "",
                                TenVatTu = m.Name ?? "",
                                DonVi = m.Unit ?? "",
                                SoLuongNhap = a.QtyIn,
                                SoLuongXuat = a.QtyOut,
                                GiaTriNhap = a.InValue,
                                GiaTriXuat = a.OutValue
                            }).ToList();

                if (excelData == null || !excelData.Any())
                {
                    // Trả về file Excel rỗng với header - tạo anonymous object với properties tương ứng headers
                    var emptyData = new[] { new {
                        MaVatTu = "",
                        TenVatTu = "",
                        DonVi = "",
                        SoLuongNhap = 0m,
                        SoLuongXuat = 0m,
                        GiaTriNhap = 0m,
                        GiaTriXuat = 0m
                    } }.Where(x => false).ToList(); // Empty list nhưng có type đúng
                    var emptyBytes2 = _excel.ExportToExcel(emptyData, "Nhập xuất", headers);
                    return File(emptyBytes2, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
                }

                var resultBytes = _excel.ExportToExcel(excelData, "Nhập xuất", headers);
                return File(resultBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
            }
            catch (Exception ex)
            {
                // Log error và trả về file Excel rỗng - tạo anonymous object với properties tương ứng headers
                var errorHeaders = new[] { "Mã vật tư", "Tên vật tư", "Đơn vị", "SL nhập", "SL xuất", "GT nhập", "GT xuất" };
                var emptyData = new[] { new {
                    MaVatTu = "",
                    TenVatTu = "",
                    DonVi = "",
                    SoLuongNhap = 0m,
                    SoLuongXuat = 0m,
                    GiaTriNhap = 0m,
                    GiaTriXuat = 0m
                } }.Where(x => false).ToList(); // Empty list nhưng có type đúng
                var errorBytes = _excel.ExportToExcel(emptyData, "Nhập xuất", errorHeaders);
                var errorFileName = $"NhapXuat_{DateTime.Now:yyyyMMdd}_{DateTime.Now:HHmmss}.xlsx";
                return File(errorBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", errorFileName);
            }
        }

        // PDF Export for Movements
        [HttpGet]
        [RequirePermission("Reports","Read")]
        public async Task<IActionResult> ExportPdfMovements([FromQuery] ReportFilterVM f)
        {
            var now = DateTime.Now;
            var from = f.From?.Date ?? now.Date.AddDays(-7);
            var to   = f.To?.Date   ?? now.Date;

            var q = _db.StockBalances.AsNoTracking()
                .Where(x => x.Date.Date >= from && x.Date.Date <= to);

            if (f.WarehouseId.HasValue)
                q = q.Where(x => x.WarehouseId == f.WarehouseId.Value);

            var agg = await q.GroupBy(x => x.MaterialId)
                .Select(g => new {
                    MaterialId = g.Key,
                    QtyIn      = g.Sum(z => z.InQty),
                    QtyOut     = g.Sum(z => z.OutQty),
                    InValue    = g.Sum(z => z.InValue),
                    OutValue   = g.Sum(z => z.OutValue)
                })
                .ToListAsync();

            var mats = await _db.Materials.AsNoTracking()
                .Select(m => new { m.Id, m.Code, m.Name, m.Unit })
                .ToListAsync();

            var data = (from a in agg
                        join m in mats on a.MaterialId equals m.Id
                        orderby (a.QtyIn + a.QtyOut) descending
                        select new {
                            MaterialId   = m.Id,
                            MaterialCode = m.Code,
                            MaterialName = m.Name,
                            Unit         = m.Unit,
                            QtyIn        = a.QtyIn,
                            QtyOut       = a.QtyOut,
                            InValue      = a.InValue,
                            OutValue     = a.OutValue
                        }).ToList();

            int totalMaterials = data.Count;
            decimal totalQtyIn = data.Sum(d => d.QtyIn);
            decimal totalQtyOut = data.Sum(d => d.QtyOut);
            decimal totalInValue = data.Sum(d => d.InValue);
            decimal totalOutValue = data.Sum(d => d.OutValue);

            string warehouseName = await GetWarehouseNameAsync(f.WarehouseId);

            // Tạo PDF
            QuestPDF.Settings.License = LicenseType.Community;
            var document = QuestPDF.Fluent.Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(2f, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Times New Roman"));

                    page.Content()
                        .Column(column =>
                        {
                            // Header - Form number và thông tin đơn vị
                            column.Item().Row(row =>
                            {
                                // Bên trái: Đơn vị, Mã QHNS
                                row.RelativeItem().Column(leftCol =>
                                {
                                    leftCol.Item().Text("Đơn vị: ").FontSize(10).FontColor(Colors.Black).FontFamily("Times New Roman");
                                    leftCol.Item().PaddingTop(4).Text("Mã QHNS: ").FontSize(10).FontColor(Colors.Black).FontFamily("Times New Roman");
                                });

                                // Bên phải: Mẫu số C30-HD
                                row.RelativeItem().Column(rightCol =>
                                {
                                    rightCol.Item().AlignRight().Text("Mẫu số C30 - HD")
                                        .FontSize(10)
                                        .Bold()
                                        .FontColor(Colors.Black)
                                        .FontFamily("Times New Roman");
                                    rightCol.Item().PaddingTop(2).AlignRight().Text("(Ban hành kèm theo thông tư 107/2017/TT-BTC")
                                        .FontSize(8)
                                        .Italic()
                                        .FontColor(Colors.Grey.Darken1)
                                        .FontFamily("Times New Roman");
                                    rightCol.Item().AlignRight().Text("ngày 24/11/2017)")
                                        .FontSize(8)
                                        .Italic()
                                        .FontColor(Colors.Grey.Darken1)
                                        .FontFamily("Times New Roman");
                                });
                            });

                            column.Item().PaddingTop(15);

                            // Tiêu đề chính
                            column.Item().AlignCenter().Text("BÁO CÁO NHẬP XUẤT")
                                .FontSize(20)
                                .Bold()
                                .FontColor(Colors.Black)
                                .FontFamily("Times New Roman");

                            column.Item().PaddingTop(8);
                            column.Item().AlignCenter().Text($"Ngày xuất: {DateTime.Now:dd/MM/yyyy HH:mm}")
                                .FontSize(10)
                                .FontColor(Colors.Grey.Darken2)
                                .FontFamily("Times New Roman");

                            column.Item().PaddingTop(20);

                            // Thông tin bộ lọc
                            column.Item().PaddingBottom(20).Column(filterCol =>
                            {
                                filterCol.Item().PaddingBottom(6).Row(row =>
                                {
                                    row.AutoItem().Text("Kho: ").FontSize(10).FontColor(Colors.Black).FontFamily("Times New Roman");
                                    row.RelativeItem().Text(warehouseName).FontSize(10).Bold().FontColor(Colors.Black).FontFamily("Times New Roman");
                                });

                                filterCol.Item().Row(row =>
                                {
                                    row.AutoItem().Text("Từ ngày: ").FontSize(10).FontColor(Colors.Black).FontFamily("Times New Roman");
                                    row.RelativeItem().Text($"{from:dd/MM/yyyy} đến {to:dd/MM/yyyy}").FontSize(10).Bold().FontColor(Colors.Black).FontFamily("Times New Roman");
                                });
                            });

                            // Bảng dữ liệu
                            column.Item().Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.RelativeColumn(1.2f); // Mã NL
                                    columns.RelativeColumn(2.5f); // Tên NL
                                    columns.RelativeColumn(1f); // ĐVT
                                    columns.RelativeColumn(1.2f); // SL nhập
                                    columns.RelativeColumn(1.2f); // SL xuất
                                    columns.RelativeColumn(1.5f); // GT nhập
                                    columns.RelativeColumn(1.5f); // GT xuất
                                });

                                table.Header(header =>
                                {
                                    header.Cell().Element(HeaderCellStyle).Text("Mã NL").Bold().FontSize(11).FontFamily("Times New Roman");
                                    header.Cell().Element(HeaderCellStyle).Text("Tên nguyên liệu").Bold().FontSize(11).FontFamily("Times New Roman");
                                    header.Cell().Element(HeaderCellStyle).AlignCenter().Text("ĐVT").Bold().FontSize(11).FontFamily("Times New Roman");
                                    header.Cell().Element(HeaderCellStyle).AlignRight().Text("SL nhập").Bold().FontSize(11).FontFamily("Times New Roman");
                                    header.Cell().Element(HeaderCellStyle).AlignRight().Text("SL xuất").Bold().FontSize(11).FontFamily("Times New Roman");
                                    header.Cell().Element(HeaderCellStyle).AlignRight().Text("GT nhập").Bold().FontSize(11).FontFamily("Times New Roman");
                                    header.Cell().Element(HeaderCellStyle).AlignRight().Text("GT xuất").Bold().FontSize(11).FontFamily("Times New Roman");
                                });

                                int rowIndex = 0;
                                foreach (var r in data)
                                {
                                    bool isEvenRow = rowIndex % 2 == 0;
                                    
                                    table.Cell().Element(c => DataCellStyle(c, isEvenRow)).Text(r.MaterialCode ?? "").FontSize(10).FontFamily("Times New Roman");
                                    table.Cell().Element(c => DataCellStyle(c, isEvenRow)).Text(r.MaterialName ?? "").FontSize(10).FontFamily("Times New Roman");
                                    table.Cell().Element(c => DataCellStyle(c, isEvenRow)).AlignCenter().Text(r.Unit ?? "").FontSize(10).FontFamily("Times New Roman");
                                    table.Cell().Element(c => DataCellStyle(c, isEvenRow)).AlignRight().Text(FormatQty(r.QtyIn)).FontSize(10).FontFamily("Times New Roman");
                                    table.Cell().Element(c => DataCellStyle(c, isEvenRow)).AlignRight().Text(FormatQty(r.QtyOut)).FontSize(10).FontFamily("Times New Roman");
                                    table.Cell().Element(c => DataCellStyle(c, isEvenRow)).AlignRight().Text(FormatMoney(r.InValue)).FontSize(10).FontFamily("Times New Roman");
                                    table.Cell().Element(c => DataCellStyle(c, isEvenRow)).AlignRight().Text(FormatMoney(r.OutValue)).FontSize(10).FontFamily("Times New Roman");
                                    rowIndex++;
                                }
                            });

                            column.Item().PaddingTop(25).PaddingBottom(15).LineHorizontal(1).LineColor(Colors.Black);

                            // Tổng hợp
                            column.Item().PaddingTop(10).Column(summaryCol =>
                            {
                                summaryCol.Item().PaddingBottom(8).Row(row =>
                                {
                                    row.AutoItem().Text("Tổng số nguyên liệu: ").FontSize(11).FontColor(Colors.Black).FontFamily("Times New Roman");
                                    row.RelativeItem().Text(totalMaterials.ToString()).FontSize(11).Bold().FontColor(Colors.Black).FontFamily("Times New Roman");
                                });

                                summaryCol.Item().PaddingBottom(8).Row(row =>
                                {
                                    row.AutoItem().Text("Tổng số lượng nhập: ").FontSize(11).FontColor(Colors.Black).FontFamily("Times New Roman");
                                    row.RelativeItem().Text(FormatQty(totalQtyIn)).FontSize(11).Bold().FontFamily("Times New Roman").FontColor(Colors.Black);
                                });

                                summaryCol.Item().PaddingBottom(8).Row(row =>
                                {
                                    row.AutoItem().Text("Tổng số lượng xuất: ").FontSize(11).FontColor(Colors.Black).FontFamily("Times New Roman");
                                    row.RelativeItem().Text(FormatQty(totalQtyOut)).FontSize(11).Bold().FontFamily("Times New Roman").FontColor(Colors.Black);
                                });

                                summaryCol.Item().PaddingBottom(8).Row(row =>
                                {
                                    row.AutoItem().Text("Tổng giá trị nhập: ").FontSize(11).FontColor(Colors.Black).FontFamily("Times New Roman");
                                    row.RelativeItem().Text(FormatMoney(totalInValue)).FontSize(11).Bold().FontFamily("Times New Roman").FontColor(Colors.Black);
                                });

                                summaryCol.Item().Row(row =>
                                {
                                    row.AutoItem().Text("Tổng giá trị xuất: ").FontSize(11).FontColor(Colors.Black).FontFamily("Times New Roman");
                                    row.RelativeItem().Text(FormatMoney(totalOutValue)).FontSize(12).Bold().FontFamily("Times New Roman").FontColor(Colors.Black);
                                });
                            });
                        });

                    page.Footer()
                        .PaddingTop(10)
                        .BorderTop(1)
                        .BorderColor(Colors.Grey.Lighten1)
                        .AlignCenter()
                        .Text(x =>
                        {
                            x.Span("Trang ").FontSize(9).FontColor(Colors.Grey.Medium).FontFamily("Times New Roman");
                            x.CurrentPageNumber().FontSize(9).FontColor(Colors.Grey.Darken1).Bold().FontFamily("Times New Roman");
                            x.Span(" / ").FontSize(9).FontColor(Colors.Grey.Medium).FontFamily("Times New Roman");
                            x.TotalPages().FontSize(9).FontColor(Colors.Grey.Darken1).Bold().FontFamily("Times New Roman");
                        });
                });
            });

            var pdfBytes = document.GeneratePdf();
            var fileName = $"NhapXuat_{from:yyyyMMdd}_{to:yyyyMMdd}.pdf";
            return File(pdfBytes, "application/pdf", fileName);
        }


        // ==========================================================================================
        // 3) TỒN KHO CUỐI KỲ theo lô — tính từ StockLots (tồn thực tế theo lô)
        //    Hiển thị chi tiết từng lô với NSX, HSD và cảnh báo hết hạn
        // ==========================================================================================
        [HttpGet]
[RequirePermission("Reports","Read")]
public async Task<IActionResult> Inventory([FromQuery] ReportFilterVM f)
        {
            var (from, to) = NormRange(f.From, f.To);
            f.From = from; 
            f.To = to;

            ViewBag.Warehouses = new SelectList(
                await GetCachedWarehousesAsync(f.WarehouseId),
                "Value", "Text", f.WarehouseId?.ToString());
            ViewBag.Filter = f;

            // Lấy tồn kho theo lô từ StockLots
            var lotsQuery = _db.StockLots
                .AsNoTracking()
                .Include(l => l.Material)
                .Include(l => l.Warehouse)
                .Where(l => l.Quantity > 0); // Chỉ lấy lô còn tồn

            if (f.WarehouseId.HasValue)
                lotsQuery = lotsQuery.Where(l => l.WarehouseId == f.WarehouseId.Value);

            var lots = await lotsQuery.ToListAsync();

            // Tính nhập/xuất trong kỳ từ StockBalance (để tham khảo)
            var qInPeriod = _db.StockBalances.AsNoTracking()
                .Where(x => x.Date.Date >= from && x.Date.Date <= to);

            if (f.WarehouseId.HasValue)
                qInPeriod = qInPeriod.Where(x => x.WarehouseId == f.WarehouseId.Value);

            var moveAgg = await qInPeriod.GroupBy(x => new { x.MaterialId, x.WarehouseId })
                .Select(g => new {
                    MaterialId = g.Key.MaterialId,
                    WarehouseId = g.Key.WarehouseId,
                    QtyIn = g.Sum(z => z.InQty),
                    QtyOut = g.Sum(z => z.OutQty)
                })
                .ToListAsync();

            var movDict = moveAgg.ToDictionary(
                x => (x.MaterialId, x.WarehouseId),
                x => (x.QtyIn, x.QtyOut)
            );

            // Tạo danh sách báo cáo theo từng lô
            var data = new List<InventoryRowVM>();
            foreach (var lot in lots)
            {
                var materialName = lot.Material != null 
                    ? $"{lot.Material.Code} - {lot.Material.Name}" 
                    : "N/A";

                var (inQty, outQty) = movDict.TryGetValue(
                    (lot.MaterialId, lot.WarehouseId), 
                    out var t
                ) ? t : (0m, 0m);

                data.Add(new InventoryRowVM
                {
                    MaterialId = lot.MaterialId,
                    MaterialName = materialName,
                    LotId = lot.Id,
                    LotNumber = lot.LotNumber,
                    ManufactureDate = lot.ManufactureDate,
                    ExpiryDate = lot.ExpiryDate,
                    WarehouseName = lot.Warehouse?.Name,
                    BeginQty = 0, // Có thể tính nếu cần lịch sử
                    InQty = inQty,
                    OutQty = outQty,
                    // EndQty sẽ dùng Quantity hiện tại của lô
                });
            }

            // Sắp xếp theo nguyên liệu, sau đó theo HSD (ưu tiên hết hạn sớm)
            data = data
                .OrderBy(x => x.MaterialName)
                .ThenBy(x => x.ExpiryDate ?? DateTime.MaxValue)
                .ToList();

            // Tính tổng từ StockLots (tồn thực tế)
            ViewBag.SumEnd = lots.Sum(l => l.Quantity);
            ViewBag.SumIn = data.Sum(x => x.InQty);
            ViewBag.SumOut = data.Sum(x => x.OutQty);

            // Cập nhật EndQty cho mỗi dòng = tồn thực tế của lô
            var lotQtyDict = lots.ToDictionary(l => l.Id, l => l.Quantity);
            foreach (var row in data)
            {
                if (row.LotId.HasValue && lotQtyDict.TryGetValue(row.LotId.Value, out var qty))
                    row.BeginQty = qty - row.InQty + row.OutQty; // Tính ngược BeginQty
            }

            ViewBag.SumBegin = data.Sum(x => x.BeginQty);

            return View(data);
        }

        // Excel Export for Inventory
        [HttpGet]
        [RequirePermission("Reports","Read")]
        public async Task<IActionResult> InventoryExcel([FromQuery] ReportFilterVM f)
        {
            var (from, to) = NormRange(f.From, f.To);

            var lotsQuery = _db.StockLots
                .AsNoTracking()
                .Include(l => l.Material)
                .Include(l => l.Warehouse)
                .Where(l => l.Quantity > 0);

            if (f.WarehouseId.HasValue)
                lotsQuery = lotsQuery.Where(l => l.WarehouseId == f.WarehouseId.Value);

            var lots = await lotsQuery.ToListAsync();

            var excelData = lots.Select(l => new {
                Kho = l.Warehouse?.Name ?? "N/A",
                MaVatTu = l.Material?.Code ?? "N/A",
                TenVatTu = l.Material?.Name ?? "N/A",
                SoLo = l.LotNumber ?? "",
                NgaySanXuat = l.ManufactureDate,
                HanSuDung = l.ExpiryDate,
                SoLuong = l.Quantity,
                DonVi = l.Material?.Unit ?? ""
            }).OrderBy(x => x.TenVatTu).ThenBy(x => x.HanSuDung);

            var headers = new[] { "Kho", "Mã vật tư", "Tên vật tư", "Số lô", "NSX", "HSD", "Số lượng", "Đơn vị" };
            var bytes = _excel.ExportToExcel(excelData, "Tồn kho", headers);
            var fileName = $"TonKho_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }

        // ===== STOCK AGING REPORT =====
        [HttpGet]
        [RequirePermission("Reports", "Read")]
        public async Task<IActionResult> StockAging()
        {
            var today = DateTime.Today;

            // Get current stock with last issue date
            var stockData = await _db.Stocks
                .Where(s => s.Quantity > 0)
                .Select(s => new
                {
                    s.MaterialId,
                    s.Material.Code,
                    s.Material.Name,
                    s.Material.Unit,
                    s.Material.PurchasePrice,
                    s.Quantity
                })
                .GroupBy(s => new { s.MaterialId, s.Code, s.Name, s.Unit, s.PurchasePrice })
                .Select(g => new
                {
                    g.Key.MaterialId,
                    g.Key.Code,
                    g.Key.Name,
                    g.Key.Unit,
                    g.Key.PurchasePrice,
                    CurrentStock = g.Sum(x => x.Quantity)
                })
                .ToListAsync();

            // Get last issue dates
            var lastIssueDates = await _db.StockIssues
                .Where(si => si.Status == DocumentStatus.HoanThanh)
                .SelectMany(si => si.Details)
                .GroupBy(d => d.MaterialId)
                .Select(g => new
                {
                    MaterialId = g.Key,
                    LastIssueDate = g.Max(d => d.StockIssue.CreatedAt)
                })
                .ToDictionaryAsync(x => x.MaterialId, x => x.LastIssueDate);

            var items = stockData.Select(s =>
            {
                DateTime? lastIssueDate = lastIssueDates.ContainsKey(s.MaterialId) 
                    ? lastIssueDates[s.MaterialId] 
                    : null;

                int daysSinceLastIssue = lastIssueDate.HasValue 
                    ? (today - lastIssueDate.Value.Date).Days 
                    : 9999;

                string category = daysSinceLastIssue switch
                {
                    < 30 => "Fast",      // < 1 month
                    < 90 => "Normal",    // 1-3 months
                    < 180 => "Slow",     // 3-6 months
                    _ => "Dead"          // > 6 months or never issued
                };

                return new StockAgingItemVM
                {
                    MaterialId = s.MaterialId,
                    Code = s.Code,
                    Name = s.Name,
                    Unit = s.Unit,
                    CurrentStock = s.CurrentStock,
                    StockValue = s.CurrentStock * (s.PurchasePrice ?? 0),
                    LastIssueDate = lastIssueDate,
                    DaysSinceLastIssue = daysSinceLastIssue == 9999 ? 0 : daysSinceLastIssue,
                    AgingCategory = category
                };
            })
            .OrderByDescending(x => x.DaysSinceLastIssue)
            .ToList();

            var vm = new StockAgingVM { Items = items };
            return View(vm);
        }

        // ==========================================================================================
        // BÁO CÁO NXT THEO GIÁ TRỊ
        // ==========================================================================================
        [HttpGet]
        [RequirePermission("Reports", "Read")]
        public async Task<IActionResult> InventoryValue([FromQuery] ReportFilterVM f)
        {
            var (from, to) = NormRange(f.From, f.To);
            f.From = from;
            f.To = to;

            ViewBag.Warehouses = new SelectList(
                await GetCachedWarehousesAsync(f.WarehouseId),
                "Value", "Text", f.WarehouseId?.ToString());
            ViewBag.Filter = f;

            // Lấy danh sách vật tư có phát sinh trong kỳ
            var materialsQuery = _db.Materials.AsNoTracking();
            
            // Lấy tất cả materialIds có phát sinh từ StockReceipts, StockIssues, hoặc StockBalances
            var materialIdsFromReceipts = await _db.StockReceiptDetails
                .Include(rd => rd.StockReceipt)
                .Where(rd => rd.StockReceipt.Status == DocumentStatus.DaNhapHang
                          && (rd.StockReceipt.ReceivedAt.HasValue 
                              ? (rd.StockReceipt.ReceivedAt.Value.Date >= from && rd.StockReceipt.ReceivedAt.Value.Date <= to)
                              : (rd.StockReceipt.CreatedAt.Date >= from && rd.StockReceipt.CreatedAt.Date <= to))
                          && (!f.WarehouseId.HasValue || rd.StockReceipt.WarehouseId == f.WarehouseId.Value))
                .Select(rd => rd.MaterialId)
                .Distinct()
                .ToListAsync();

            var materialIdsFromIssues = await _db.StockIssueDetails
                .Include(id => id.StockIssue)
                .Where(id => id.StockIssue.Status == DocumentStatus.DaXuatHang
                          && id.StockIssue.CreatedAt.Date >= from
                          && id.StockIssue.CreatedAt.Date <= to
                          && (!f.WarehouseId.HasValue || id.StockIssue.WarehouseId == f.WarehouseId.Value))
                .Select(id => id.MaterialId)
                .Distinct()
                .ToListAsync();

            var materialIdsFromBalances = await _db.StockBalances
                .Where(sb => sb.Date >= from && sb.Date <= to
                          && (!f.WarehouseId.HasValue || sb.WarehouseId == f.WarehouseId.Value))
                .Select(sb => sb.MaterialId)
                .Distinct()
                .ToListAsync();

            var allMaterialIds = materialIdsFromReceipts
                .Union(materialIdsFromIssues)
                .Union(materialIdsFromBalances)
                .Distinct()
                .ToList();

            if (allMaterialIds.Any())
            {
                materialsQuery = materialsQuery.Where(m => allMaterialIds.Contains(m.Id));
            }
            else
            {
                // Nếu không có phát sinh, trả về empty
                materialsQuery = materialsQuery.Where(m => false);
            }

            var materials = await materialsQuery
                .Include(m => m.Warehouse)
                .ToListAsync();

            var costingService = HttpContext.RequestServices.GetRequiredService<ICostingService>();
            var data = new List<InventoryValueRowVM>();

            // Lấy danh sách warehouses cần xử lý
            var warehousesToProcess = f.WarehouseId.HasValue
                ? new List<int> { f.WarehouseId.Value }
                : await _db.Warehouses.AsNoTracking().Select(w => w.Id).ToListAsync();

            foreach (var mat in materials)
            {
                foreach (var warehouseId in warehousesToProcess)
                {
                    // Kiểm tra xem material có phát sinh trong warehouse này không
                    var hasActivity = await _db.StockReceiptDetails
                        .Include(rd => rd.StockReceipt)
                        .AnyAsync(rd => rd.MaterialId == mat.Id
                                     && rd.StockReceipt.WarehouseId == warehouseId
                                     && rd.StockReceipt.Status == DocumentStatus.DaNhapHang)
                        || await _db.StockIssueDetails
                        .Include(id => id.StockIssue)
                        .AnyAsync(id => id.MaterialId == mat.Id
                                     && id.StockIssue.WarehouseId == warehouseId
                                     && id.StockIssue.Status == DocumentStatus.DaXuatHang)
                        || await _db.StockLots
                        .AnyAsync(l => l.MaterialId == mat.Id && l.WarehouseId == warehouseId && l.Quantity > 0);

                    if (!hasActivity) continue;

                // Nhập trong kỳ: Từ StockReceiptDetails (ưu tiên ReceivedAt, nếu không có thì dùng CreatedAt)
                var receiptsInPeriod = await _db.StockReceiptDetails
                    .Include(rd => rd.StockReceipt)
                    .Where(rd => rd.MaterialId == mat.Id
                              && rd.StockReceipt.WarehouseId == warehouseId
                              && rd.StockReceipt.Status == DocumentStatus.DaNhapHang
                              && ((rd.StockReceipt.ReceivedAt.HasValue 
                                  ? (rd.StockReceipt.ReceivedAt.Value.Date >= from && rd.StockReceipt.ReceivedAt.Value.Date <= to)
                                  : (rd.StockReceipt.CreatedAt.Date >= from && rd.StockReceipt.CreatedAt.Date <= to))))
                    .ToListAsync();
                var inQty = (decimal)receiptsInPeriod.Sum(r => r.Quantity);
                var inValue = receiptsInPeriod.Sum(r => (decimal)r.Quantity * r.UnitPrice);

                // Xuất trong kỳ: Từ StockIssueDetails (dùng CostPrice nếu có)
                var issuesInPeriod = await _db.StockIssueDetails
                    .Include(id => id.StockIssue)
                    .Where(id => id.MaterialId == mat.Id
                              && id.StockIssue.WarehouseId == warehouseId
                              && id.StockIssue.Status == DocumentStatus.DaXuatHang
                              && id.StockIssue.CreatedAt.Date >= from
                              && id.StockIssue.CreatedAt.Date <= to)
                    .ToListAsync();
                var outQty = (decimal)issuesInPeriod.Sum(i => i.Quantity);
                var outValue = issuesInPeriod.Sum(i => i.CostPrice.HasValue 
                    ? (decimal)i.Quantity * i.CostPrice.Value 
                    : (decimal)i.Quantity * i.UnitPrice);

                // Tồn cuối kỳ: Tính từ StockLots tại ngày To
                var endLots = await _db.StockLots
                    .Where(l => l.WarehouseId == warehouseId && l.MaterialId == mat.Id && l.Quantity > 0)
                    .ToListAsync();
                var endQty = endLots.Sum(l => l.Quantity);
                var endValue = 0m;
                if (endQty > 0)
                {
                    var costingMethod = mat.CostingMethod ?? CostingMethod.WeightedAverage;
                    if (costingMethod == CostingMethod.FIFO)
                    {
                        // Tính theo FIFO
                        var lotCosts = await costingService.GetLotCostsAsync(warehouseId, mat.Id, to);
                        endValue = lotCosts.Sum(l => l.quantity * l.unitPrice);
                    }
                    else
                    {
                        // Tính theo bình quân
                        var avgCost = await costingService.CalculateAverageCostAsync(warehouseId, mat.Id, to);
                        endValue = endQty * avgCost;
                    }
                }

                // Tồn đầu kỳ: Tính ngược từ tồn cuối kỳ trừ nhập cộng xuất
                // Begin = End - In + Out
                var beginQty = Math.Max(0, endQty - inQty + outQty);
                var beginValue = 0m;
                if (beginQty > 0)
                {
                    var avgCost = await costingService.CalculateAverageCostAsync(warehouseId, mat.Id, from.AddDays(-1));
                    beginValue = beginQty * avgCost;
                }

                var warehouse = await _db.Warehouses.FindAsync(warehouseId);
                data.Add(new InventoryValueRowVM
                {
                    MaterialId = mat.Id,
                    MaterialCode = mat.Code,
                    MaterialName = mat.Name,
                    Unit = mat.Unit,
                    WarehouseName = warehouse?.Name ?? "N/A",
                    BeginQty = beginQty,
                    BeginValue = Math.Round(beginValue, 2),
                    InQty = inQty,
                    InValue = Math.Round(inValue, 2),
                    OutQty = outQty,
                    OutValue = Math.Round(outValue, 2),
                    EndQty = endQty,
                    EndValue = Math.Round(endValue, 2)
                });
                }
            }

            ViewBag.TotalBeginValue = data.Sum(d => d.BeginValue);
            ViewBag.TotalInValue = data.Sum(d => d.InValue);
            ViewBag.TotalOutValue = data.Sum(d => d.OutValue);
            ViewBag.TotalEndValue = data.Sum(d => d.EndValue);

            return View(data.OrderBy(d => d.MaterialCode).ToList());
        }

        // ==========================================================================================
        // BÁO CÁO GIÁ VỐN HÀNG BÁN (COGS)
        // ==========================================================================================
        [HttpGet]
        [RequirePermission("Reports", "Read")]
        public async Task<IActionResult> COGS([FromQuery] ReportFilterVM f)
        {
            var (from, to) = NormRange(f.From, f.To);
            f.From = from;
            f.To = to;

            ViewBag.Warehouses = new SelectList(
                await GetCachedWarehousesAsync(f.WarehouseId),
                "Value", "Text", f.WarehouseId?.ToString());
            ViewBag.Filter = f;

            var issuesQuery = _db.StockIssueDetails
                .AsNoTracking()
                .Include(d => d.StockIssue)
                    .ThenInclude(i => i.Warehouse)
                .Include(d => d.Material)
                .Where(d => d.StockIssue.Status == DocumentStatus.DaXuatHang
                         && d.StockIssue.CreatedAt.Date >= from
                         && d.StockIssue.CreatedAt.Date <= to);

            if (f.WarehouseId.HasValue)
                issuesQuery = issuesQuery.Where(d => d.StockIssue.WarehouseId == f.WarehouseId.Value);

            var issues = await issuesQuery.ToListAsync();

            var data = issues.Select(d => new COGSRowVM
            {
                MaterialId = d.MaterialId,
                MaterialCode = d.Material.Code,
                MaterialName = d.Material.Name,
                Unit = d.Unit,
                Quantity = (decimal)d.Quantity,
                CostPrice = d.CostPrice ?? d.UnitPrice,  // Dùng CostPrice nếu có, không thì dùng UnitPrice
                IssueDate = d.StockIssue.CreatedAt,
                IssueNumber = d.StockIssue.IssueNumber,
                WarehouseName = d.StockIssue.Warehouse?.Name
            }).OrderBy(d => d.IssueDate).ThenBy(d => d.MaterialCode).ToList();

            ViewBag.TotalCOGS = data.Sum(d => d.TotalCOGS);
            ViewBag.TotalQty = data.Sum(d => d.Quantity);

            return View(data);
        }

        [HttpGet]
        [RequirePermission("Reports", "Read")]
        public async Task<IActionResult> ExportPdfCOGS([FromQuery] ReportFilterVM f)
        {
            var (from, to) = NormRange(f.From, f.To);

            var issuesQuery = _db.StockIssueDetails
                .AsNoTracking()
                .Include(d => d.StockIssue)
                    .ThenInclude(i => i.Warehouse)
                .Include(d => d.Material)
                .Where(d => d.StockIssue.Status == DocumentStatus.DaXuatHang
                         && d.StockIssue.CreatedAt.Date >= from
                         && d.StockIssue.CreatedAt.Date <= to);

            if (f.WarehouseId.HasValue)
                issuesQuery = issuesQuery.Where(d => d.StockIssue.WarehouseId == f.WarehouseId.Value);

            var issues = await issuesQuery.ToListAsync();

            var data = issues.Select(d => new COGSRowVM
            {
                MaterialId = d.MaterialId,
                MaterialCode = d.Material.Code,
                MaterialName = d.Material.Name,
                Unit = d.Unit,
                Quantity = (decimal)d.Quantity,
                CostPrice = d.CostPrice ?? d.UnitPrice,
                IssueDate = d.StockIssue.CreatedAt,
                IssueNumber = d.StockIssue.IssueNumber,
                WarehouseName = d.StockIssue.Warehouse?.Name
            }).OrderBy(d => d.IssueDate).ThenBy(d => d.MaterialCode).ToList();

            decimal totalCOGS = data.Sum(d => d.TotalCOGS);
            decimal totalQty = data.Sum(d => d.Quantity);

            string warehouseName = await GetWarehouseNameAsync(f.WarehouseId);

            // Tạo PDF
            QuestPDF.Settings.License = LicenseType.Community;
            var document = QuestPDF.Fluent.Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(2f, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Times New Roman"));

                    page.Content()
                        .Column(column =>
                        {
                            // Header - Form number và thông tin đơn vị
                            column.Item().Row(row =>
                            {
                                // Bên trái: Đơn vị, Mã QHNS
                                row.RelativeItem().Column(leftCol =>
                                {
                                    leftCol.Item().Text("Đơn vị: ").FontSize(10).FontColor(Colors.Black).FontFamily("Times New Roman");
                                    leftCol.Item().PaddingTop(4).Text("Mã QHNS: ").FontSize(10).FontColor(Colors.Black).FontFamily("Times New Roman");
                                });

                                // Bên phải: Mẫu số C30-HD
                                row.RelativeItem().Column(rightCol =>
                                {
                                    rightCol.Item().AlignRight().Text("Mẫu số C30 - HD")
                                        .FontSize(10)
                                        .Bold()
                                        .FontColor(Colors.Black)
                                        .FontFamily("Times New Roman");
                                    rightCol.Item().PaddingTop(2).AlignRight().Text("(Ban hành kèm theo thông tư 107/2017/TT-BTC")
                                        .FontSize(8)
                                        .Italic()
                                        .FontColor(Colors.Grey.Darken1)
                                        .FontFamily("Times New Roman");
                                    rightCol.Item().AlignRight().Text("ngày 24/11/2017)")
                                        .FontSize(8)
                                        .Italic()
                                        .FontColor(Colors.Grey.Darken1)
                                        .FontFamily("Times New Roman");
                                });
                            });

                            column.Item().PaddingTop(15);

                            // Tiêu đề chính
                            column.Item().AlignCenter().Text("BÁO CÁO GIÁ VỐN HÀNG BÁN (COGS)")
                                .FontSize(20)
                                .Bold()
                                .FontColor(Colors.Black)
                                .FontFamily("Times New Roman");

                            column.Item().PaddingTop(8);
                            column.Item().AlignCenter().Text($"Ngày xuất: {DateTime.Now:dd/MM/yyyy HH:mm}")
                                .FontSize(10)
                                .FontColor(Colors.Grey.Darken2)
                                .FontFamily("Times New Roman");

                            column.Item().PaddingTop(20);

                            // Thông tin bộ lọc
                            column.Item().PaddingBottom(20).Column(filterCol =>
                            {
                                filterCol.Item().PaddingBottom(6).Row(row =>
                                {
                                    row.AutoItem().Text("Kho: ").FontSize(10).FontColor(Colors.Black).FontFamily("Times New Roman");
                                    row.RelativeItem().Text(warehouseName).FontSize(10).Bold().FontColor(Colors.Black).FontFamily("Times New Roman");
                                });

                                filterCol.Item().Row(row =>
                                {
                                    row.AutoItem().Text("Từ ngày: ").FontSize(10).FontColor(Colors.Black).FontFamily("Times New Roman");
                                    row.RelativeItem().Text($"{from:dd/MM/yyyy} đến {to:dd/MM/yyyy}").FontSize(10).Bold().FontColor(Colors.Black).FontFamily("Times New Roman");
                                });
                            });

                            // Bảng dữ liệu
                            column.Item().Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.RelativeColumn(1.5f); // Mã VT
                                    columns.RelativeColumn(2.5f); // Tên VT
                                    columns.RelativeColumn(1f); // ĐVT
                                    columns.RelativeColumn(1.2f); // SL
                                    columns.RelativeColumn(1.8f); // Đơn giá
                                    columns.RelativeColumn(2f); // Tổng giá vốn
                                });

                                table.Header(header =>
                                {
                                    header.Cell().Element(HeaderCellStyle).Text("Mã VT").Bold().FontSize(9).FontFamily("Times New Roman");
                                    header.Cell().Element(HeaderCellStyle).Text("Tên vật tư").Bold().FontSize(9).FontFamily("Times New Roman");
                                    header.Cell().Element(HeaderCellStyle).AlignCenter().Text("ĐVT").Bold().FontSize(9).FontFamily("Times New Roman");
                                    header.Cell().Element(HeaderCellStyle).AlignRight().Text("SL").Bold().FontSize(9).FontFamily("Times New Roman");
                                    header.Cell().Element(HeaderCellStyle).AlignRight().Text("Đơn giá").Bold().FontSize(9).FontFamily("Times New Roman");
                                    header.Cell().Element(HeaderCellStyle).AlignRight().Text("Tổng giá vốn").Bold().FontSize(9).FontFamily("Times New Roman");
                                });

                                int rowIndex = 0;
                                foreach (var d in data)
                                {
                                    bool isEvenRow = rowIndex % 2 == 0;
                                    table.Cell().Element(c => DataCellStyle(c, isEvenRow)).Text(d.MaterialCode ?? "").FontSize(8).FontFamily("Times New Roman");
                                    table.Cell().Element(c => DataCellStyle(c, isEvenRow)).Text(d.MaterialName ?? "").FontSize(8).FontFamily("Times New Roman");
                                    table.Cell().Element(c => DataCellStyle(c, isEvenRow)).AlignCenter().Text(d.Unit ?? "").FontSize(8).FontFamily("Times New Roman");
                                    table.Cell().Element(c => DataCellStyle(c, isEvenRow)).AlignRight().Text(FormatQty(d.Quantity)).FontSize(8).FontFamily("Times New Roman");
                                    table.Cell().Element(c => DataCellStyle(c, isEvenRow)).AlignRight().Text(FormatMoney(d.CostPrice)).FontSize(8).FontFamily("Times New Roman");
                                    table.Cell().Element(c => DataCellStyle(c, isEvenRow)).AlignRight().Text(FormatMoney(d.TotalCOGS)).FontSize(8).FontFamily("Times New Roman").Bold();
                                    rowIndex++;
                                }
                            });

                            column.Item().PaddingTop(25).PaddingBottom(15).LineHorizontal(1).LineColor(Colors.Black);

                            // Tổng hợp
                            column.Item().PaddingTop(10).Column(summaryCol =>
                            {
                                summaryCol.Item().PaddingBottom(8).Row(row =>
                                {
                                    row.AutoItem().Text("Tổng số dòng: ").FontSize(11).FontColor(Colors.Black).FontFamily("Times New Roman");
                                    row.RelativeItem().Text(data.Count.ToString()).FontSize(11).Bold().FontColor(Colors.Black).FontFamily("Times New Roman");
                                });

                                summaryCol.Item().PaddingBottom(8).Row(row =>
                                {
                                    row.AutoItem().Text("Tổng số lượng: ").FontSize(11).FontColor(Colors.Black).FontFamily("Times New Roman");
                                    row.RelativeItem().Text(FormatQty(totalQty)).FontSize(11).Bold().FontFamily("Times New Roman").FontColor(Colors.Black);
                                });

                                summaryCol.Item().Row(row =>
                                {
                                    row.AutoItem().Text("Tổng giá vốn: ").FontSize(11).FontColor(Colors.Black).FontFamily("Times New Roman");
                                    row.RelativeItem().Text(FormatMoney(totalCOGS)).FontSize(12).Bold().FontFamily("Times New Roman").FontColor(Colors.Black);
                                });
                            });
                        });

                    page.Footer()
                        .PaddingTop(10)
                        .BorderTop(1)
                        .BorderColor(Colors.Grey.Lighten1)
                        .AlignCenter()
                        .Text(x =>
                        {
                            x.Span("Trang ").FontSize(9).FontColor(Colors.Grey.Medium).FontFamily("Times New Roman");
                            x.CurrentPageNumber().FontSize(9).FontColor(Colors.Grey.Darken1).Bold().FontFamily("Times New Roman");
                            x.Span(" / ").FontSize(9).FontColor(Colors.Grey.Medium).FontFamily("Times New Roman");
                            x.TotalPages().FontSize(9).FontColor(Colors.Grey.Darken1).Bold().FontFamily("Times New Roman");
                        });
                });
            });

            var pdfBytes = document.GeneratePdf();
            var fileName = $"COGS_{from:yyyyMMdd}_{to:yyyyMMdd}.pdf";
            return File(pdfBytes, "application/pdf", fileName);
        }

        // ==========================================================================================
        // ĐỊNH GIÁ TỒN KHO CUỐI KỲ
        // ==========================================================================================
        [HttpGet]
        [RequirePermission("Reports", "Read")]
        public async Task<IActionResult> InventoryValuation([FromQuery] ReportFilterVM f)
        {
            var (from, to) = NormRange(f.From, f.To);
            f.From = from;
            f.To = to;

            ViewBag.Warehouses = new SelectList(
                await GetCachedWarehousesAsync(f.WarehouseId),
                "Value", "Text", f.WarehouseId?.ToString());
            ViewBag.Filter = f;

            var costingService = HttpContext.RequestServices.GetRequiredService<ICostingService>();

            // Lấy tồn kho cuối kỳ từ StockLots
            var lotsQuery = _db.StockLots
                .AsNoTracking()
                .Include(l => l.Material)
                .Include(l => l.Warehouse)
                .Where(l => l.Quantity > 0);

            if (f.WarehouseId.HasValue)
                lotsQuery = lotsQuery.Where(l => l.WarehouseId == f.WarehouseId.Value);

            var lots = await lotsQuery.ToListAsync();

            // Group by Material và Warehouse
            var grouped = lots.GroupBy(l => new { l.MaterialId, l.WarehouseId });

            var data = new List<InventoryValuationRowVM>();

            foreach (var group in grouped)
            {
                var material = group.First().Material;
                var warehouse = group.First().Warehouse;
                var totalQty = group.Sum(l => l.Quantity);
                var costingMethod = material?.CostingMethod ?? CostingMethod.WeightedAverage;

                decimal unitCost = 0m;
                if (totalQty > 0)
                {
                    if (costingMethod == CostingMethod.FIFO)
                    {
                        var lotCosts = await costingService.GetLotCostsAsync(group.Key.WarehouseId, group.Key.MaterialId, to);
                        var totalValue = lotCosts.Sum(l => l.quantity * l.unitPrice);
                        unitCost = totalQty > 0 ? Math.Round(totalValue / totalQty, 2) : 0m;
                    }
                    else
                    {
                        unitCost = await costingService.CalculateAverageCostAsync(group.Key.WarehouseId, group.Key.MaterialId, to);
                    }
                }

                data.Add(new InventoryValuationRowVM
                {
                    MaterialId = group.Key.MaterialId,
                    MaterialCode = material?.Code ?? "",
                    MaterialName = material?.Name ?? "",
                    Unit = material?.Unit ?? "",
                    WarehouseName = warehouse?.Name,
                    Quantity = totalQty,
                    UnitCost = unitCost,
                    CostingMethod = costingMethod
                });
            }

            ViewBag.TotalValue = data.Sum(d => d.TotalValue);
            ViewBag.TotalQty = data.Sum(d => d.Quantity);

            return View(data.OrderBy(d => d.MaterialCode).ThenBy(d => d.WarehouseName).ToList());
        }

        [HttpGet]
        [RequirePermission("Reports", "Read")]
        public async Task<IActionResult> ExportPdfInventoryValue([FromQuery] ReportFilterVM f)
        {
            var (from, to) = NormRange(f.From, f.To);
            f.From = from;
            f.To = to;

            // Lấy dữ liệu tương tự method InventoryValue()
            var materialIdsFromReceipts = await _db.StockReceiptDetails
                .Include(rd => rd.StockReceipt)
                .Where(rd => rd.StockReceipt.Status == DocumentStatus.DaNhapHang
                          && rd.StockReceipt.CreatedAt.Date >= from
                          && rd.StockReceipt.CreatedAt.Date <= to
                          && (!f.WarehouseId.HasValue || rd.StockReceipt.WarehouseId == f.WarehouseId.Value))
                .Select(rd => rd.MaterialId)
                .Distinct()
                .ToListAsync();

            var materialIdsFromIssues = await _db.StockIssueDetails
                .Include(id => id.StockIssue)
                .Where(id => id.StockIssue.Status == DocumentStatus.DaXuatHang
                          && id.StockIssue.CreatedAt.Date >= from
                          && id.StockIssue.CreatedAt.Date <= to
                          && (!f.WarehouseId.HasValue || id.StockIssue.WarehouseId == f.WarehouseId.Value))
                .Select(id => id.MaterialId)
                .Distinct()
                .ToListAsync();

            var materialIdsFromBalances = await _db.StockBalances
                .Where(sb => sb.Date >= from && sb.Date <= to
                          && (!f.WarehouseId.HasValue || sb.WarehouseId == f.WarehouseId.Value))
                .Select(sb => sb.MaterialId)
                .Distinct()
                .ToListAsync();

            var allMaterialIds = materialIdsFromReceipts
                .Union(materialIdsFromIssues)
                .Union(materialIdsFromBalances)
                .Distinct()
                .ToList();

            var baseQuery = _db.Materials.AsNoTracking();
            
            if (allMaterialIds.Any())
            {
                baseQuery = baseQuery.Where(m => allMaterialIds.Contains(m.Id));
            }
            else
            {
                baseQuery = baseQuery.Where(m => false);
            }

            var materials = await baseQuery.Include(m => m.Warehouse).ToListAsync();

            var costingService = HttpContext.RequestServices.GetRequiredService<ICostingService>();
            var data = new List<InventoryValueRowVM>();

            var warehousesToProcess = f.WarehouseId.HasValue
                ? new List<int> { f.WarehouseId.Value }
                : await _db.Warehouses.AsNoTracking().Select(w => w.Id).ToListAsync();

            foreach (var mat in materials)
            {
                foreach (var warehouseId in warehousesToProcess)
                {
                    var hasActivity = await _db.StockReceiptDetails
                        .Include(rd => rd.StockReceipt)
                        .AnyAsync(rd => rd.MaterialId == mat.Id
                                     && rd.StockReceipt.WarehouseId == warehouseId
                                     && rd.StockReceipt.Status == DocumentStatus.DaNhapHang)
                        || await _db.StockIssueDetails
                        .Include(id => id.StockIssue)
                        .AnyAsync(id => id.MaterialId == mat.Id
                                     && id.StockIssue.WarehouseId == warehouseId
                                     && id.StockIssue.Status == DocumentStatus.DaXuatHang)
                        || await _db.StockLots
                        .AnyAsync(l => l.MaterialId == mat.Id && l.WarehouseId == warehouseId && l.Quantity > 0);

                    if (!hasActivity) continue;

                    var receiptsInPeriod = await _db.StockReceiptDetails
                        .Include(rd => rd.StockReceipt)
                        .Where(rd => rd.MaterialId == mat.Id
                                  && rd.StockReceipt.WarehouseId == warehouseId
                                  && rd.StockReceipt.Status == DocumentStatus.DaNhapHang
                                  && ((rd.StockReceipt.ReceivedAt.HasValue
                                      ? (rd.StockReceipt.ReceivedAt.Value.Date >= from && rd.StockReceipt.ReceivedAt.Value.Date <= to)
                                      : (rd.StockReceipt.CreatedAt.Date >= from && rd.StockReceipt.CreatedAt.Date <= to))))
                        .ToListAsync();
                    var inQty = (decimal)receiptsInPeriod.Sum(r => r.Quantity);
                    var inValue = receiptsInPeriod.Sum(r => (decimal)r.Quantity * r.UnitPrice);

                    var issuesInPeriod = await _db.StockIssueDetails
                        .Include(id => id.StockIssue)
                        .Where(id => id.MaterialId == mat.Id
                                  && id.StockIssue.WarehouseId == warehouseId
                                  && id.StockIssue.Status == DocumentStatus.DaXuatHang
                                  && id.StockIssue.CreatedAt.Date >= from
                                  && id.StockIssue.CreatedAt.Date <= to)
                        .ToListAsync();
                    var outQty = (decimal)issuesInPeriod.Sum(i => i.Quantity);
                    var outValue = issuesInPeriod.Sum(i => i.CostPrice.HasValue
                        ? (decimal)i.Quantity * i.CostPrice.Value
                        : (decimal)i.Quantity * i.UnitPrice);

                    var endLots = await _db.StockLots
                        .Where(l => l.WarehouseId == warehouseId && l.MaterialId == mat.Id && l.Quantity > 0)
                        .ToListAsync();
                    var endQty = endLots.Sum(l => l.Quantity);
                    var endValue = 0m;
                    if (endQty > 0)
                    {
                        var costingMethod = mat.CostingMethod ?? CostingMethod.WeightedAverage;
                        if (costingMethod == CostingMethod.FIFO)
                        {
                            var lotCosts = await costingService.GetLotCostsAsync(warehouseId, mat.Id, to);
                            endValue = lotCosts.Sum(l => l.quantity * l.unitPrice);
                        }
                        else
                        {
                            var lotCosts = await costingService.GetLotCostsAsync(warehouseId, mat.Id, to);
                            var totalValue = lotCosts.Sum(l => l.quantity * l.unitPrice);
                            var totalQty = lotCosts.Sum(l => l.quantity);
                            if (totalQty > 0)
                                endValue = (endQty * totalValue) / totalQty;
                        }
                    }

                    var beginQty = endQty - inQty + outQty;
                    var beginValue = endValue - inValue + outValue;

                    data.Add(new InventoryValueRowVM
                    {
                        MaterialId = mat.Id,
                        MaterialCode = mat.Code,
                        MaterialName = mat.Name,
                        Unit = mat.Unit,
                        WarehouseName = (await _db.Warehouses.FindAsync(warehouseId))?.Name,
                        BeginQty = Math.Max(0, beginQty),
                        BeginValue = Math.Max(0, beginValue),
                        InQty = inQty,
                        InValue = inValue,
                        OutQty = outQty,
                        OutValue = outValue,
                        EndQty = endQty,
                        EndValue = endValue
                    });
                }
            }

            decimal totalBeginValue = data.Sum(d => d.BeginValue);
            decimal totalInValue = data.Sum(d => d.InValue);
            decimal totalOutValue = data.Sum(d => d.OutValue);
            decimal totalEndValue = data.Sum(d => d.EndValue);

            string warehouseName = await GetWarehouseNameAsync(f.WarehouseId);

            // Tạo PDF
            QuestPDF.Settings.License = LicenseType.Community;
            var document = QuestPDF.Fluent.Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(2f, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Times New Roman"));

                    page.Content()
                        .Column(column =>
                        {
                            // Header - Form number và thông tin đơn vị
                            column.Item().Row(row =>
                            {
                                // Bên trái: Đơn vị, Mã QHNS
                                row.RelativeItem().Column(leftCol =>
                                {
                                    leftCol.Item().Text("Đơn vị: ").FontSize(10).FontColor(Colors.Black).FontFamily("Times New Roman");
                                    leftCol.Item().PaddingTop(4).Text("Mã QHNS: ").FontSize(10).FontColor(Colors.Black).FontFamily("Times New Roman");
                                });

                                // Bên phải: Mẫu số C30-HD
                                row.RelativeItem().Column(rightCol =>
                                {
                                    rightCol.Item().AlignRight().Text("Mẫu số C30 - HD")
                                        .FontSize(10)
                                        .Bold()
                                        .FontColor(Colors.Black)
                                        .FontFamily("Times New Roman");
                                    rightCol.Item().PaddingTop(2).AlignRight().Text("(Ban hành kèm theo thông tư 107/2017/TT-BTC")
                                        .FontSize(8)
                                        .Italic()
                                        .FontColor(Colors.Grey.Darken1)
                                        .FontFamily("Times New Roman");
                                    rightCol.Item().AlignRight().Text("ngày 24/11/2017)")
                                        .FontSize(8)
                                        .Italic()
                                        .FontColor(Colors.Grey.Darken1)
                                        .FontFamily("Times New Roman");
                                });
                            });

                            column.Item().PaddingTop(15);

                            // Tiêu đề chính
                            column.Item().AlignCenter().Text("BÁO CÁO NXT THEO GIÁ TRỊ")
                                .FontSize(20)
                                .Bold()
                                .FontColor(Colors.Black)
                                .FontFamily("Times New Roman");

                            column.Item().PaddingTop(8);
                            column.Item().AlignCenter().Text($"Ngày xuất: {DateTime.Now:dd/MM/yyyy HH:mm}")
                                .FontSize(10)
                                .FontColor(Colors.Grey.Darken2)
                                .FontFamily("Times New Roman");

                            column.Item().PaddingTop(20);

                            // Thông tin bộ lọc
                            column.Item().PaddingBottom(20).Column(filterCol =>
                            {
                                filterCol.Item().PaddingBottom(6).Row(row =>
                                {
                                    row.AutoItem().Text("Kho: ").FontSize(10).FontColor(Colors.Black).FontFamily("Times New Roman");
                                    row.RelativeItem().Text(warehouseName).FontSize(10).Bold().FontColor(Colors.Black).FontFamily("Times New Roman");
                                });

                                filterCol.Item().Row(row =>
                                {
                                    row.AutoItem().Text("Kỳ: ").FontSize(10).FontColor(Colors.Black).FontFamily("Times New Roman");
                                    row.RelativeItem().Text($"{from:dd/MM/yyyy} đến {to:dd/MM/yyyy}").FontSize(10).Bold().FontColor(Colors.Black).FontFamily("Times New Roman");
                                });
                            });

                            // Bảng dữ liệu
                            column.Item().Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.RelativeColumn(1f); // Mã VT
                                    columns.RelativeColumn(2.2f); // Tên VT
                                    columns.RelativeColumn(0.7f); // ĐVT
                                    columns.RelativeColumn(0.9f); // Tồn đầu
                                    columns.RelativeColumn(1.1f); // GT đầu
                                    columns.RelativeColumn(0.9f); // Nhập SL
                                    columns.RelativeColumn(1.1f); // Nhập GT
                                    columns.RelativeColumn(0.9f); // Xuất SL
                                    columns.RelativeColumn(1.1f); // Xuất GT
                                    columns.RelativeColumn(0.9f); // Tồn cuối
                                    columns.RelativeColumn(1.1f); // GT cuối
                                });

                                table.Header(header =>
                                {
                                    header.Cell().Element(HeaderCellStyle).Text("Mã VT").Bold().FontSize(9).FontFamily("Times New Roman");
                                    header.Cell().Element(HeaderCellStyle).Text("Tên vật tư").Bold().FontSize(9).FontFamily("Times New Roman");
                                    header.Cell().Element(HeaderCellStyle).AlignCenter().Text("ĐVT").Bold().FontSize(9).FontFamily("Times New Roman");
                                    header.Cell().Element(HeaderCellStyle).AlignRight().Text("Tồn đầu").Bold().FontSize(9).FontFamily("Times New Roman");
                                    header.Cell().Element(HeaderCellStyle).AlignRight().Text("GT đầu").Bold().FontSize(9).FontFamily("Times New Roman");
                                    header.Cell().Element(HeaderCellStyle).AlignRight().Text("Nhập SL").Bold().FontSize(9).FontFamily("Times New Roman");
                                    header.Cell().Element(HeaderCellStyle).AlignRight().Text("Nhập GT").Bold().FontSize(9).FontFamily("Times New Roman");
                                    header.Cell().Element(HeaderCellStyle).AlignRight().Text("Xuất SL").Bold().FontSize(9).FontFamily("Times New Roman");
                                    header.Cell().Element(HeaderCellStyle).AlignRight().Text("Xuất GT").Bold().FontSize(9).FontFamily("Times New Roman");
                                    header.Cell().Element(HeaderCellStyle).AlignRight().Text("Tồn cuối").Bold().FontSize(9).FontFamily("Times New Roman");
                                    header.Cell().Element(HeaderCellStyle).AlignRight().Text("GT cuối").Bold().FontSize(9).FontFamily("Times New Roman");
                                });

                                int rowIndex = 0;
                                foreach (var d in data.OrderBy(d => d.MaterialCode))
                                {
                                    bool isEvenRow = rowIndex % 2 == 0;
                                    table.Cell().Element(c => DataCellStyle(c, isEvenRow)).Text(d.MaterialCode ?? "").FontSize(8).FontFamily("Times New Roman");
                                    table.Cell().Element(c => DataCellStyle(c, isEvenRow)).Text(d.MaterialName ?? "").FontSize(8).FontFamily("Times New Roman");
                                    table.Cell().Element(c => DataCellStyle(c, isEvenRow)).AlignCenter().Text(d.Unit ?? "").FontSize(8).FontFamily("Times New Roman");
                                    table.Cell().Element(c => DataCellStyle(c, isEvenRow)).AlignRight().Text(FormatQty(d.BeginQty)).FontSize(8).FontFamily("Times New Roman");
                                    table.Cell().Element(c => DataCellStyle(c, isEvenRow)).AlignRight().Text(FormatMoney(d.BeginValue)).FontSize(8).FontFamily("Times New Roman");
                                    table.Cell().Element(c => DataCellStyle(c, isEvenRow)).AlignRight().Text(FormatQty(d.InQty)).FontSize(8).FontFamily("Times New Roman");
                                    table.Cell().Element(c => DataCellStyle(c, isEvenRow)).AlignRight().Text(FormatMoney(d.InValue)).FontSize(8).FontFamily("Times New Roman");
                                    table.Cell().Element(c => DataCellStyle(c, isEvenRow)).AlignRight().Text(FormatQty(d.OutQty)).FontSize(8).FontFamily("Times New Roman");
                                    table.Cell().Element(c => DataCellStyle(c, isEvenRow)).AlignRight().Text(FormatMoney(d.OutValue)).FontSize(8).FontFamily("Times New Roman");
                                    table.Cell().Element(c => DataCellStyle(c, isEvenRow)).AlignRight().Text(FormatQty(d.EndQty)).FontSize(8).FontFamily("Times New Roman");
                                    table.Cell().Element(c => DataCellStyle(c, isEvenRow)).AlignRight().Text(FormatMoney(d.EndValue)).FontSize(8).FontFamily("Times New Roman").Bold();
                                    rowIndex++;
                                }
                            });

                            column.Item().PaddingTop(25).PaddingBottom(15).LineHorizontal(1).LineColor(Colors.Black);

                            // Tổng hợp
                            column.Item().PaddingTop(10).Column(summaryCol =>
                            {
                                summaryCol.Item().PaddingBottom(8).Row(row =>
                                {
                                    row.AutoItem().Text("Tổng giá trị tồn đầu: ").FontSize(11).FontColor(Colors.Black).FontFamily("Times New Roman");
                                    row.RelativeItem().Text(FormatMoney(totalBeginValue)).FontSize(11).Bold().FontFamily("Times New Roman").FontColor(Colors.Black);
                                });

                                summaryCol.Item().PaddingBottom(8).Row(row =>
                                {
                                    row.AutoItem().Text("Tổng giá trị nhập: ").FontSize(11).FontColor(Colors.Black).FontFamily("Times New Roman");
                                    row.RelativeItem().Text(FormatMoney(totalInValue)).FontSize(11).Bold().FontFamily("Times New Roman").FontColor(Colors.Black);
                                });

                                summaryCol.Item().PaddingBottom(8).Row(row =>
                                {
                                    row.AutoItem().Text("Tổng giá trị xuất: ").FontSize(11).FontColor(Colors.Black).FontFamily("Times New Roman");
                                    row.RelativeItem().Text(FormatMoney(totalOutValue)).FontSize(11).Bold().FontFamily("Times New Roman").FontColor(Colors.Black);
                                });

                                summaryCol.Item().Row(row =>
                                {
                                    row.AutoItem().Text("Tổng giá trị tồn cuối: ").FontSize(11).FontColor(Colors.Black).FontFamily("Times New Roman");
                                    row.RelativeItem().Text(FormatMoney(totalEndValue)).FontSize(12).Bold().FontFamily("Times New Roman").FontColor(Colors.Black);
                                });
                            });
                        });

                    page.Footer()
                        .PaddingTop(10)
                        .BorderTop(1)
                        .BorderColor(Colors.Grey.Lighten1)
                        .AlignCenter()
                        .Text(x =>
                        {
                            x.Span("Trang ").FontSize(9).FontColor(Colors.Grey.Medium).FontFamily("Times New Roman");
                            x.CurrentPageNumber().FontSize(9).FontColor(Colors.Grey.Darken1).Bold().FontFamily("Times New Roman");
                            x.Span(" / ").FontSize(9).FontColor(Colors.Grey.Medium).FontFamily("Times New Roman");
                            x.TotalPages().FontSize(9).FontColor(Colors.Grey.Darken1).Bold().FontFamily("Times New Roman");
                        });
                });
            });

            var pdfBytes = document.GeneratePdf();
            var fileName = $"NXTTheoGiaTri_{from:yyyyMMdd}_{to:yyyyMMdd}.pdf";
            return File(pdfBytes, "application/pdf", fileName);
        }

        [HttpGet]
        [RequirePermission("Reports", "Read")]
        public async Task<IActionResult> ExportPdfInventoryValuation([FromQuery] ReportFilterVM f)
        {
            var (from, to) = NormRange(f.From, f.To);
            f.From = from;
            f.To = to;

            var costingService = HttpContext.RequestServices.GetRequiredService<ICostingService>();

            var lotsQuery = _db.StockLots
                .AsNoTracking()
                .Include(l => l.Material)
                .Include(l => l.Warehouse)
                .Where(l => l.Quantity > 0);

            if (f.WarehouseId.HasValue)
                lotsQuery = lotsQuery.Where(l => l.WarehouseId == f.WarehouseId.Value);

            var lots = await lotsQuery.ToListAsync();

            var grouped = lots.GroupBy(l => new { l.MaterialId, l.WarehouseId });

            var data = new List<InventoryValuationRowVM>();

            foreach (var group in grouped)
            {
                var material = group.First().Material;
                var warehouse = group.First().Warehouse;
                var groupQty = group.Sum(l => l.Quantity);
                var costingMethod = material?.CostingMethod ?? CostingMethod.WeightedAverage;

                decimal unitCost = 0m;
                if (groupQty > 0)
                {
                    if (costingMethod == CostingMethod.FIFO)
                    {
                        var lotCosts = await costingService.GetLotCostsAsync(group.Key.WarehouseId, group.Key.MaterialId, to);
                        var groupValue = lotCosts.Sum(l => l.quantity * l.unitPrice);
                        unitCost = groupQty > 0 ? Math.Round(groupValue / groupQty, 2) : 0m;
                    }
                    else
                    {
                        var lotCosts = await costingService.GetLotCostsAsync(group.Key.WarehouseId, group.Key.MaterialId, to);
                        var groupValue = lotCosts.Sum(l => l.quantity * l.unitPrice);
                        var totalLotQty = lotCosts.Sum(l => l.quantity);
                        unitCost = totalLotQty > 0 ? Math.Round(groupValue / totalLotQty, 2) : 0m;
                    }
                }

                data.Add(new InventoryValuationRowVM
                {
                    MaterialId = material?.Id ?? 0,
                    MaterialCode = material?.Code ?? "",
                    MaterialName = material?.Name ?? "",
                    Unit = material?.Unit ?? "",
                    WarehouseName = warehouse?.Name,
                    Quantity = groupQty,
                    UnitCost = unitCost,
                    CostingMethod = costingMethod
                });
            }

            decimal totalValue = data.Sum(d => d.TotalValue);
            decimal totalQty = data.Sum(d => d.Quantity);

            string warehouseName = await GetWarehouseNameAsync(f.WarehouseId);

            // Tạo PDF
            QuestPDF.Settings.License = LicenseType.Community;
            var document = QuestPDF.Fluent.Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(2f, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Times New Roman"));

                    page.Content()
                        .Column(column =>
                        {
                            // Header - Form number và thông tin đơn vị
                            column.Item().Row(row =>
                            {
                                // Bên trái: Đơn vị, Mã QHNS
                                row.RelativeItem().Column(leftCol =>
                                {
                                    leftCol.Item().Text("Đơn vị: ").FontSize(10).FontColor(Colors.Black).FontFamily("Times New Roman");
                                    leftCol.Item().PaddingTop(4).Text("Mã QHNS: ").FontSize(10).FontColor(Colors.Black).FontFamily("Times New Roman");
                                });

                                // Bên phải: Mẫu số C30-HD
                                row.RelativeItem().Column(rightCol =>
                                {
                                    rightCol.Item().AlignRight().Text("Mẫu số C30 - HD")
                                        .FontSize(10)
                                        .Bold()
                                        .FontColor(Colors.Black)
                                        .FontFamily("Times New Roman");
                                    rightCol.Item().PaddingTop(2).AlignRight().Text("(Ban hành kèm theo thông tư 107/2017/TT-BTC")
                                        .FontSize(8)
                                        .Italic()
                                        .FontColor(Colors.Grey.Darken1)
                                        .FontFamily("Times New Roman");
                                    rightCol.Item().AlignRight().Text("ngày 24/11/2017)")
                                        .FontSize(8)
                                        .Italic()
                                        .FontColor(Colors.Grey.Darken1)
                                        .FontFamily("Times New Roman");
                                });
                            });

                            column.Item().PaddingTop(15);

                            // Tiêu đề chính
                            column.Item().AlignCenter().Text("BÁO CÁO ĐỊNH GIÁ TỒN KHO CUỐI KỲ")
                                .FontSize(20)
                                .Bold()
                                .FontColor(Colors.Black)
                                .FontFamily("Times New Roman");

                            column.Item().PaddingTop(8);
                            column.Item().AlignCenter().Text($"Ngày xuất: {DateTime.Now:dd/MM/yyyy HH:mm}")
                                .FontSize(10)
                                .FontColor(Colors.Grey.Darken2)
                                .FontFamily("Times New Roman");

                            column.Item().PaddingTop(20);

                            // Thông tin bộ lọc
                            column.Item().PaddingBottom(20).Column(filterCol =>
                            {
                                filterCol.Item().PaddingBottom(6).Row(row =>
                                {
                                    row.AutoItem().Text("Kho: ").FontSize(10).FontColor(Colors.Black).FontFamily("Times New Roman");
                                    row.RelativeItem().Text(warehouseName).FontSize(10).Bold().FontColor(Colors.Black).FontFamily("Times New Roman");
                                });

                                filterCol.Item().Row(row =>
                                {
                                    row.AutoItem().Text("Cuối kỳ: ").FontSize(10).FontColor(Colors.Black).FontFamily("Times New Roman");
                                    row.RelativeItem().Text($"{to:dd/MM/yyyy}").FontSize(10).Bold().FontColor(Colors.Black).FontFamily("Times New Roman");
                                });
                            });

                            // Bảng dữ liệu
                            column.Item().Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.RelativeColumn(1.5f); // Mã VT
                                    columns.RelativeColumn(3f); // Tên VT
                                    columns.RelativeColumn(1f); // ĐVT
                                    columns.RelativeColumn(2f); // Kho
                                    columns.RelativeColumn(1.5f); // SL
                                    columns.RelativeColumn(1.8f); // Đơn giá
                                    columns.RelativeColumn(2.2f); // Tổng giá trị
                                    columns.RelativeColumn(1.5f); // PP tính
                                });

                                table.Header(header =>
                                {
                                    header.Cell().Element(HeaderCellStyle).Text("Mã VT").Bold().FontSize(9).FontFamily("Times New Roman");
                                    header.Cell().Element(HeaderCellStyle).Text("Tên vật tư").Bold().FontSize(9).FontFamily("Times New Roman");
                                    header.Cell().Element(HeaderCellStyle).AlignCenter().Text("ĐVT").Bold().FontSize(9).FontFamily("Times New Roman");
                                    header.Cell().Element(HeaderCellStyle).Text("Kho").Bold().FontSize(9).FontFamily("Times New Roman");
                                    header.Cell().Element(HeaderCellStyle).AlignRight().Text("SL").Bold().FontSize(9).FontFamily("Times New Roman");
                                    header.Cell().Element(HeaderCellStyle).AlignRight().Text("Đơn giá").Bold().FontSize(9).FontFamily("Times New Roman");
                                    header.Cell().Element(HeaderCellStyle).AlignRight().Text("Tổng giá trị").Bold().FontSize(9).FontFamily("Times New Roman");
                                    header.Cell().Element(HeaderCellStyle).AlignCenter().Text("PP tính").Bold().FontSize(9).FontFamily("Times New Roman");
                                });

                                int rowIndex = 0;
                                foreach (var d in data.OrderBy(d => d.MaterialCode).ThenBy(d => d.WarehouseName))
                                {
                                    bool isEvenRow = rowIndex % 2 == 0;
                                    table.Cell().Element(c => DataCellStyle(c, isEvenRow)).Text(d.MaterialCode ?? "").FontSize(8).FontFamily("Times New Roman");
                                    table.Cell().Element(c => DataCellStyle(c, isEvenRow)).Text(d.MaterialName ?? "").FontSize(8).FontFamily("Times New Roman");
                                    table.Cell().Element(c => DataCellStyle(c, isEvenRow)).AlignCenter().Text(d.Unit ?? "").FontSize(8).FontFamily("Times New Roman");
                                    table.Cell().Element(c => DataCellStyle(c, isEvenRow)).Text(d.WarehouseName ?? "").FontSize(8).FontFamily("Times New Roman");
                                    table.Cell().Element(c => DataCellStyle(c, isEvenRow)).AlignRight().Text(FormatQty(d.Quantity)).FontSize(8).FontFamily("Times New Roman");
                                    table.Cell().Element(c => DataCellStyle(c, isEvenRow)).AlignRight().Text(FormatMoney(d.UnitCost)).FontSize(8).FontFamily("Times New Roman");
                                    table.Cell().Element(c => DataCellStyle(c, isEvenRow)).AlignRight().Text(FormatMoney(d.TotalValue)).FontSize(8).FontFamily("Times New Roman").Bold();
                                    table.Cell().Element(c => DataCellStyle(c, isEvenRow)).AlignCenter().Text(d.CostingMethodName ?? "").FontSize(8).FontFamily("Times New Roman");
                                    rowIndex++;
                                }
                            });

                            // Tổng hợp
                            column.Item().PaddingTop(25).Column(summaryCol =>
                            {
                                summaryCol.Item().PaddingBottom(8).Row(row =>
                                {
                                    row.AutoItem().Text("Tổng số dòng: ").FontSize(11).FontColor(Colors.Black).FontFamily("Times New Roman");
                                    row.RelativeItem().Text(data.Count.ToString()).FontSize(11).Bold().FontColor(Colors.Black).FontFamily("Times New Roman");
                                });

                                summaryCol.Item().PaddingBottom(8).Row(row =>
                                {
                                    row.AutoItem().Text("Tổng số lượng: ").FontSize(11).FontColor(Colors.Black).FontFamily("Times New Roman");
                                    row.RelativeItem().Text(FormatQty(totalQty)).FontSize(11).Bold().FontFamily("Times New Roman").FontColor(Colors.Black);
                                });

                                summaryCol.Item().Row(row =>
                                {
                                    row.AutoItem().Text("Tổng giá trị tồn kho: ").FontSize(11).FontColor(Colors.Black).FontFamily("Times New Roman");
                                    row.RelativeItem().Text(FormatMoney(totalValue)).FontSize(12).Bold().FontFamily("Times New Roman").FontColor(Colors.Black);
                                });
                            });
                        });

                    page.Footer()
                        .PaddingTop(10)
                        .BorderTop(1)
                        .BorderColor(Colors.Grey.Lighten1)
                        .AlignCenter()
                        .Text(x =>
                        {
                            x.Span("Trang ").FontSize(9).FontColor(Colors.Grey.Medium).FontFamily("Times New Roman");
                            x.CurrentPageNumber().FontSize(9).FontColor(Colors.Grey.Darken1).Bold().FontFamily("Times New Roman");
                            x.Span(" / ").FontSize(9).FontColor(Colors.Grey.Medium).FontFamily("Times New Roman");
                            x.TotalPages().FontSize(9).FontColor(Colors.Grey.Darken1).Bold().FontFamily("Times New Roman");
                        });
                });
            });

            var pdfBytes = document.GeneratePdf();
            var fileName = $"DinhGiaTonKhoCuoiKy_{to:yyyyMMdd}.pdf";
            return File(pdfBytes, "application/pdf", fileName);
        }

        private static IContainer HeaderCellStyle(IContainer container)
        {
            return container
                .Border(1)
                .BorderColor(Colors.Black)
                .PaddingVertical(8)
                .PaddingHorizontal(6)
                .Background(Colors.White)
                .AlignMiddle()
                .AlignCenter();
        }

        private static IContainer DataCellStyle(IContainer container, bool isEvenRow)
        {
            return container
                .Border(1)
                .BorderColor(Colors.Black)
                .PaddingVertical(6)
                .PaddingHorizontal(5)
                .Background(Colors.White)
                .AlignMiddle();
        }

        private static string FormatQty(decimal v)
        {
            return (v % 1m == 0m) ? ((long)v).ToString() : v.ToString("0.##");
        }

        private static string FormatMoney(decimal v)
        {
            return v.ToString("N0") + " đ";
        }

    }
}

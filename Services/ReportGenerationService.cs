using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MNBEMART.Data;
using MNBEMART.Models;

namespace MNBEMART.Services
{
    public interface IReportGenerationService
    {
        Task<ReportData> GenerateStockReportAsync(DateTime? fromDate = null, DateTime? toDate = null, int? warehouseId = null);
        Task<ReportData> GenerateReceiptReportAsync(DateTime? fromDate = null, DateTime? toDate = null, int? warehouseId = null);
        Task<ReportData> GenerateIssueReportAsync(DateTime? fromDate = null, DateTime? toDate = null, int? warehouseId = null);
        Task<ReportData> GenerateRevenueReportAsync(DateTime? fromDate = null, DateTime? toDate = null, int? warehouseId = null, string groupBy = "day", bool detail = false);
        Task<ReportData> GenerateProfitLossReportAsync(DateTime? fromDate = null, DateTime? toDate = null, int? warehouseId = null, string groupBy = "day");
        Task<ReportData> GenerateTurnoverRateReportAsync(DateTime? fromDate = null, DateTime? toDate = null, int? warehouseId = null);
    }

    public class ReportData
    {
        public string ReportType { get; set; } = "";
        public string Title { get; set; } = "";
        public DateTime GeneratedAt { get; set; } = DateTime.Now;
        public Dictionary<string, object> Metadata { get; set; } = new();
        public List<Dictionary<string, object>> Rows { get; set; } = new();
        public string Summary { get; set; } = "";
    }

    public class ReportGenerationService : IReportGenerationService
    {
        private readonly AppDbContext _db;
        private readonly ILogger<ReportGenerationService> _logger;

        public ReportGenerationService(AppDbContext db, ILogger<ReportGenerationService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<ReportData> GenerateStockReportAsync(DateTime? fromDate = null, DateTime? toDate = null, int? warehouseId = null)
        {
            // Nếu có fromDate và toDate, tính báo cáo tồn kho theo kỳ (đầu kỳ, nhập, xuất, cuối kỳ)
            if (fromDate.HasValue && toDate.HasValue)
            {
                // Lấy tồn kho theo lô từ StockLots (tồn cuối kỳ)
                var lotsQuery = _db.StockLots
                    .AsNoTracking()
                    .Include(l => l.Material)
                    .Include(l => l.Warehouse)
                    .Where(l => l.Quantity > 0);

                if (warehouseId.HasValue)
                    lotsQuery = lotsQuery.Where(l => l.WarehouseId == warehouseId.Value);

                var lots = await lotsQuery.ToListAsync();

                // Tính nhập/xuất trong kỳ từ StockBalance
                var qInPeriod = _db.StockBalances.AsNoTracking()
                    .Where(x => x.Date.Date >= fromDate.Value.Date && x.Date.Date <= toDate.Value.Date);

                if (warehouseId.HasValue)
                    qInPeriod = qInPeriod.Where(x => x.WarehouseId == warehouseId.Value);

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

                // Hiển thị theo từng lô (giống ReportsController.Inventory)
                var rows = new List<Dictionary<string, object>>();
                foreach (var lot in lots)
                {
                    var (inQty, outQty) = movDict.TryGetValue(
                        (lot.MaterialId, lot.WarehouseId),
                        out var t
                    ) ? t : (0m, 0m);

                    // Tính tồn đầu kỳ = Tồn cuối kỳ - Nhập + Xuất
                    var beginQty = lot.Quantity - inQty + outQty;

                    rows.Add(new Dictionary<string, object>
                    {
                        ["Kho"] = lot.Warehouse?.Name ?? "",
                        ["Mã"] = lot.Material?.Code ?? "",
                        ["Tên"] = lot.Material?.Name ?? "",
                        ["Số lô"] = lot.LotNumber ?? "",
                        ["NSX"] = lot.ManufactureDate?.ToString("dd/MM/yyyy") ?? "",
                        ["HSD"] = lot.ExpiryDate?.ToString("dd/MM/yyyy") ?? "",
                        ["Đầu kỳ"] = beginQty,
                        ["Nhập"] = inQty,
                        ["Xuất"] = outQty,
                        ["Cuối kỳ"] = lot.Quantity,
                        ["Đơn vị"] = lot.Material?.Unit ?? ""
                    });
                }

                // Sắp xếp theo nguyên liệu, sau đó theo HSD (ưu tiên hết hạn sớm)
                rows = rows.OrderBy(r => r["Mã"]?.ToString() ?? "")
                    .ThenBy(r => {
                        var hsd = r["HSD"]?.ToString();
                        if (string.IsNullOrEmpty(hsd)) return DateTime.MaxValue;
                        // Parse date từ format dd/MM/yyyy
                        var parts = hsd.Split('/');
                        if (parts.Length == 3 && 
                            int.TryParse(parts[0], out var day) &&
                            int.TryParse(parts[1], out var month) &&
                            int.TryParse(parts[2], out var year))
                        {
                            try
                            {
                                return new DateTime(year, month, day);
                            }
                            catch
                            {
                                return DateTime.MaxValue;
                            }
                        }
                        return DateTime.MaxValue;
                    })
                    .ToList();

                var totalBegin = rows.Sum(r => Convert.ToDecimal(r["Đầu kỳ"]));
                var totalIn = rows.Sum(r => Convert.ToDecimal(r["Nhập"]));
                var totalOut = rows.Sum(r => Convert.ToDecimal(r["Xuất"]));
                var totalEnd = rows.Sum(r => Convert.ToDecimal(r["Cuối kỳ"]));
                var warehouseCount = rows.Select(r => r["Kho"].ToString()).Distinct().Count();
                var lotCount = rows.Count;
                var materialCount = rows.Select(r => $"{r["Mã"]}_{r["Kho"]}").Distinct().Count();

                return new ReportData
                {
                    ReportType = "Stock",
                    Title = $"Báo cáo tồn kho {fromDate.Value:MM/yyyy}",
                    GeneratedAt = DateTime.Now,
                    Metadata = new Dictionary<string, object>
                    {
                        ["Từ ngày"] = fromDate.Value.ToString("dd/MM/yyyy"),
                        ["Đến ngày"] = toDate.Value.ToString("dd/MM/yyyy"),
                        ["Kho"] = warehouseId.HasValue ? rows.FirstOrDefault()?["Kho"]?.ToString() ?? "Tất cả" : "Tất cả",
                        ["Số kho"] = warehouseCount,
                        ["Số lô"] = lotCount,
                        ["Số nguyên liệu"] = materialCount
                    },
                    Rows = rows,
                    Summary = $"Tổng số lô: {lotCount} | Đầu kỳ: {totalBegin:0.###} | Nhập: {totalIn:0.###} | Xuất: {totalOut:0.###} | Cuối kỳ: {totalEnd:0.###}"
                };
            }
            else
            {
                // Nếu không có date range, lấy tồn kho hiện tại
                var query = _db.Stocks
                    .Include(s => s.Warehouse)
                    .Include(s => s.Material)
                    .AsQueryable();

                if (warehouseId.HasValue)
                {
                    query = query.Where(s => s.WarehouseId == warehouseId.Value);
                }

                var stocks = await query
                    .OrderBy(s => s.Warehouse.Name)
                    .ThenBy(s => s.Material.Code)
                    .ToListAsync();

                var rows = stocks.Select(s => new Dictionary<string, object>
                {
                    ["Kho"] = s.Warehouse?.Name ?? "",
                    ["Mã"] = s.Material?.Code ?? "",
                    ["Tên"] = s.Material?.Name ?? "",
                    ["Số lượng"] = s.Quantity,
                    ["Đơn vị"] = s.Material?.Unit ?? ""
                }).ToList();

                var totalQty = stocks.Sum(s => s.Quantity);
                var warehouseCount = stocks.Select(s => s.WarehouseId).Distinct().Count();
                var materialCount = stocks.Select(s => s.MaterialId).Distinct().Count();

                return new ReportData
                {
                    ReportType = "Stock",
                    Title = "Báo cáo tồn kho",
                    GeneratedAt = DateTime.Now,
                    Metadata = new Dictionary<string, object>
                    {
                        ["Từ ngày"] = "Tất cả",
                        ["Đến ngày"] = "Tất cả",
                        ["Kho"] = warehouseId.HasValue ? stocks.FirstOrDefault()?.Warehouse?.Name : "Tất cả",
                        ["Số kho"] = warehouseCount,
                        ["Số nguyên liệu"] = materialCount
                    },
                    Rows = rows,
                    Summary = $"Tổng số lượng tồn: {totalQty:0.###} | Số kho: {warehouseCount} | Số nguyên liệu: {materialCount}"
                };
            }
        }

        public async Task<ReportData> GenerateReceiptReportAsync(DateTime? fromDate = null, DateTime? toDate = null, int? warehouseId = null)
        {
            var query = _db.StockReceipts
                .Include(r => r.Warehouse)
                .Include(r => r.Details)
                .ThenInclude(d => d.Material)
                .AsQueryable();

            if (fromDate.HasValue)
            {
                query = query.Where(r => r.CreatedAt >= fromDate.Value);
            }

            if (toDate.HasValue)
            {
                query = query.Where(r => r.CreatedAt <= toDate.Value);
            }

            if (warehouseId.HasValue)
            {
                query = query.Where(r => r.WarehouseId == warehouseId.Value);
            }

            var receipts = await query
                .OrderBy(r => r.CreatedAt)
                .ToListAsync();

            var rows = new List<Dictionary<string, object>>();
            foreach (var receipt in receipts)
            {
                foreach (var detail in receipt.Details)
                {
                    rows.Add(new Dictionary<string, object>
                    {
                        ["Ngày"] = receipt.CreatedAt.ToString("dd/MM/yyyy"),
                        ["Số phiếu"] = receipt.ReceiptNumber ?? "",
                        ["Kho"] = receipt.Warehouse?.Name ?? "",
                        ["Mã"] = detail.Material?.Code ?? "",
                        ["Tên"] = detail.Material?.Name ?? "",
                        ["Số lượng"] = detail.Quantity,
                        ["Đơn giá"] = detail.UnitPrice,
                        ["Thành tiền"] = (decimal)detail.Quantity * detail.UnitPrice
                    });
                }
            }

            var totalQty = receipts.Sum(r => r.Details.Sum(d => (decimal)d.Quantity));
            var totalValue = receipts.Sum(r => r.Details.Sum(d => (decimal)d.Quantity * d.UnitPrice));

            return new ReportData
            {
                ReportType = "Receipt",
                Title = "Báo cáo nhập kho",
                GeneratedAt = DateTime.Now,
                Metadata = new Dictionary<string, object>
                {
                    ["Từ ngày"] = fromDate?.ToString("dd/MM/yyyy") ?? "Tất cả",
                    ["Đến ngày"] = toDate?.ToString("dd/MM/yyyy") ?? "Tất cả",
                    ["Số phiếu"] = receipts.Count
                },
                Rows = rows,
                Summary = $"Tổng số lượng nhập: {totalQty:0.###} | Tổng giá trị: {totalValue:N0} đ | Số phiếu: {receipts.Count}"
            };
        }

        public async Task<ReportData> GenerateIssueReportAsync(DateTime? fromDate = null, DateTime? toDate = null, int? warehouseId = null)
        {
            var query = _db.StockIssues
                .Include(i => i.Warehouse)
                .Include(i => i.Details)
                .ThenInclude(d => d.Material)
                .AsQueryable();

            if (fromDate.HasValue)
            {
                query = query.Where(i => i.CreatedAt >= fromDate.Value);
            }

            if (toDate.HasValue)
            {
                query = query.Where(i => i.CreatedAt <= toDate.Value);
            }

            if (warehouseId.HasValue)
            {
                query = query.Where(i => i.WarehouseId == warehouseId.Value);
            }

            var issues = await query
                .OrderBy(i => i.CreatedAt)
                .ToListAsync();

            var rows = new List<Dictionary<string, object>>();
            foreach (var issue in issues)
            {
                foreach (var detail in issue.Details)
                {
                    rows.Add(new Dictionary<string, object>
                    {
                        ["Ngày"] = issue.CreatedAt.ToString("dd/MM/yyyy"),
                        ["Số phiếu"] = issue.IssueNumber ?? "",
                        ["Kho"] = issue.Warehouse?.Name ?? "",
                        ["Mã"] = detail.Material?.Code ?? "",
                        ["Tên"] = detail.Material?.Name ?? "",
                        ["Số lượng"] = detail.Quantity,
                        ["Đơn giá"] = detail.UnitPrice,
                        ["Thành tiền"] = (decimal)detail.Quantity * detail.UnitPrice
                    });
                }
            }

            var totalQty = issues.Sum(i => i.Details.Sum(d => (decimal)d.Quantity));
            var totalValue = issues.Sum(i => i.Details.Sum(d => (decimal)d.Quantity * d.UnitPrice));

            return new ReportData
            {
                ReportType = "Issue",
                Title = "Báo cáo xuất kho",
                GeneratedAt = DateTime.Now,
                Metadata = new Dictionary<string, object>
                {
                    ["Từ ngày"] = fromDate?.ToString("dd/MM/yyyy") ?? "Tất cả",
                    ["Đến ngày"] = toDate?.ToString("dd/MM/yyyy") ?? "Tất cả",
                    ["Số phiếu"] = issues.Count
                },
                Rows = rows,
                Summary = $"Tổng số lượng xuất: {totalQty:0.###} | Tổng giá trị: {totalValue:N0} đ | Số phiếu: {issues.Count}"
            };
        }

        public async Task<ReportData> GenerateRevenueReportAsync(DateTime? fromDate = null, DateTime? toDate = null, int? warehouseId = null, string groupBy = "day", bool detail = false)
        {
            var from = fromDate ?? DateTime.Now.AddMonths(-1);
            var to = toDate ?? DateTime.Now;
            groupBy = string.IsNullOrWhiteSpace(groupBy) ? "day" : groupBy.ToLowerInvariant();

            var query = _db.StockIssueDetails
                .Include(d => d.StockIssue)
                .ThenInclude(i => i.Warehouse)
                .Include(d => d.Material)
                .Where(d => d.StockIssue.Status == Models.DocumentStatus.DaXuatHang
                         && d.StockIssue.CreatedAt.Date >= from.Date
                         && d.StockIssue.CreatedAt.Date <= to.Date);

            if (warehouseId.HasValue)
            {
                query = query.Where(d => d.StockIssue.WarehouseId == warehouseId.Value);
            }

            var rows = new List<Dictionary<string, object>>();
            decimal totalRevenue = 0;
            decimal totalQty = 0;

            // Nếu detail = true, trả về từng dòng chi tiết (như báo cáo xuất kho)
            if (detail)
            {
                var details = await query
                    .OrderBy(d => d.StockIssue.CreatedAt)
                    .ThenBy(d => d.StockIssue.IssueNumber)
                    .ToListAsync();

                foreach (var d in details)
                {
                    var revenue = (decimal)d.Quantity * d.UnitPrice;
                    rows.Add(new Dictionary<string, object>
                    {
                        ["Ngày"] = d.StockIssue.CreatedAt.ToString("dd/MM/yyyy"),
                        ["Số phiếu"] = d.StockIssue.IssueNumber ?? "",
                        ["Kho"] = d.StockIssue.Warehouse?.Name ?? "",
                        ["Mã"] = d.Material?.Code ?? "",
                        ["Tên"] = d.Material?.Name ?? "",
                        ["Số lượng"] = d.Quantity,
                        ["Đơn giá"] = d.UnitPrice,
                        ["Thành tiền"] = revenue
                    });
                    totalRevenue += revenue;
                    totalQty += (decimal)d.Quantity;
                }
            }
            else
            {
                // Logic group by như cũ
                switch (groupBy)
                {
                    case "year":
                        var yearData = await query
                            .GroupBy(d => d.StockIssue.CreatedAt.Year)
                            .Select(g => new
                            {
                                Year = g.Key,
                                Revenue = g.Sum(z => (decimal)z.Quantity * z.UnitPrice),
                                Qty = g.Sum(z => (decimal)z.Quantity)
                            })
                            .OrderBy(x => x.Year)
                            .ToListAsync();

                        foreach (var item in yearData)
                        {
                            rows.Add(new Dictionary<string, object>
                            {
                                ["Kỳ"] = item.Year.ToString(),
                                ["Doanh thu"] = item.Revenue,
                                ["Số lượng xuất"] = item.Qty
                            });
                            totalRevenue += item.Revenue;
                            totalQty += item.Qty;
                        }
                        break;

                    case "month":
                        var monthData = await query
                            .GroupBy(d => new { d.StockIssue.CreatedAt.Year, d.StockIssue.CreatedAt.Month })
                            .Select(g => new
                            {
                                g.Key.Year,
                                g.Key.Month,
                                Revenue = g.Sum(z => (decimal)z.Quantity * z.UnitPrice),
                                Qty = g.Sum(z => (decimal)z.Quantity)
                            })
                            .OrderBy(x => x.Year).ThenBy(x => x.Month)
                            .ToListAsync();

                        foreach (var item in monthData)
                        {
                            rows.Add(new Dictionary<string, object>
                            {
                                ["Kỳ"] = $"{item.Year}-{item.Month:00}",
                                ["Doanh thu"] = item.Revenue,
                                ["Số lượng xuất"] = item.Qty
                            });
                            totalRevenue += item.Revenue;
                            totalQty += item.Qty;
                        }
                        break;

                    default: // day
                        var dayData = await query
                            .GroupBy(d => d.StockIssue.CreatedAt.Date)
                            .Select(g => new
                            {
                                Day = g.Key,
                                Revenue = g.Sum(z => (decimal)z.Quantity * z.UnitPrice),
                                Qty = g.Sum(z => (decimal)z.Quantity)
                            })
                            .OrderBy(x => x.Day)
                            .ToListAsync();

                        foreach (var item in dayData)
                        {
                            rows.Add(new Dictionary<string, object>
                            {
                                ["Kỳ"] = item.Day.ToString("yyyy-MM-dd"),
                                ["Doanh thu"] = item.Revenue,
                                ["Số lượng xuất"] = item.Qty
                            });
                            totalRevenue += item.Revenue;
                            totalQty += item.Qty;
                        }
                        break;
                }
            }

            var title = detail ? "Báo cáo doanh thu chi tiết" : "Báo cáo doanh thu";
            var summary = detail 
                ? $"Tổng doanh thu: {totalRevenue:N0} đ | Tổng số lượng xuất: {totalQty:0.###} | Số dòng: {rows.Count}"
                : $"Tổng doanh thu: {totalRevenue:N0} đ | Tổng số lượng xuất: {totalQty:0.###} | Số kỳ: {rows.Count}";

            return new ReportData
            {
                ReportType = "Revenue",
                Title = title,
                GeneratedAt = DateTime.Now,
                Metadata = new Dictionary<string, object>
                {
                    ["Từ ngày"] = from.ToString("dd/MM/yyyy"),
                    ["Đến ngày"] = to.ToString("dd/MM/yyyy"),
                    ["Nhóm theo"] = detail ? "Chi tiết" : (groupBy == "year" ? "Năm" : groupBy == "month" ? "Tháng" : "Ngày"),
                    ["Kho"] = warehouseId.HasValue ? (await _db.Warehouses.FindAsync(warehouseId.Value))?.Name ?? "N/A" : "Tất cả"
                },
                Rows = rows,
                Summary = summary
            };
        }

        public async Task<ReportData> GenerateProfitLossReportAsync(DateTime? fromDate = null, DateTime? toDate = null, int? warehouseId = null, string groupBy = "day")
        {
            var from = fromDate ?? DateTime.Now.AddMonths(-1);
            var to = toDate ?? DateTime.Now;
            groupBy = string.IsNullOrWhiteSpace(groupBy) ? "day" : groupBy.ToLowerInvariant();

            var revenueQuery = _db.StockBalances
                .Where(x => x.Date >= from.Date && x.Date <= to.Date);

            var cogsQuery = _db.StockIssueDetails
                .Include(d => d.StockIssue)
                .Where(d => d.StockIssue.Status == Models.DocumentStatus.DaXuatHang
                         && d.StockIssue.CreatedAt.Date >= from.Date
                         && d.StockIssue.CreatedAt.Date <= to.Date);

            if (warehouseId.HasValue)
            {
                revenueQuery = revenueQuery.Where(x => x.WarehouseId == warehouseId.Value);
                cogsQuery = cogsQuery.Where(d => d.StockIssue.WarehouseId == warehouseId.Value);
            }

            var rows = new List<Dictionary<string, object>>();
            decimal totalRevenue = 0;
            decimal totalCOGS = 0;

            switch (groupBy)
            {
                case "year":
                    var revenueYearData = await revenueQuery
                        .GroupBy(x => x.Date.Year)
                        .Select(g => new { Year = g.Key, Revenue = g.Sum(z => z.OutValue) })
                        .ToListAsync();

                    var cogsYearData = await cogsQuery
                        .GroupBy(d => d.StockIssue.CreatedAt.Year)
                        .Select(g => new { Year = g.Key, COGS = g.Sum(z => (z.CostPrice ?? z.UnitPrice) * (decimal)z.Quantity) })
                        .ToListAsync();

                    var years = revenueYearData.Select(r => r.Year)
                        .Union(cogsYearData.Select(c => c.Year))
                        .OrderBy(y => y)
                        .Distinct()
                        .ToList();

                    foreach (var year in years)
                    {
                        var revenue = revenueYearData.FirstOrDefault(r => r.Year == year)?.Revenue ?? 0m;
                        var cogs = cogsYearData.FirstOrDefault(c => c.Year == year)?.COGS ?? 0m;
                        var grossProfit = revenue - cogs;
                        var netProfit = grossProfit; // Assuming no operating expenses

                        rows.Add(new Dictionary<string, object>
                        {
                            ["Kỳ"] = year.ToString(),
                            ["Doanh thu"] = revenue,
                            ["Giá vốn (COGS)"] = cogs,
                            ["Lợi nhuận gộp"] = grossProfit,
                            ["Lợi nhuận ròng"] = netProfit,
                            ["Tỷ suất LN gộp (%)"] = revenue > 0 ? Math.Round((grossProfit / revenue) * 100, 2) : 0,
                            ["Tỷ suất LN ròng (%)"] = revenue > 0 ? Math.Round((netProfit / revenue) * 100, 2) : 0
                        });
                        totalRevenue += revenue;
                        totalCOGS += cogs;
                    }
                    break;

                case "month":
                    var revenueMonthData = await revenueQuery
                        .GroupBy(x => new { x.Date.Year, x.Date.Month })
                        .Select(g => new { g.Key.Year, g.Key.Month, Revenue = g.Sum(z => z.OutValue) })
                        .OrderBy(x => x.Year).ThenBy(x => x.Month)
                        .ToListAsync();

                    var cogsMonthData = await cogsQuery
                        .GroupBy(d => new { d.StockIssue.CreatedAt.Year, d.StockIssue.CreatedAt.Month })
                        .Select(g => new { g.Key.Year, g.Key.Month, COGS = g.Sum(z => (z.CostPrice ?? z.UnitPrice) * (decimal)z.Quantity) })
                        .OrderBy(x => x.Year).ThenBy(x => x.Month)
                        .ToListAsync();

                    var periods = revenueMonthData.Select(r => new { r.Year, r.Month })
                        .Union(cogsMonthData.Select(c => new { c.Year, c.Month }))
                        .OrderBy(p => p.Year).ThenBy(p => p.Month)
                        .Distinct()
                        .ToList();

                    foreach (var period in periods)
                    {
                        var revenue = revenueMonthData.FirstOrDefault(r => r.Year == period.Year && r.Month == period.Month)?.Revenue ?? 0m;
                        var cogs = cogsMonthData.FirstOrDefault(c => c.Year == period.Year && c.Month == period.Month)?.COGS ?? 0m;
                        var grossProfit = revenue - cogs;
                        var netProfit = grossProfit;

                        rows.Add(new Dictionary<string, object>
                        {
                            ["Kỳ"] = $"{period.Year}-{period.Month:00}",
                            ["Doanh thu"] = revenue,
                            ["Giá vốn (COGS)"] = cogs,
                            ["Lợi nhuận gộp"] = grossProfit,
                            ["Lợi nhuận ròng"] = netProfit,
                            ["Tỷ suất LN gộp (%)"] = revenue > 0 ? Math.Round((grossProfit / revenue) * 100, 2) : 0,
                            ["Tỷ suất LN ròng (%)"] = revenue > 0 ? Math.Round((netProfit / revenue) * 100, 2) : 0
                        });
                        totalRevenue += revenue;
                        totalCOGS += cogs;
                    }
                    break;

                default: // day
                    var revenueDayData = await revenueQuery
                        .GroupBy(x => x.Date.Date)
                        .Select(g => new { Day = g.Key, Revenue = g.Sum(z => z.OutValue) })
                        .OrderBy(x => x.Day)
                        .ToListAsync();

                    var cogsDayData = await cogsQuery
                        .GroupBy(d => d.StockIssue.CreatedAt.Date)
                        .Select(g => new { Day = g.Key, COGS = g.Sum(z => (z.CostPrice ?? z.UnitPrice) * (decimal)z.Quantity) })
                        .OrderBy(x => x.Day)
                        .ToListAsync();

                    var days = revenueDayData.Select(r => r.Day)
                        .Union(cogsDayData.Select(c => c.Day))
                        .OrderBy(d => d)
                        .Distinct()
                        .ToList();

                    foreach (var day in days)
                    {
                        var revenue = revenueDayData.FirstOrDefault(r => r.Day == day)?.Revenue ?? 0m;
                        var cogs = cogsDayData.FirstOrDefault(c => c.Day == day)?.COGS ?? 0m;
                        var grossProfit = revenue - cogs;
                        var netProfit = grossProfit;

                        rows.Add(new Dictionary<string, object>
                        {
                            ["Kỳ"] = day.ToString("yyyy-MM-dd"),
                            ["Doanh thu"] = revenue,
                            ["Giá vốn (COGS)"] = cogs,
                            ["Lợi nhuận gộp"] = grossProfit,
                            ["Lợi nhuận ròng"] = netProfit,
                            ["Tỷ suất LN gộp (%)"] = revenue > 0 ? Math.Round((grossProfit / revenue) * 100, 2) : 0,
                            ["Tỷ suất LN ròng (%)"] = revenue > 0 ? Math.Round((netProfit / revenue) * 100, 2) : 0
                        });
                        totalRevenue += revenue;
                        totalCOGS += cogs;
                    }
                    break;
            }

            var totalGrossProfit = totalRevenue - totalCOGS;
            var totalNetProfit = totalGrossProfit;

            return new ReportData
            {
                ReportType = "ProfitLoss",
                Title = "Báo cáo lợi nhuận (P&L)",
                GeneratedAt = DateTime.Now,
                Metadata = new Dictionary<string, object>
                {
                    ["Từ ngày"] = from.ToString("dd/MM/yyyy"),
                    ["Đến ngày"] = to.ToString("dd/MM/yyyy"),
                    ["Nhóm theo"] = groupBy == "year" ? "Năm" : groupBy == "month" ? "Tháng" : "Ngày",
                    ["Kho"] = warehouseId.HasValue ? (await _db.Warehouses.FindAsync(warehouseId.Value))?.Name ?? "N/A" : "Tất cả"
                },
                Rows = rows,
                Summary = $"Tổng doanh thu: {totalRevenue:N0} đ | Tổng giá vốn: {totalCOGS:N0} đ | Lợi nhuận gộp: {totalGrossProfit:N0} đ | Lợi nhuận ròng: {totalNetProfit:N0} đ"
            };
        }

        public async Task<ReportData> GenerateTurnoverRateReportAsync(DateTime? fromDate = null, DateTime? toDate = null, int? warehouseId = null)
        {
            var from = fromDate ?? DateTime.Now.AddMonths(-1);
            var to = toDate ?? DateTime.Now;
            int daysInPeriod = (to.Date - from.Date).Days + 1;

            var issuedQuery = _db.StockIssueDetails
                .Include(d => d.StockIssue)
                .Include(d => d.Material)
                .Where(d => d.StockIssue.Status == Models.DocumentStatus.DaXuatHang
                         && d.StockIssue.CreatedAt.Date >= from.Date
                         && d.StockIssue.CreatedAt.Date <= to.Date);

            if (warehouseId.HasValue)
            {
                issuedQuery = issuedQuery.Where(d => d.StockIssue.WarehouseId == warehouseId.Value);
            }

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

            var rows = new List<Dictionary<string, object>>();

            foreach (var item in issuedData)
            {
                // Lấy tồn cuối kỳ từ StockLots
                var endStock = await _db.StockLots
                    .Where(l => l.MaterialId == item.MaterialId
                            && l.WarehouseId == item.WarehouseId
                            && l.Quantity > 0)
                    .SumAsync(l => l.Quantity);

                // Tính InQty và OutQty trong kỳ từ StockBalance
                var balanceInPeriod = await _db.StockBalances
                    .Where(b => b.MaterialId == item.MaterialId
                             && b.WarehouseId == item.WarehouseId
                             && b.Date >= from.Date
                             && b.Date <= to.Date)
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

                string category = turnoverRate > 12 ? "Fast" : turnoverRate >= 4 ? "Medium" : "Slow";

                rows.Add(new Dictionary<string, object>
                {
                    ["Mã NL"] = item.MaterialCode,
                    ["Tên NL"] = item.MaterialName,
                    ["Kho"] = warehouseName,
                    ["Tồn TB"] = Math.Round(averageStock, 2),
                    ["Tổng xuất"] = Math.Round(item.TotalIssued, 2),
                    ["Đơn vị"] = item.Unit,
                    ["Turnover Rate"] = Math.Round(turnoverRate, 2),
                    ["Số ngày quay vòng"] = daysToTurnover == 999 ? "N/A" : Math.Round(daysToTurnover, 1).ToString(),
                    ["Phân loại"] = category == "Fast" ? "Nhanh (>12)" : category == "Medium" ? "Trung bình (4-12)" : "Chậm (<4)"
                });
            }

            // Order by TurnoverRate DESC (fastest first)
            rows = rows.OrderByDescending(r => {
                var rate = r.ContainsKey("Turnover Rate") ? Convert.ToDecimal(r["Turnover Rate"]) : 0m;
                return rate;
            }).ToList();

            var fastCount = rows.Count(r => r.ContainsKey("Phân loại") && r["Phân loại"].ToString().Contains("Nhanh"));
            var mediumCount = rows.Count(r => r.ContainsKey("Phân loại") && r["Phân loại"].ToString().Contains("Trung bình"));
            var slowCount = rows.Count(r => r.ContainsKey("Phân loại") && r["Phân loại"].ToString().Contains("Chậm"));

            return new ReportData
            {
                ReportType = "TurnoverRate",
                Title = "Báo cáo tỷ lệ quay vòng hàng tồn kho",
                GeneratedAt = DateTime.Now,
                Metadata = new Dictionary<string, object>
                {
                    ["Từ ngày"] = from.ToString("dd/MM/yyyy"),
                    ["Đến ngày"] = to.ToString("dd/MM/yyyy"),
                    ["Kho"] = warehouseId.HasValue ? (await _db.Warehouses.FindAsync(warehouseId.Value))?.Name ?? "N/A" : "Tất cả",
                    ["Số nguyên liệu"] = rows.Count,
                    ["Nhanh (>12)"] = fastCount,
                    ["Trung bình (4-12)"] = mediumCount,
                    ["Chậm (<4)"] = slowCount
                },
                Rows = rows,
                Summary = $"Tổng số nguyên liệu: {rows.Count} | Nhanh: {fastCount} | Trung bình: {mediumCount} | Chậm: {slowCount}"
            };
        }
    }
}










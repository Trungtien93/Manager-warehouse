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
    public interface IDemandForecastingService
    {
        Task<DemandForecastResult> ForecastAsync(int materialId, int warehouseId, int monthsAhead = 1);
        Task<List<DemandForecastResult>> ForecastAllMaterialsAsync(int warehouseId, int monthsAhead = 1);
        Task<DemandForecastResult> ForecastWithMethodAsync(int materialId, int warehouseId, string method, int monthsAhead = 1);
    }

    public class DemandForecastResult
    {
        public int MaterialId { get; set; }
        public string MaterialCode { get; set; } = "";
        public string MaterialName { get; set; } = "";
        public int WarehouseId { get; set; }
        public string WarehouseName { get; set; } = "";
        public DateTime ForecastDate { get; set; }
        public decimal ForecastedQuantity { get; set; }
        public decimal ConfidenceLevel { get; set; }
        public string Method { get; set; } = "";
        public decimal? HistoricalAverage { get; set; }
        public decimal? Trend { get; set; }
        public string? Notes { get; set; }
    }

    public class DemandForecastingService : IDemandForecastingService
    {
        private readonly AppDbContext _db;
        private readonly ILogger<DemandForecastingService> _logger;

        public DemandForecastingService(AppDbContext db, ILogger<DemandForecastingService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<DemandForecastResult> ForecastAsync(int materialId, int warehouseId, int monthsAhead = 1)
        {
            // Mặc định dùng Moving Average
            return await ForecastWithMethodAsync(materialId, warehouseId, "MovingAverage", monthsAhead);
        }

        public async Task<DemandForecastResult> ForecastWithMethodAsync(int materialId, int warehouseId, string method, int monthsAhead = 1)
        {
            var material = await _db.Materials.FindAsync(materialId);
            var warehouse = await _db.Warehouses.FindAsync(warehouseId);

            if (material == null || warehouse == null)
            {
                throw new ArgumentException("Material or Warehouse not found");
            }

            // Lấy lịch sử xuất kho (demand) trong 6 tháng gần nhất
            var endDate = DateTime.Now;
            var startDate = endDate.AddMonths(-6);

            var demandHistory = await _db.StockIssueDetails
                .Include(d => d.StockIssue)
                .Include(d => d.Material)
                .Where(d => d.MaterialId == materialId 
                    && d.StockIssue.WarehouseId == warehouseId
                    && d.StockIssue.CreatedAt >= startDate
                    && d.StockIssue.Status == DocumentStatus.DaXuatHang) // Chỉ lấy phiếu đã xuất
                .GroupBy(d => new { 
                    Year = d.StockIssue.CreatedAt.Year,
                    Month = d.StockIssue.CreatedAt.Month 
                })
                .Select(g => new
                {
                    Year = g.Key.Year,
                    Month = g.Key.Month,
                    Quantity = g.Sum(d => (decimal)d.Quantity)
                })
                .OrderBy(x => x.Year)
                .ThenBy(x => x.Month)
                .ToListAsync();

            if (!demandHistory.Any())
            {
                // Không có lịch sử, trả về dự đoán dựa trên tồn kho hiện tại
                var currentStock = await _db.Stocks
                    .Where(s => s.MaterialId == materialId && s.WarehouseId == warehouseId)
                    .Select(s => s.Quantity)
                    .FirstOrDefaultAsync();

                return new DemandForecastResult
                {
                    MaterialId = materialId,
                    MaterialCode = material.Code,
                    MaterialName = material.Name,
                    WarehouseId = warehouseId,
                    WarehouseName = warehouse.Name,
                    ForecastDate = endDate.AddMonths(monthsAhead),
                    ForecastedQuantity = currentStock > 0 ? currentStock / 2 : 0, // Ước tính dựa trên tồn hiện tại
                    ConfidenceLevel = 30, // Thấp vì không có lịch sử
                    Method = method,
                    Notes = "Không có đủ dữ liệu lịch sử để dự đoán chính xác"
                };
            }

            var quantities = demandHistory.Select(x => x.Quantity).ToList();
            decimal forecastedQty;
            decimal confidence;
            decimal? trend = null;
            decimal historicalAvg = quantities.Average();

            switch (method.ToLowerInvariant())
            {
                case "movingaverage":
                    forecastedQty = CalculateMovingAverage(quantities, 3); // 3 tháng gần nhất
                    confidence = CalculateConfidence(quantities, forecastedQty);
                    break;

                case "exponentialsmoothing":
                    forecastedQty = CalculateExponentialSmoothing(quantities, 0.3m);
                    confidence = CalculateConfidence(quantities, forecastedQty);
                    break;

                case "linearregression":
                    var (forecast, trendValue) = CalculateLinearRegression(quantities, monthsAhead);
                    forecastedQty = forecast;
                    trend = trendValue;
                    confidence = CalculateConfidence(quantities, forecastedQty);
                    break;

                default:
                    forecastedQty = historicalAvg;
                    confidence = 50;
                    break;
            }

            // Đảm bảo forecast không âm
            if (forecastedQty < 0) forecastedQty = 0;

            return new DemandForecastResult
            {
                MaterialId = materialId,
                MaterialCode = material.Code,
                MaterialName = material.Name,
                WarehouseId = warehouseId,
                WarehouseName = warehouse.Name,
                ForecastDate = endDate.AddMonths(monthsAhead),
                ForecastedQuantity = forecastedQty,
                ConfidenceLevel = confidence,
                Method = method,
                HistoricalAverage = historicalAvg,
                Trend = trend,
                Notes = $"Dựa trên {demandHistory.Count} tháng dữ liệu lịch sử"
            };
        }

        public async Task<List<DemandForecastResult>> ForecastAllMaterialsAsync(int warehouseId, int monthsAhead = 1)
        {
            var materials = await _db.Stocks
                .Where(s => s.WarehouseId == warehouseId && s.Quantity > 0)
                .Select(s => s.MaterialId)
                .Distinct()
                .ToListAsync();

            var results = new List<DemandForecastResult>();

            foreach (var materialId in materials)
            {
                try
                {
                    var forecast = await ForecastAsync(materialId, warehouseId, monthsAhead);
                    results.Add(forecast);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error forecasting for material {MaterialId}", materialId);
                }
            }

            return results.OrderByDescending(r => r.ForecastedQuantity).ToList();
        }

        private decimal CalculateMovingAverage(List<decimal> values, int period)
        {
            if (values.Count == 0) return 0;
            if (values.Count < period) period = values.Count;

            var recent = values.TakeLast(period).ToList();
            return recent.Average();
        }

        private decimal CalculateExponentialSmoothing(List<decimal> values, decimal alpha)
        {
            if (values.Count == 0) return 0;
            if (values.Count == 1) return values[0];

            decimal forecast = values[0];
            for (int i = 1; i < values.Count; i++)
            {
                forecast = alpha * values[i] + (1 - alpha) * forecast;
            }

            return forecast;
        }

        private (decimal forecast, decimal trend) CalculateLinearRegression(List<decimal> values, int periodsAhead)
        {
            if (values.Count < 2)
            {
                return (values.Any() ? values.Average() : 0, 0);
            }

            int n = values.Count;
            var x = Enumerable.Range(1, n).Select(i => (decimal)i).ToList();
            var y = values;

            decimal sumX = x.Sum();
            decimal sumY = y.Sum();
            decimal sumXY = x.Zip(y, (a, b) => a * b).Sum();
            decimal sumX2 = x.Sum(xi => xi * xi);

            decimal slope = (n * sumXY - sumX * sumY) / (n * sumX2 - sumX * sumX);
            decimal intercept = (sumY - slope * sumX) / n;

            decimal forecast = intercept + slope * (n + periodsAhead);
            return (forecast, slope);
        }

        private decimal CalculateConfidence(List<decimal> values, decimal forecast)
        {
            if (values.Count < 2) return 30;

            var avg = values.Average();
            var variance = values.Select(v => (v - avg) * (v - avg)).Average();
            var stdDev = (decimal)Math.Sqrt((double)variance);

            if (stdDev == 0) return 95; // Dữ liệu ổn định

            var cv = stdDev / avg; // Coefficient of Variation
            var confidence = 100 - Math.Min(70, (decimal)(cv * 50)); // CV càng cao, confidence càng thấp

            return Math.Max(30, Math.Min(95, confidence));
        }
    }
}


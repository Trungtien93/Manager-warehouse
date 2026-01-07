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
    public interface IStockoutPredictionService
    {
        Task<List<StockoutPrediction>> PredictStockoutsAsync(int daysAhead = 14);
        Task<StockoutPrediction?> PredictStockoutAsync(int materialId, int warehouseId, int daysAhead = 14);
    }

    public class StockoutPrediction
    {
        public int MaterialId { get; set; }
        public string MaterialCode { get; set; } = "";
        public string MaterialName { get; set; } = "";
        public int WarehouseId { get; set; }
        public string WarehouseName { get; set; } = "";
        public decimal CurrentStock { get; set; }
        public decimal PredictedDemand { get; set; }
        public DateTime? PredictedStockoutDate { get; set; }
        public int DaysUntilStockout { get; set; }
        public decimal RecommendedOrderQuantity { get; set; }
        public string RiskLevel { get; set; } = "Low"; // Low, Medium, High, Critical
        public string? Notes { get; set; }
    }

    public class StockoutPredictionService : IStockoutPredictionService
    {
        private readonly AppDbContext _db;
        private readonly IDemandForecastingService _forecastingService;
        private readonly IOptimalOrderQuantityService _eoqService;
        private readonly ILogger<StockoutPredictionService> _logger;

        public StockoutPredictionService(
            AppDbContext db,
            IDemandForecastingService forecastingService,
            IOptimalOrderQuantityService eoqService,
            ILogger<StockoutPredictionService> logger)
        {
            _db = db;
            _forecastingService = forecastingService;
            _eoqService = eoqService;
            _logger = logger;
        }

        public async Task<List<StockoutPrediction>> PredictStockoutsAsync(int daysAhead = 14)
        {
            var predictions = new List<StockoutPrediction>();

            // Lấy tất cả stocks có tồn kho > 0
            var stocks = await _db.Stocks
                .Include(s => s.Material)
                .Include(s => s.Warehouse)
                .Where(s => s.Quantity > 0)
                .ToListAsync();

            foreach (var stock in stocks)
            {
                try
                {
                    var prediction = await PredictStockoutAsync(stock.MaterialId, stock.WarehouseId, daysAhead);
                    if (prediction != null && prediction.PredictedStockoutDate.HasValue)
                    {
                        predictions.Add(prediction);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error predicting stockout for material {MaterialId}", stock.MaterialId);
                }
            }

            return predictions
                .OrderBy(p => p.DaysUntilStockout)
                .ThenByDescending(p => p.RiskLevel)
                .ToList();
        }

        public async Task<StockoutPrediction?> PredictStockoutAsync(int materialId, int warehouseId, int daysAhead = 14)
        {
            var stock = await _db.Stocks
                .Include(s => s.Material)
                .Include(s => s.Warehouse)
                .FirstOrDefaultAsync(s => s.MaterialId == materialId && s.WarehouseId == warehouseId);

            if (stock == null || stock.Quantity <= 0)
            {
                return null;
            }

            // Dự đoán nhu cầu trong tháng tới
            var forecast = await _forecastingService.ForecastAsync(materialId, warehouseId, 1);
            var dailyDemand = forecast.ForecastedQuantity / 30; // Ước tính nhu cầu/ngày

            if (dailyDemand <= 0)
            {
                return null; // Không có nhu cầu dự đoán
            }

            // Tính số ngày đến khi hết hàng
            var daysUntilStockout = (int)Math.Floor(stock.Quantity / dailyDemand);

            // Chỉ cảnh báo nếu trong khoảng daysAhead
            if (daysUntilStockout > daysAhead)
            {
                return null;
            }

            var predictedStockoutDate = DateTime.Now.AddDays(daysUntilStockout);

            // Xác định mức độ rủi ro
            string riskLevel;
            if (daysUntilStockout <= 3)
                riskLevel = "Critical";
            else if (daysUntilStockout <= 7)
                riskLevel = "High";
            else if (daysUntilStockout <= 10)
                riskLevel = "Medium";
            else
                riskLevel = "Low";

            // Tính số lượng đặt hàng đề xuất
            var eoq = await _eoqService.CalculateEOQAsync(materialId, warehouseId);
            var recommendedQty = Math.Max(eoq.OptimalOrderQuantity, forecast.ForecastedQuantity);

            return new StockoutPrediction
            {
                MaterialId = materialId,
                MaterialCode = stock.Material.Code,
                MaterialName = stock.Material.Name,
                WarehouseId = warehouseId,
                WarehouseName = stock.Warehouse.Name,
                CurrentStock = stock.Quantity,
                PredictedDemand = forecast.ForecastedQuantity,
                PredictedStockoutDate = predictedStockoutDate,
                DaysUntilStockout = daysUntilStockout,
                RecommendedOrderQuantity = Math.Round(recommendedQty, 2),
                RiskLevel = riskLevel,
                Notes = $"Nhu cầu dự đoán: {forecast.ForecastedQuantity:0.###}/tháng ({dailyDemand:0.###}/ngày)"
            };
        }
    }
}






























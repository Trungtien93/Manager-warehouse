using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MNBEMART.Data;
using MNBEMART.Models;

namespace MNBEMART.Services
{
    public interface IOptimalOrderQuantityService
    {
        Task<EOQResult> CalculateEOQAsync(int materialId, int warehouseId);
        Task<EOQResult> CalculateEOQWithForecastAsync(int materialId, int warehouseId, decimal forecastedDemand);
    }

    public class EOQResult
    {
        public int MaterialId { get; set; }
        public string MaterialCode { get; set; } = "";
        public string MaterialName { get; set; } = "";
        public decimal OptimalOrderQuantity { get; set; }
        public decimal AnnualDemand { get; set; }
        public decimal OrderingCost { get; set; } = 50000; // Chi phí đặt hàng mặc định (VNĐ)
        public decimal HoldingCostPerUnit { get; set; }
        public decimal TotalCost { get; set; }
        public int OrdersPerYear { get; set; }
        public int DaysBetweenOrders { get; set; }
        public string? Notes { get; set; }
    }

    public class OptimalOrderQuantityService : IOptimalOrderQuantityService
    {
        private readonly AppDbContext _db;
        private readonly IDemandForecastingService _forecastingService;
        private readonly ILogger<OptimalOrderQuantityService> _logger;

        public OptimalOrderQuantityService(
            AppDbContext db,
            IDemandForecastingService forecastingService,
            ILogger<OptimalOrderQuantityService> logger)
        {
            _db = db;
            _forecastingService = forecastingService;
            _logger = logger;
        }

        public async Task<EOQResult> CalculateEOQAsync(int materialId, int warehouseId)
        {
            // Lấy dự đoán nhu cầu
            var forecast = await _forecastingService.ForecastAsync(materialId, warehouseId, 1);
            var annualDemand = forecast.ForecastedQuantity * 12; // Ước tính nhu cầu năm

            return await CalculateEOQWithForecastAsync(materialId, warehouseId, annualDemand);
        }

        public async Task<EOQResult> CalculateEOQWithForecastAsync(int materialId, int warehouseId, decimal forecastedDemand)
        {
            var material = await _db.Materials.FindAsync(materialId);
            if (material == null)
            {
                throw new ArgumentException("Material not found");
            }

            // Lấy giá mua (để tính holding cost)
            var purchasePrice = material.PurchasePrice ?? 100000; // Mặc định 100k nếu chưa có
            var holdingCostRate = 0.2m; // 20% giá trị hàng hóa/năm (chi phí lưu kho)
            var holdingCostPerUnit = purchasePrice * holdingCostRate / 12; // Chi phí lưu kho/tháng/đơn vị

            var orderingCost = 50000m; // Chi phí đặt hàng mặc định (VNĐ)

            // Công thức EOQ: sqrt((2 * D * S) / H)
            // D = Annual Demand, S = Ordering Cost, H = Holding Cost per unit
            decimal eoq;
            if (holdingCostPerUnit > 0 && forecastedDemand > 0)
            {
                eoq = (decimal)Math.Sqrt((double)(2 * forecastedDemand * orderingCost / holdingCostPerUnit));
            }
            else
            {
                // Nếu không đủ dữ liệu, dùng ước tính đơn giản
                eoq = forecastedDemand / 12; // Đặt hàng theo tháng
            }

            var ordersPerYear = forecastedDemand > 0 ? (int)Math.Ceiling(forecastedDemand / eoq) : 0;
            var daysBetweenOrders = ordersPerYear > 0 ? 365 / ordersPerYear : 0;

            // Tổng chi phí = (D/Q * S) + (Q/2 * H)
            var totalCost = (forecastedDemand / eoq * orderingCost) + (eoq / 2 * holdingCostPerUnit);

            return new EOQResult
            {
                MaterialId = materialId,
                MaterialCode = material.Code,
                MaterialName = material.Name,
                OptimalOrderQuantity = Math.Round(eoq, 2),
                AnnualDemand = forecastedDemand,
                OrderingCost = orderingCost,
                HoldingCostPerUnit = holdingCostPerUnit,
                TotalCost = Math.Round(totalCost, 2),
                OrdersPerYear = ordersPerYear,
                DaysBetweenOrders = daysBetweenOrders,
                Notes = $"EOQ được tính dựa trên nhu cầu dự đoán: {forecastedDemand:0.###}/năm"
            };
        }
    }
}






























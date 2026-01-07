using Microsoft.EntityFrameworkCore;
using MNBEMART.Data;
using MNBEMART.Models;

namespace MNBEMART.Services
{
    public class CostingService : ICostingService
    {
        private readonly AppDbContext _db;

        public CostingService(AppDbContext db)
        {
            _db = db;
        }

        public async Task<decimal> CalculateIssueCostAsync(int warehouseId, int materialId, decimal quantity, DateTime issueDate)
        {
            var material = await _db.Materials.FindAsync(materialId);
            if (material == null)
                throw new InvalidOperationException($"Material {materialId} not found");

            var costingMethod = material.CostingMethod ?? CostingMethod.WeightedAverage;

            return costingMethod switch
            {
                CostingMethod.FIFO => await CalculateFIFOCostAsync(warehouseId, materialId, quantity, issueDate),
                CostingMethod.WeightedAverage => await CalculateAverageCostAsync(warehouseId, materialId, issueDate),
                _ => await CalculateAverageCostAsync(warehouseId, materialId, issueDate) // Default to average
            };
        }

        public async Task<decimal> CalculateAverageCostAsync(int warehouseId, int materialId, DateTime asOfDate)
        {
            // Lấy tất cả các lô còn tồn tại thời điểm asOfDate
            var lots = await _db.StockLots
                .Where(l => l.WarehouseId == warehouseId
                         && l.MaterialId == materialId
                         && l.Quantity > 0
                         && l.CreatedAt <= asOfDate)
                .OrderBy(l => l.CreatedAt)
                .ToListAsync();

            if (!lots.Any())
            {
                // Nếu không có lô, dùng PurchasePrice của Material
                var material = await _db.Materials.FindAsync(materialId);
                return material?.PurchasePrice ?? 0m;
            }

            decimal totalValue = 0m;
            decimal totalQuantity = 0m;

            foreach (var lot in lots)
            {
                var lotPrice = lot.UnitPrice;
                if (!lotPrice.HasValue)
                {
                    // Nếu lô không có giá, dùng PurchasePrice của Material
                    var material = await _db.Materials.FindAsync(materialId);
                    lotPrice = material?.PurchasePrice ?? 0m;
                }

                totalValue += lot.Quantity * lotPrice.Value;
                totalQuantity += lot.Quantity;
            }

            if (totalQuantity == 0)
                return 0m;

            return Math.Round(totalValue / totalQuantity, 2);
        }

        public async Task<decimal> CalculateFIFOCostAsync(int warehouseId, int materialId, decimal quantity, DateTime issueDate)
        {
            // Lấy các lô theo thứ tự FIFO (CreatedAt tăng dần)
            var lots = await _db.StockLots
                .Where(l => l.WarehouseId == warehouseId
                         && l.MaterialId == materialId
                         && l.Quantity > 0
                         && l.CreatedAt <= issueDate)
                .OrderBy(l => l.CreatedAt)
                .ThenBy(l => l.Id)  // Secondary sort để đảm bảo thứ tự nhất quán
                .ToListAsync();

            if (!lots.Any())
            {
                // Nếu không có lô, dùng PurchasePrice của Material
                var material = await _db.Materials.FindAsync(materialId);
                return material?.PurchasePrice ?? 0m;
            }

            decimal remainingQty = quantity;
            decimal totalCost = 0m;

            foreach (var lot in lots)
            {
                if (remainingQty <= 0)
                    break;

                var lotPrice = lot.UnitPrice;
                if (!lotPrice.HasValue)
                {
                    // Nếu lô không có giá, dùng PurchasePrice của Material
                    var material = await _db.Materials.FindAsync(materialId);
                    lotPrice = material?.PurchasePrice ?? 0m;
                }

                var qtyToTake = Math.Min(remainingQty, lot.Quantity);
                totalCost += qtyToTake * lotPrice.Value;
                remainingQty -= qtyToTake;
            }

            if (quantity == 0)
                return 0m;

            // Trả về giá bình quân của số lượng đã lấy
            return Math.Round(totalCost / quantity, 2);
        }

        public async Task<List<(int lotId, decimal quantity, decimal unitPrice)>> GetLotCostsAsync(int warehouseId, int materialId, DateTime asOfDate)
        {
            var lots = await _db.StockLots
                .Where(l => l.WarehouseId == warehouseId
                         && l.MaterialId == materialId
                         && l.Quantity > 0
                         && l.CreatedAt <= asOfDate)
                .OrderBy(l => l.CreatedAt)
                .ThenBy(l => l.Id)
                .ToListAsync();

            var material = await _db.Materials.FindAsync(materialId);
            var defaultPrice = material?.PurchasePrice ?? 0m;

            return lots.Select(l => (
                l.Id,
                l.Quantity,
                l.UnitPrice ?? defaultPrice
            )).ToList();
        }
    }
}



















































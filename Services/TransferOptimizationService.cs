using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MNBEMART.Data;
using MNBEMART.Models;
using MNBEMART.ViewModels;

namespace MNBEMART.Services
{
    public class TransferOptimizationService : ITransferOptimizationService
    {
        private readonly AppDbContext _db;
        private readonly ILogger<TransferOptimizationService> _logger;

        public TransferOptimizationService(AppDbContext db, ILogger<TransferOptimizationService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<WarehouseSuggestionVM> SuggestBestSourceWarehouse(
            int materialId, 
            decimal requiredQuantity, 
            int targetWarehouseId)
        {
            // 1. Get all warehouses with sufficient stock
            var stocks = await _db.Stocks
                .Include(s => s.Warehouse)
                .Include(s => s.Material)
                .Where(s => s.MaterialId == materialId 
                    && s.Quantity >= requiredQuantity
                    && s.WarehouseId != targetWarehouseId)
                .ToListAsync();

            if (!stocks.Any())
            {
                return new WarehouseSuggestionVM
                {
                    Reasons = new List<string> { "Không tìm thấy kho nào có đủ số lượng yêu cầu" }
                };
            }

            // 2. Calculate score for each warehouse
            var suggestions = new List<WarehouseInfoVM>();
            
            foreach (var stock in stocks)
            {
                var distance = await GetDistance(stock.WarehouseId, targetWarehouseId);
                var cost = await CalculateCostForQuantity(stock.WarehouseId, targetWarehouseId, requiredQuantity, materialId);
                var freshness = await GetStockFreshness(materialId, stock.WarehouseId);
                var score = CalculateScore(stock, targetWarehouseId, requiredQuantity, distance, cost, freshness);

                suggestions.Add(new WarehouseInfoVM
                {
                    Id = stock.WarehouseId,
                    Name = stock.Warehouse?.Name ?? "",
                    Address = stock.Warehouse?.Address ?? "",
                    AvailableStock = stock.Quantity,
                    DistanceKm = distance,
                    EstimatedCost = cost,
                    Score = score,
                    ScoreReason = GenerateScoreReason(stock.Quantity, distance, cost, freshness)
                });
            }

            // 3. Sort by score (highest first)
            suggestions = suggestions.OrderByDescending(s => s.Score).ToList();

            var best = suggestions.First();
            var alternatives = suggestions.Skip(1).Take(3).ToList();

            return new WarehouseSuggestionVM
            {
                BestWarehouse = best,
                Alternatives = alternatives,
                Reasons = new List<string>
                {
                    $"Kho {best.Name} có tồn {best.AvailableStock:N2}",
                    $"Khoảng cách: {best.DistanceKm:N2} km",
                    $"Chi phí ước tính: {best.EstimatedCost:N0} đ",
                    best.ScoreReason
                }
            };
        }

        public async Task<TransferCostVM> CalculateTransferCost(
            int fromWarehouseId, 
            int toWarehouseId, 
            List<TransferItemVM> items)
        {
            var from = await _db.Warehouses.FindAsync(fromWarehouseId);
            var to = await _db.Warehouses.FindAsync(toWarehouseId);

            if (from == null || to == null)
            {
                throw new Exception("Không tìm thấy kho");
            }

            var distance = await GetDistance(fromWarehouseId, toWarehouseId);
            var totalWeight = items.Sum(i => i.Weight * i.Quantity);
            var totalVolume = items.Sum(i => i.Volume * i.Quantity);

            // Cost calculation formula:
            // BaseCost + (Distance * CostPerKm) + (Weight * CostPerKg) + VolumeFactor

            decimal baseCost = from.BaseTransferCost ?? 50000; // Default 50k VND
            decimal costPerKm = from.CostPerKm ?? 5000; // Default 5k/km
            decimal costPerKg = from.CostPerKg ?? 2000; // Default 2k/kg

            var distanceCost = distance * costPerKm;
            var weightCost = totalWeight * costPerKg;
            var volumeCost = totalVolume > 1 ? totalVolume * 10000 : 0; // Volume surcharge: 10k per m³

            var totalCost = baseCost + distanceCost + weightCost + volumeCost;

            return new TransferCostVM
            {
                BaseCost = baseCost,
                DistanceCost = distanceCost,
                WeightCost = weightCost,
                VolumeCost = volumeCost,
                TotalCost = totalCost,
                DistanceKm = distance,
                TotalWeight = totalWeight,
                TotalVolume = totalVolume,
                EstimatedTimeHours = distance > 0 ? distance / 50 : 0 // Assume 50km/h average
            };
        }

        public async Task<decimal> GetDistance(int fromWarehouseId, int toWarehouseId)
        {
            if (fromWarehouseId == toWarehouseId)
                return 0;

            // Check cache first
            var cached = await _db.WarehouseDistances
                .FirstOrDefaultAsync(d => 
                    (d.FromWarehouseId == fromWarehouseId && d.ToWarehouseId == toWarehouseId) ||
                    (d.FromWarehouseId == toWarehouseId && d.ToWarehouseId == fromWarehouseId));

            if (cached != null)
                return cached.DistanceKm;

            // Calculate from GPS
            var from = await _db.Warehouses.FindAsync(fromWarehouseId);
            var to = await _db.Warehouses.FindAsync(toWarehouseId);

            if (from?.Latitude == null || to?.Latitude == null || 
                from.Longitude == null || to.Longitude == null)
            {
                // No GPS data - return 0 (unknown distance)
                return 0;
            }

            var distance = HaversineDistance(
                from.Latitude.Value, from.Longitude.Value,
                to.Latitude.Value, to.Longitude.Value);

            // Cache result (bidirectional)
            try
            {
                await _db.WarehouseDistances.AddAsync(new WarehouseDistance
                {
                    FromWarehouseId = fromWarehouseId,
                    ToWarehouseId = toWarehouseId,
                    DistanceKm = distance,
                    EstimatedTimeHours = distance / 50, // Assume 50km/h
                    BaseCost = from.BaseTransferCost ?? 50000
                });
                await _db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not cache distance");
            }

            return distance;
        }

        public async Task<List<OptimizedRouteVM>> OptimizeTransferRoute(
            int targetWarehouseId, 
            List<MaterialRequirementVM> requirements)
        {
            var routes = new List<OptimizedRouteVM>();

            foreach (var req in requirements)
            {
                var suggestion = await SuggestBestSourceWarehouse(
                    req.MaterialId, 
                    req.RequiredQuantity, 
                    targetWarehouseId);

                if (suggestion.BestWarehouse != null)
                {
                    routes.Add(new OptimizedRouteVM
                    {
                        FromWarehouseId = suggestion.BestWarehouse.Id,
                        FromWarehouseName = suggestion.BestWarehouse.Name,
                        ToWarehouseId = targetWarehouseId,
                        ToWarehouseName = (await _db.Warehouses.FindAsync(targetWarehouseId))?.Name ?? "",
                        Items = new List<MaterialRequirementVM> { req },
                        TotalCost = suggestion.BestWarehouse.EstimatedCost,
                        TotalDistance = suggestion.BestWarehouse.DistanceKm
                    });
                }
            }

            return routes.OrderBy(r => r.TotalCost).ToList();
        }

        // Private helper methods

        private decimal CalculateScore(
            Stock stock, 
            int targetWarehouseId, 
            decimal requiredQty, 
            decimal distance, 
            decimal cost, 
            decimal freshness)
        {
            // Scoring algorithm:
            // - Stock availability: 40% (more stock = better)
            // - Distance: 30% (closer = better)
            // - Cost: 20% (cheaper = better)
            // - Stock freshness: 10% (FEFO - earlier expiry = better)

            // Normalize values (0-1 scale)
            var stockScore = Math.Min(stock.Quantity / (requiredQty * 2), 1); // Cap at 2x required
            var distanceScore = distance > 0 ? 1 / (1 + distance / 100) : 1; // Inverse distance (normalized)
            var costScore = cost > 0 ? 1 / (1 + cost / 1000000) : 1; // Inverse cost (normalized to 1M VND)
            var freshnessScore = freshness; // Already 0-1

            var score = 
                stockScore * 0.4m +
                distanceScore * 0.3m +
                costScore * 0.2m +
                freshnessScore * 0.1m;

            return (decimal)score;
        }

        private async Task<decimal> CalculateCostForQuantity(
            int fromWarehouseId, 
            int toWarehouseId, 
            decimal quantity, 
            int materialId)
        {
            var material = await _db.Materials.FindAsync(materialId);
            if (material == null) return 0;

            var weightPerUnit = material.WeightPerUnit ?? 0;
            var volumePerUnit = material.VolumePerUnit ?? 0;

            var items = new List<TransferItemVM>
            {
                new TransferItemVM
                {
                    MaterialId = materialId,
                    Quantity = quantity,
                    Weight = weightPerUnit,
                    Volume = volumePerUnit
                }
            };

            var cost = await CalculateTransferCost(fromWarehouseId, toWarehouseId, items);
            return cost.TotalCost;
        }

        private async Task<decimal> GetStockFreshness(int materialId, int warehouseId)
        {
            // Check if material has expiry dates in lots
            var lots = await _db.StockLots
                .Where(l => l.MaterialId == materialId && l.WarehouseId == warehouseId && l.Quantity > 0)
                .OrderBy(l => l.ExpiryDate)
                .ToListAsync();

            if (!lots.Any() || !lots.Any(l => l.ExpiryDate.HasValue))
                return 0.5m; // Neutral score if no expiry data

            // FEFO: Earlier expiry = use first (higher freshness score)
            var earliestExpiry = lots.Where(l => l.ExpiryDate.HasValue).Min(l => l.ExpiryDate!.Value);
            var daysUntilExpiry = (earliestExpiry - DateTime.Today).Days;

            // Score: 1.0 if > 90 days, 0.0 if expired, linear in between
            if (daysUntilExpiry < 0) return 0;
            if (daysUntilExpiry > 90) return 1;
            return (decimal)daysUntilExpiry / 90;
        }

        private string GenerateScoreReason(decimal stock, decimal distance, decimal cost, decimal freshness)
        {
            var reasons = new List<string>();
            
            if (stock > 1000) reasons.Add("Tồn kho dồi dào");
            if (distance < 10) reasons.Add("Khoảng cách gần");
            if (cost < 200000) reasons.Add("Chi phí thấp");
            if (freshness > 0.7m) reasons.Add("Hàng còn hạn sử dụng lâu");

            return reasons.Any() ? string.Join(", ", reasons) : "Phù hợp";
        }

        private decimal HaversineDistance(decimal lat1, decimal lon1, decimal lat2, decimal lon2)
        {
            const double R = 6371; // Earth radius in km
            var dLat = ToRadians((double)(lat2 - lat1));
            var dLon = ToRadians((double)(lon2 - lon1));

            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                    Math.Cos(ToRadians((double)lat1)) * Math.Cos(ToRadians((double)lat2)) *
                    Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return (decimal)(R * c);
        }

        private double ToRadians(double degrees)
        {
            return degrees * Math.PI / 180.0;
        }
    }
}


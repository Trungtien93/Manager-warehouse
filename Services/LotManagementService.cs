using Microsoft.EntityFrameworkCore;
using MNBEMART.Data;
using MNBEMART.Models;

namespace MNBEMART.Services
{
    public class LotManagementService : ILotManagementService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<LotManagementService> _logger;

        public LotManagementService(AppDbContext context, ILogger<LotManagementService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<List<StockLot>> SplitLot(int lotId, List<decimal> quantities, int userId, string notes)
        {
            var originalLot = await _context.StockLots
                .Include(l => l.Material)
                .FirstOrDefaultAsync(l => l.Id == lotId);

            if (originalLot == null)
                throw new Exception("Lot not found");

            if (originalLot.IsReserved)
                throw new Exception("Cannot split reserved lot");

            var totalSplit = quantities.Sum();
            if (totalSplit > originalLot.Quantity)
                throw new Exception($"Split quantities ({totalSplit}) exceed lot quantity ({originalLot.Quantity})");

            var newLots = new List<StockLot>();
            var user = await _context.Users.FindAsync(userId);
            var username = user?.Username ?? "System";

            try
            {
                // Create new lots
                foreach (var qty in quantities)
                {
                    var newLot = new StockLot
                    {
                        WarehouseId = originalLot.WarehouseId,
                        MaterialId = originalLot.MaterialId,
                        LotNumber = GenerateChildLotNumber(originalLot.LotNumber),
                        ManufactureDate = originalLot.ManufactureDate,
                        ExpiryDate = originalLot.ExpiryDate,
                        Quantity = qty,
                        ParentLotId = originalLot.LotNumber,
                        CreatedAt = DateTime.Now,
                        LastUpdated = DateTime.Now
                    };

                    await _context.StockLots.AddAsync(newLot);
                    newLots.Add(newLot);
                }

                // Update original lot quantity
                var originalQuantity = originalLot.Quantity;
                originalLot.Quantity -= totalSplit;
                originalLot.LastUpdated = DateTime.Now;

                // Log history for original lot
                var originalHistory = new LotHistory
                {
                    LotId = originalLot.Id,
                    Action = "Split",
                    QuantityBefore = originalQuantity,
                    QuantityAfter = originalLot.Quantity,
                    RelatedLotIds = string.Join(",", newLots.Select(l => l.LotNumber)),
                    PerformedBy = username,
                    PerformedAt = DateTime.Now,
                    Notes = notes ?? $"Split into {quantities.Count} lots"
                };
                await _context.LotHistories.AddAsync(originalHistory);

                await _context.SaveChangesAsync();

                // Log history for new lots (after save to get IDs)
                foreach (var newLot in newLots)
                {
                    var newHistory = new LotHistory
                    {
                        LotId = newLot.Id,
                        Action = "Created from split",
                        QuantityBefore = 0,
                        QuantityAfter = newLot.Quantity,
                        RelatedLotIds = originalLot.LotNumber,
                        PerformedBy = username,
                        PerformedAt = DateTime.Now,
                        Notes = $"Split from {originalLot.LotNumber}"
                    };
                    await _context.LotHistories.AddAsync(newHistory);
                }

                await _context.SaveChangesAsync();

                _logger.LogInformation($"Lot {originalLot.LotNumber} split into {newLots.Count} new lots by {username}");
                return newLots;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error splitting lot {lotId}");
                throw;
            }
        }

        public async Task<StockLot> MergeLots(List<int> lotIds, int userId, string notes)
        {
            var lots = await _context.StockLots
                .Include(l => l.Material)
                .Where(l => lotIds.Contains(l.Id))
                .ToListAsync();

            if (lots.Count < 2)
                throw new Exception("Need at least 2 lots to merge");

            // Validation
            if (lots.Any(l => l.IsReserved))
                throw new Exception("Cannot merge reserved lots");

            var firstLot = lots.First();
            if (!lots.All(l => l.MaterialId == firstLot.MaterialId))
                throw new Exception("All lots must be same material");

            if (!lots.All(l => l.WarehouseId == firstLot.WarehouseId))
                throw new Exception("All lots must be in same warehouse");

            if (!lots.All(l => l.ExpiryDate == firstLot.ExpiryDate))
                _logger.LogWarning("Merging lots with different expiry dates");

            var user = await _context.Users.FindAsync(userId);
            var username = user?.Username ?? "System";

            try
            {
                // Create merged lot
                var mergedLot = new StockLot
                {
                    WarehouseId = firstLot.WarehouseId,
                    MaterialId = firstLot.MaterialId,
                    LotNumber = await GenerateMergedLotNumber(),
                    ManufactureDate = lots.Min(l => l.ManufactureDate),
                    ExpiryDate = lots.Min(l => l.ExpiryDate),  // Use earliest expiry
                    Quantity = lots.Sum(l => l.Quantity),
                    CreatedAt = DateTime.Now,
                    LastUpdated = DateTime.Now
                };

                await _context.StockLots.AddAsync(mergedLot);

                // Mark original lots as merged (set quantity to 0)
                var originalLotNumbers = string.Join(",", lots.Select(l => l.LotNumber));
                foreach (var lot in lots)
                {
                    var originalQty = lot.Quantity;
                    lot.Quantity = 0;
                    lot.LastUpdated = DateTime.Now;

                    // Log history for each original lot
                    var history = new LotHistory
                    {
                        LotId = lot.Id,
                        Action = "Merged",
                        QuantityBefore = originalQty,
                        QuantityAfter = 0,
                        RelatedLotIds = mergedLot.LotNumber,
                        PerformedBy = username,
                        PerformedAt = DateTime.Now,
                        Notes = notes ?? "Merged into new lot"
                    };
                    await _context.LotHistories.AddAsync(history);
                }

                await _context.SaveChangesAsync();

                // Log history for merged lot
                var mergedHistory = new LotHistory
                {
                    LotId = mergedLot.Id,
                    Action = "Created from merge",
                    QuantityBefore = 0,
                    QuantityAfter = mergedLot.Quantity,
                    RelatedLotIds = originalLotNumbers,
                    PerformedBy = username,
                    PerformedAt = DateTime.Now,
                    Notes = $"Merged from {lots.Count} lots: {originalLotNumbers}"
                };
                await _context.LotHistories.AddAsync(mergedHistory);

                await _context.SaveChangesAsync();

                _logger.LogInformation($"Merged {lots.Count} lots into {mergedLot.LotNumber} by {username}");
                return mergedLot;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error merging lots");
                throw;
            }
        }

        public async Task ReserveLot(int lotId, decimal quantity, int issueId, int userId)
        {
            var lot = await _context.StockLots.FindAsync(lotId);
            if (lot == null)
                throw new Exception("Lot not found");

            if (lot.IsReserved)
                throw new Exception("Lot already reserved");

            if (quantity > lot.Quantity)
                throw new Exception("Cannot reserve more than available quantity");

            var user = await _context.Users.FindAsync(userId);
            var username = user?.Username ?? "System";

            lot.IsReserved = true;
            lot.ReservedForIssueId = issueId;
            lot.ReservedDate = DateTime.Now;
            lot.ReservedBy = username;
            lot.LastUpdated = DateTime.Now;

            var history = new LotHistory
            {
                LotId = lotId,
                Action = "Reserved",
                QuantityBefore = lot.Quantity,
                QuantityAfter = lot.Quantity,  // Quantity unchanged
                RelatedLotIds = issueId.ToString(),
                PerformedBy = username,
                PerformedAt = DateTime.Now,
                Notes = $"Reserved {quantity} for issue #{issueId}"
            };

            await _context.LotHistories.AddAsync(history);
            await _context.SaveChangesAsync();

            _logger.LogInformation($"Lot {lot.LotNumber} reserved for issue {issueId} by {username}");
        }

        public async Task ReleaseLot(int lotId, int userId)
        {
            var lot = await _context.StockLots.FindAsync(lotId);
            if (lot == null)
                throw new Exception("Lot not found");

            if (!lot.IsReserved)
                throw new Exception("Lot is not reserved");

            var user = await _context.Users.FindAsync(userId);
            var username = user?.Username ?? "System";
            var previousIssueId = lot.ReservedForIssueId;

            lot.IsReserved = false;
            lot.ReservedForIssueId = null;
            lot.ReservedDate = null;
            lot.ReservedBy = null;
            lot.LastUpdated = DateTime.Now;

            var history = new LotHistory
            {
                LotId = lotId,
                Action = "Released",
                QuantityBefore = lot.Quantity,
                QuantityAfter = lot.Quantity,
                RelatedLotIds = previousIssueId?.ToString(),
                PerformedBy = username,
                PerformedAt = DateTime.Now,
                Notes = $"Reservation released from issue #{previousIssueId}"
            };

            await _context.LotHistories.AddAsync(history);
            await _context.SaveChangesAsync();

            _logger.LogInformation($"Lot {lot.LotNumber} reservation released by {username}");
        }

        public async Task<List<LotHistory>> GetLotHistory(int lotId)
        {
            return await _context.LotHistories
                .Where(h => h.LotId == lotId)
                .OrderByDescending(h => h.PerformedAt)
                .ToListAsync();
        }

        public async Task<bool> CanSplitLot(int lotId)
        {
            var lot = await _context.StockLots.FindAsync(lotId);
            if (lot == null) return false;
            if (lot.IsReserved) return false;
            if (lot.Quantity <= 1) return false;
            return true;
        }

        public async Task<bool> CanMergeLots(List<int> lotIds)
        {
            if (lotIds.Count < 2) return false;

            var lots = await _context.StockLots
                .Where(l => lotIds.Contains(l.Id))
                .ToListAsync();

            if (lots.Count != lotIds.Count) return false;
            if (lots.Any(l => l.IsReserved)) return false;
            if (lots.Any(l => l.Quantity == 0)) return false;

            var firstLot = lots.First();
            if (!lots.All(l => l.MaterialId == firstLot.MaterialId)) return false;
            if (!lots.All(l => l.WarehouseId == firstLot.WarehouseId)) return false;

            return true;
        }

        private string GenerateChildLotNumber(string? parentLotNumber)
        {
            var prefix = parentLotNumber ?? "LOT";
            var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
            var random = new Random().Next(100, 999);
            return $"{prefix}-{timestamp}-{random}";
        }

        private async Task<string> GenerateMergedLotNumber()
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd");
            var count = await _context.StockLots.CountAsync(l => l.LotNumber!.StartsWith($"MERGED-{timestamp}"));
            return $"MERGED-{timestamp}-{(count + 1):D3}";
        }
    }
}













































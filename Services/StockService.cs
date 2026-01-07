using Microsoft.EntityFrameworkCore;
using MNBEMART.Data;
using MNBEMART.Models;

namespace MNBEMART.Services
{
    public class StockService : IStockService
    {
        private readonly AppDbContext _db;
        public StockService(AppDbContext db) => _db = db;

        // === CORE (không theo lô) ===
        public async Task IncreaseAsync(int warehouseId, int materialId, decimal qty)
        {
            var stock = await _db.Stocks
                .FirstOrDefaultAsync(s => s.WarehouseId == warehouseId && s.MaterialId == materialId);

            if (stock == null)
            {
                stock = new Stock
                {
                    WarehouseId = warehouseId,
                    MaterialId = materialId,
                    Quantity = 0,
                    LastUpdated = DateTime.UtcNow
                };
                _db.Stocks.Add(stock);
            }

            stock.Quantity += qty;
            stock.LastUpdated = DateTime.UtcNow;
        }

        public async Task DecreaseAsync(int warehouseId, int materialId, decimal qty)
        {
            var stock = await _db.Stocks
                .FirstOrDefaultAsync(s => s.WarehouseId == warehouseId && s.MaterialId == materialId);

            if (stock == null || stock.Quantity < qty)
                throw new InvalidOperationException("Tồn kho không đủ để xuất.");

            stock.Quantity -= qty;
            stock.LastUpdated = DateTime.UtcNow;
        }

        public async Task ApplyTransferAsync(StockTransfer t)
        {
            foreach (var d in t.Details)
            {
                await DecreaseAsync(t.FromWarehouseId, d.MaterialId, d.Quantity);
                await IncreaseAsync(t.ToWarehouseId,   d.MaterialId, d.Quantity);
                // TODO: nếu có quản lý lô cho chuyển kho → giảm từ các lô FEFO kho đi và tăng lô tương ứng kho đến
            }
        }

        // StockAdjustment đã bị vô hiệu hóa - chức năng không còn sử dụng
        // public async Task ApplyAdjustmentAsync(StockAdjustment a)
        // {
        //     foreach (var d in a.Details)
        //     {
        //         if (d.QuantityDiff >= 0)
        //             await IncreaseAsync(a.WarehouseId, d.MaterialId, d.QuantityDiff);
        //         else
        //             await DecreaseAsync(a.WarehouseId, d.MaterialId, Math.Abs(d.QuantityDiff));
        //     }
        // }

        // Lưu thay đổi vào cơ sở dữ liệu
        public Task SaveAsync() => _db.SaveChangesAsync();

        public async Task ApplyReceiptAsync(StockReceipt r)
        {
            foreach (var d in r.Details)
            {
                await IncreaseAsync(r.WarehouseId, d.MaterialId, (decimal)d.Quantity);
                // Tăng theo lô (nếu có thông tin lô) - lưu giá nhập
                var unitPrice = d.UnitPrice > 0 ? d.UnitPrice : (await _db.Materials.FindAsync(d.MaterialId))?.PurchasePrice;
                await IncreaseLotAsync(r.WarehouseId, d.MaterialId, (decimal)d.Quantity, d.LotNumber, d.ManufactureDate, d.ExpiryDate, unitPrice);
            }
        }

        public async Task RevertReceiptAsync(StockReceipt r)
        {
            foreach (var d in r.Details)
            {
                await DecreaseAsync(r.WarehouseId, d.MaterialId, (decimal)d.Quantity);
                await DecreaseLotAsync(r.WarehouseId, d.MaterialId, (decimal)d.Quantity, d.LotNumber, d.ManufactureDate, d.ExpiryDate);
            }
        }

        // === LOT helpers ===
        private static bool LotKeyEquals(StockLot lot, int warehouseId, int materialId, string? lotNo, DateTime? mfg, DateTime? exp)
        {
            return lot.WarehouseId == warehouseId && lot.MaterialId == materialId
                && ((lot.LotNumber == lotNo) || (lot.LotNumber == null && lotNo == null))
                && ((lot.ManufactureDate == mfg) || (lot.ManufactureDate == null && mfg == null))
                && ((lot.ExpiryDate == exp) || (lot.ExpiryDate == null && exp == null));
        }

        public async Task IncreaseLotAsync(int warehouseId, int materialId, decimal qty, string? lotNo, DateTime? mfg, DateTime? exp)
        {
            await IncreaseLotAsync(warehouseId, materialId, qty, lotNo, mfg, exp, null);
        }

        public async Task IncreaseLotAsync(int warehouseId, int materialId, decimal qty, string? lotNo, DateTime? mfg, DateTime? exp, decimal? unitPrice)
        {
            // Tìm theo key; do null equality khó, lấy candidates và lọc trong bộ nhớ
            var candidates = await _db.StockLots
                .Where(x => x.WarehouseId == warehouseId && x.MaterialId == materialId)
                .ToListAsync();
            var lot = candidates.FirstOrDefault(x => LotKeyEquals(x, warehouseId, materialId, lotNo, mfg, exp));
            if (lot == null)
            {
                lot = new StockLot
                {
                    WarehouseId = warehouseId,
                    MaterialId = materialId,
                    LotNumber = lotNo,
                    ManufactureDate = mfg,
                    ExpiryDate = exp,
                    Quantity = 0,
                    UnitPrice = unitPrice,  // Lưu giá nhập vào lô
                    CreatedAt = DateTime.Now,
                    LastUpdated = DateTime.Now
                };
                _db.StockLots.Add(lot);
            }
            else
            {
                // Nếu lô đã tồn tại và có giá mới, cập nhật giá bình quân gia quyền
                if (unitPrice.HasValue && lot.UnitPrice.HasValue)
                {
                    // Tính giá bình quân gia quyền: (giá cũ * số lượng cũ + giá mới * số lượng mới) / tổng số lượng
                    var totalValue = (lot.Quantity * lot.UnitPrice.Value) + (qty * unitPrice.Value);
                    var totalQty = lot.Quantity + qty;
                    if (totalQty > 0)
                    {
                        lot.UnitPrice = Math.Round(totalValue / totalQty, 2);
                    }
                }
                else if (unitPrice.HasValue && !lot.UnitPrice.HasValue)
                {
                    // Nếu lô chưa có giá, gán giá mới
                    lot.UnitPrice = unitPrice;
                }
            }
            lot.Quantity += qty;
            lot.LastUpdated = DateTime.Now;
        }

        public async Task DecreaseLotAsync(int warehouseId, int materialId, decimal qty, string? lotNo, DateTime? mfg, DateTime? exp)
        {
            var candidates = await _db.StockLots
                .Where(x => x.WarehouseId == warehouseId && x.MaterialId == materialId)
                .ToListAsync();
            var lot = candidates.FirstOrDefault(x => LotKeyEquals(x, warehouseId, materialId, lotNo, mfg, exp));
            if (lot == null || lot.Quantity < qty)
                throw new InvalidOperationException("Tồn theo lô không đủ.");
            lot.Quantity -= qty;
            lot.LastUpdated = DateTime.Now;
        }

        // FEFO: phân bổ từ các lô sắp hết hạn → trả danh sách (lotId, qty)
        public async Task<List<(int lotId, decimal qty)>> AllocateFromLotsFefoAsync(int warehouseId, int materialId, decimal qty)
        {
            var lots = await _db.StockLots.AsQueryable()
                .Where(x => x.WarehouseId == warehouseId && x.MaterialId == materialId && x.Quantity > 0)
                .OrderBy(x => x.ExpiryDate.HasValue ? 0 : 1) // có HSD trước, null cuối
                .ThenBy(x => x.ExpiryDate)
                .ThenBy(x => x.ManufactureDate.HasValue ? 0 : 1)
                .ThenBy(x => x.ManufactureDate)
                .ThenBy(x => x.CreatedAt)
                .ToListAsync();

            var remain = qty;
            var result = new List<(int lotId, decimal qty)>();
            foreach (var lot in lots)
            {
                if (remain <= 0) break;
                var take = Math.Min(lot.Quantity, remain);
                lot.Quantity -= take;
                lot.LastUpdated = DateTime.Now;
                result.Add((lot.Id, take));
                remain -= take;
            }
            if (remain > 0)
                throw new InvalidOperationException("Không đủ tồn theo lô để xuất.");
            return result;
        }
    }
}

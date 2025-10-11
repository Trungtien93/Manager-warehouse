using Microsoft.EntityFrameworkCore;
using MNBEMART.Data;
using MNBEMART.Models;

namespace MNBEMART.Services
{
    public class StockService : IStockService
    {
        private readonly AppDbContext _db;
        public StockService(AppDbContext db) => _db = db;

       
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
            }
        }

        public async Task ApplyAdjustmentAsync(StockAdjustment a)
        {
            foreach (var d in a.Details)
            {
                if (d.QuantityDiff >= 0)
                    await IncreaseAsync(a.WarehouseId, d.MaterialId, d.QuantityDiff);
                else
                    await DecreaseAsync(a.WarehouseId, d.MaterialId, Math.Abs(d.QuantityDiff));
            }
        }

        // Lưu thay đổi vào cơ sở dữ liệu
        public Task SaveAsync() => _db.SaveChangesAsync();

        public Task ApplyReceiptAsync(DocumentStatus r)
        {
            throw new NotImplementedException();
        }

        public Task RevertReceiptAsync(DocumentStatus r)
        {
            throw new NotImplementedException();
        }
    }
}

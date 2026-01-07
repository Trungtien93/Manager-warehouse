using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MNBEMART.Models;

namespace MNBEMART.Services
{
    public interface IStockService
    {
        Task IncreaseAsync(int warehouseId, int materialId, decimal qty);
        Task DecreaseAsync(int warehouseId, int materialId, decimal qty);

        // LOT APIs
        Task IncreaseLotAsync(int warehouseId, int materialId, decimal qty, string? lotNo, DateTime? mfg, DateTime? exp);
        Task IncreaseLotAsync(int warehouseId, int materialId, decimal qty, string? lotNo, DateTime? mfg, DateTime? exp, decimal? unitPrice);
        Task DecreaseLotAsync(int warehouseId, int materialId, decimal qty, string? lotNo, DateTime? mfg, DateTime? exp);
        Task<List<(int lotId, decimal qty)>> AllocateFromLotsFefoAsync(int warehouseId, int materialId, decimal qty);

        Task ApplyReceiptAsync(StockReceipt receipt);      // cộng tồn theo chi tiết
        Task RevertReceiptAsync(StockReceipt receipt);     // trừ tồn theo chi tiết (đảo ngược)
        Task ApplyTransferAsync(StockTransfer transfer);
        // StockAdjustment đã bị vô hiệu hóa - chức năng không còn sử dụng
        // Task ApplyAdjustmentAsync(StockAdjustment adjustment);
        Task SaveAsync(); 
    }
}

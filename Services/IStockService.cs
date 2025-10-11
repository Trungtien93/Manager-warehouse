using System.Threading.Tasks;
using MNBEMART.Models;

namespace MNBEMART.Services
{
    public interface IStockService
    {
        Task IncreaseAsync(int warehouseId, int materialId, decimal qty);
        Task DecreaseAsync(int warehouseId, int materialId, decimal qty);
        Task ApplyReceiptAsync(DocumentStatus r);      // cộng tồn theo chi tiết
        Task RevertReceiptAsync(DocumentStatus r);     // trừ tồn theo chi tiết (đảo ngược)
        Task ApplyTransferAsync(StockTransfer transfer);
        Task ApplyAdjustmentAsync(StockAdjustment adjustment);
        Task SaveAsync(); 
    }
}

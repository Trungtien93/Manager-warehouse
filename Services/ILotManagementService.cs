using MNBEMART.Models;

namespace MNBEMART.Services
{
    public interface ILotManagementService
    {
        Task<List<StockLot>> SplitLot(int lotId, List<decimal> quantities, int userId, string notes);
        Task<StockLot> MergeLots(List<int> lotIds, int userId, string notes);
        Task ReserveLot(int lotId, decimal quantity, int issueId, int userId);
        Task ReleaseLot(int lotId, int userId);
        Task<List<LotHistory>> GetLotHistory(int lotId);
        Task<bool> CanSplitLot(int lotId);
        Task<bool> CanMergeLots(List<int> lotIds);
    }
}























































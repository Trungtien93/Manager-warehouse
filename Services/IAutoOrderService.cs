namespace MNBEMART.Services
{
    public interface IAutoOrderService
    {
        Task<List<int>> CheckLowStockAndCreateRequests();
        Task<int> GetLowStockCount();
    }
}




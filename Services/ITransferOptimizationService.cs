using MNBEMART.ViewModels;

namespace MNBEMART.Services
{
    public interface ITransferOptimizationService
    {
        // Suggest best source warehouse for a material
        Task<WarehouseSuggestionVM> SuggestBestSourceWarehouse(
            int materialId, 
            decimal requiredQuantity, 
            int targetWarehouseId);
        
        // Calculate transfer cost
        Task<TransferCostVM> CalculateTransferCost(
            int fromWarehouseId, 
            int toWarehouseId, 
            List<TransferItemVM> items);
        
        // Get distance between warehouses
        Task<decimal> GetDistance(int fromWarehouseId, int toWarehouseId);
        
        // Optimize transfer route (if multiple warehouses)
        Task<List<OptimizedRouteVM>> OptimizeTransferRoute(
            int targetWarehouseId, 
            List<MaterialRequirementVM> requirements);
    }
}






















































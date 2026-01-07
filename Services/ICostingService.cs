namespace MNBEMART.Services
{
    /// <summary>
    /// Service để tính giá vốn hàng bán (COGS) theo các phương pháp FIFO và Bình quân gia quyền
    /// </summary>
    public interface ICostingService
    {
        /// <summary>
        /// Tính giá vốn khi xuất kho dựa trên CostingMethod của Material
        /// </summary>
        Task<decimal> CalculateIssueCostAsync(int warehouseId, int materialId, decimal quantity, DateTime issueDate);

        /// <summary>
        /// Tính giá bình quân gia quyền tại thời điểm cụ thể
        /// </summary>
        Task<decimal> CalculateAverageCostAsync(int warehouseId, int materialId, DateTime asOfDate);

        /// <summary>
        /// Tính giá FIFO cho số lượng xuất kho
        /// </summary>
        Task<decimal> CalculateFIFOCostAsync(int warehouseId, int materialId, decimal quantity, DateTime issueDate);

        /// <summary>
        /// Lấy danh sách lô với giá để tính FIFO (theo thứ tự CreatedAt)
        /// </summary>
        Task<List<(int lotId, decimal quantity, decimal unitPrice)>> GetLotCostsAsync(int warehouseId, int materialId, DateTime asOfDate);
    }
}



















































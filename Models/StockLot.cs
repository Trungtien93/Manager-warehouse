using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace MNBEMART.Models
{
    // Tồn theo lô cho từng (Kho, Vật tư)
    public class StockLot
    {
        public int Id { get; set; }
        [Required] public int WarehouseId { get; set; }
        [Required] public int MaterialId { get; set; }
        public string? LotNumber { get; set; }
        public DateTime? ManufactureDate { get; set; }
        public DateTime? ExpiryDate { get; set; }
        [Precision(18,3)] public decimal Quantity { get; set; }
        [Precision(18,2)] public decimal? UnitPrice { get; set; }  // Giá nhập của lô (để tính FIFO)
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime LastUpdated { get; set; } = DateTime.Now;

        // Advanced Lot Management
        public string? ParentLotId { get; set; }  // For tracking split lots
        public bool IsReserved { get; set; } = false;
        public int? ReservedForIssueId { get; set; }  // FK to StockIssue
        public DateTime? ReservedDate { get; set; }
        public string? ReservedBy { get; set; }

        public Warehouse? Warehouse { get; set; }
        public Material? Material { get; set; }
    }

    // Phân bổ xuất kho theo lô cho từng dòng chi tiết
    public class StockIssueAllocation
    {
        public int Id { get; set; }
        [Required] public int StockIssueDetailId { get; set; }
        [Required] public int StockLotId { get; set; }
        [Precision(18,3)] public decimal Quantity { get; set; }

        public StockIssueDetail? StockIssueDetail { get; set; }
        public StockLot? StockLot { get; set; }
    }
}

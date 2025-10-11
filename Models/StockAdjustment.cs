namespace MNBEMART.Models
{
    public class StockAdjustment
    {
        public int Id { get; set; }
        public string AdjustNumber { get; set; } = "";
        public int WarehouseId { get; set; }
        public DateTime CreatedAt { get; set; }
        public int CreatedById { get; set; }
        public DocumentStatus Status { get; set; } = DocumentStatus.Moi;
        public int? ApprovedById { get; set; }
        public DateTime? ApprovedAt { get; set; }
        public string? Reason { get; set; }

        public Warehouse Warehouse { get; set; } = null!;
        public User CreatedBy { get; set; } = null!;
        public User? ApprovedBy { get; set; }

        public ICollection<StockAdjustmentDetail> Details { get; set; } = new List<StockAdjustmentDetail>();
    }

    public class StockAdjustmentDetail
    {
        public int Id { get; set; }
        public int StockAdjustmentId { get; set; }
        public int MaterialId { get; set; }
        public decimal QuantityDiff { get; set; }       // + tăng / - giảm
        public string? Note { get; set; }

        public StockAdjustment StockAdjustment { get; set; } = null!;
        public Material Material { get; set; } = null!;
    }
}

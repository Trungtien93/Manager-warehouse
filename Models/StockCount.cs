// Models/StockCount.cs
namespace MNBEMART.Models
{
    public class StockCount
    {
        public int Id { get; set; }
        public string CountNumber { get; set; } = "";
        public int WarehouseId { get; set; }
        public DateTime CountDate { get; set; }
        public string? Note { get; set; }
        public DocumentStatus Status { get; set; } = DocumentStatus.Moi;

        public Warehouse Warehouse { get; set; } = null!;
        public ICollection<StockCountLine> Lines { get; set; } = new List<StockCountLine>();
    }

    public class StockCountLine
    {
        public int Id { get; set; }
        public int StockCountId { get; set; }
        public int MaterialId { get; set; }
        public decimal CountedQty { get; set; }
        public decimal? SystemQty { get; set; } // tồn hệ thống tại thời điểm kiểm kê (snapshot)
        public decimal DiffQty => CountedQty - (SystemQty ?? 0);

        public StockCount StockCount { get; set; } = null!;
        public Material Material { get; set; } = null!;
    }
}

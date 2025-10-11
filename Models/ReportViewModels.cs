namespace MNBEMART.ViewModels;

public class ReportFilterVM
{
    public int? WarehouseId { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    // day | month | year
    public string GroupBy { get; set; } = "day";
}

public class RevenueRowVM
{
    public string Period { get; set; } = ""; // ví dụ: 2025-08-01 hoặc 2025-08
    public decimal TotalRevenue { get; set; } // tổng tiền xuất kho (đã duyệt)
}

public class MovementRowVM
{
    public int MaterialId { get; set; }
    public string MaterialName { get; set; } = "";
    public decimal QtyIn { get; set; }
    public decimal QtyOut { get; set; }
}

public class InventoryRowVM
{
    public int MaterialId { get; set; }
    public string MaterialName { get; set; } = "";
    public decimal BeginQty { get; set; }
    public decimal InQty { get; set; }
    public decimal OutQty { get; set; }
    public decimal EndQty => BeginQty + InQty - OutQty;
}

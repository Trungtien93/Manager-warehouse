using MNBEMART.Models;

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
    public decimal QtyOut { get; set; } // tổng số lượng xuất kho trong kỳ
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
    
    // Lot information
    public int? LotId { get; set; }
    public string? LotNumber { get; set; }
    public DateTime? ManufactureDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public string? WarehouseName { get; set; }
}

public class InventoryValueRowVM
{
    public int MaterialId { get; set; }
    public string MaterialName { get; set; } = "";
    public string MaterialCode { get; set; } = "";
    public string Unit { get; set; } = "";
    public decimal BeginQty { get; set; }
    public decimal BeginValue { get; set; }
    public decimal InQty { get; set; }
    public decimal InValue { get; set; }
    public decimal OutQty { get; set; }
    public decimal OutValue { get; set; }
    public decimal EndQty { get; set; }
    public decimal EndValue { get; set; }
    public decimal AvgCost => EndQty > 0 ? Math.Round(EndValue / EndQty, 2) : 0m;
    public string? WarehouseName { get; set; }
}

public class COGSRowVM
{
    public int MaterialId { get; set; }
    public string MaterialName { get; set; } = "";
    public string MaterialCode { get; set; } = "";
    public string Unit { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal CostPrice { get; set; }
    public decimal TotalCOGS => Math.Round(Quantity * CostPrice, 2);
    public DateTime IssueDate { get; set; }
    public string IssueNumber { get; set; } = "";
    public string? WarehouseName { get; set; }
}

public class InventoryValuationRowVM
{
    public int MaterialId { get; set; }
    public string MaterialName { get; set; } = "";
    public string MaterialCode { get; set; } = "";
    public string Unit { get; set; } = "";
    public string? WarehouseName { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal TotalValue => Math.Round(Quantity * UnitCost, 2);
    public CostingMethod CostingMethod { get; set; }
    public string CostingMethodName => CostingMethod == CostingMethod.FIFO ? "FIFO" : "Bình quân";
}

public class ProfitLossReportVM
{
    public string Period { get; set; } = ""; // Ngày/Tháng/Năm
    public decimal Revenue { get; set; } // Doanh thu từ xuất kho
    public decimal COGS { get; set; } // Giá vốn hàng bán
    public decimal GrossProfit => Revenue - COGS; // Lợi nhuận gộp
    public decimal OperatingExpenses { get; set; } = 0; // Chi phí hoạt động (tùy chọn)
    public decimal NetProfit => GrossProfit - OperatingExpenses; // Lợi nhuận ròng
    public decimal GrossProfitMargin => Revenue > 0 ? Math.Round((GrossProfit / Revenue) * 100, 2) : 0; // Tỷ suất lợi nhuận gộp (%)
    public decimal NetProfitMargin => Revenue > 0 ? Math.Round((NetProfit / Revenue) * 100, 2) : 0; // Tỷ suất lợi nhuận ròng (%)
}

public class TurnoverRateVM
{
    public int MaterialId { get; set; }
    public string MaterialCode { get; set; } = "";
    public string MaterialName { get; set; } = "";
    public string Unit { get; set; } = "";
    public string? WarehouseName { get; set; }
    public decimal AverageStock { get; set; } // Tồn trung bình trong kỳ
    public decimal TotalIssued { get; set; } // Tổng số lượng xuất trong kỳ
    public decimal TurnoverRate => AverageStock > 0 ? Math.Round(TotalIssued / AverageStock, 2) : 0; // Số lần quay vòng
    public decimal DaysToTurnover { get; set; } // Số ngày quay vòng
    public string Category => TurnoverRate > 12 ? "Fast" : TurnoverRate >= 4 ? "Medium" : "Slow"; // Fast (>12), Medium (4-12), Slow (<4)
}
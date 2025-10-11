using Microsoft.AspNetCore.Mvc.Rendering;
using MNBEMART.Models;

// public class MaterialIndexVM
// {
//     public IEnumerable<Material> Items { get; set; } = Enumerable.Empty<Material>();

//     public string? Q { get; set; }                  // từ khoá
//     public int? WarehouseId { get; set; }           // lọc kho
//     public SelectList? WarehouseOptions { get; set; }

//     public int Page { get; set; } = 1;
//     public int PageSize { get; set; } = 30;
//     public int TotalItems { get; set; }
//     public int TotalPages => (int)Math.Ceiling((double)TotalItems / Math.Max(1, PageSize));

//     public int TotalStockQty { get; set; }          // tổng SL tồn (theo filter)
//     public decimal TotalStockValue { get; set; }    // tổng giá trị tồn = SL * PurchasePrice
// }
public class WarehouseQtyVM
{
    public int WarehouseId { get; set; }
    public string WarehouseName { get; set; } = "";
    public decimal Qty { get; set; }
}

public class MaterialRowVM
{
    public Material M { get; set; } = default!;
    public decimal TotalQty { get; set; }
    public List<WarehouseQtyVM> WhereStock { get; set; } = new();
}

// Cập nhật MaterialIndexVM: đổi Items thành List<MaterialRowVM>
public class MaterialIndexVM
{
    public List<MaterialRowVM> Items { get; set; } = new();
    public string? Q { get; set; }
    public int? WarehouseId { get; set; }
    public SelectList? WarehouseOptions { get; set; }

    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalItems { get; set; }

    public int TotalPages => (int)Math.Ceiling((double)TotalItems / Math.Max(1, PageSize));

    public decimal TotalStockQty { get; set; }
    public decimal TotalStockValue { get; set; }
}

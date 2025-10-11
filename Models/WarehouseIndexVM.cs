using System.Collections.Generic;

namespace MNBEMART.Models
{
    public class WarehouseIndexVM
    {
        public IEnumerable<WarehouseRowVM> Items { get; set; } = new List<WarehouseRowVM>();
        public string? Q { get; set; }

        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 30;
        public int TotalItems { get; set; }
        public int TotalPages => (int)System.Math.Ceiling((double)System.Math.Max(1, TotalItems) / PageSize);
    }

    public class WarehouseRowVM
    {
        public Warehouse W { get; set; } = new Warehouse();
        public int DistinctMaterials { get; set; }       // số nguyên liệu có tồn (Stocks) trong kho này
        public decimal TotalQty { get; set; }            // tổng SL tồn (Stocks) trong kho này
    }
}

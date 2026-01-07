namespace MNBEMART.Models
{
    using System.Collections.Generic;

    // Material class representing a material entity in the application     
    public class Material
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public string Name { get; set; }
        public string Unit { get; set; }
        public string? Description { get; set; }
        public string? ImageUrl { get; set; }
        public int? SupplierId { get; set; }
        public Supplier? Supplier { get; set; }
        public int? WarehouseId { get; set; } // kho hàng
        public Warehouse? Warehouse { get; set; } // kho hàng

        public string? Specification { get; set; }     // Quy cách / quy chuẩn
        public decimal? PurchasePrice { get; set; }    // Giá nhập mặc định
        public decimal? SellingPrice { get; set; }    // Giá bán mặc định
        public int? StockQuantity { get; set; } = 0;     // Số lượng tồn kho hiện tại
        
        public DateTime? ManufactureDate { get; set; }  // Ngày sản xuất
        public DateTime? ExpiryDate { get; set; }       // Hạn sử dụng

        // Auto-ordering fields
        public decimal? MinimumStock { get; set; }      // Mức tồn tối thiểu
        public decimal? MaximumStock { get; set; }      // Mức tồn tối đa
        public decimal? ReorderQuantity { get; set; }   // Số lượng đặt lại
        public int? PreferredSupplierId { get; set; }   // Nhà cung cấp ưu tiên

        // Transfer Cost Calculation Fields
        public decimal? WeightPerUnit { get; set; }  // kg per unit (for cost calculation)
        public decimal? VolumePerUnit { get; set; }  // m³ per unit (for cost calculation)

        // Warehouse Accounting Fields
        public CostingMethod? CostingMethod { get; set; }  // Phương pháp tính giá xuất kho (mặc định WeightedAverage)
    }
}

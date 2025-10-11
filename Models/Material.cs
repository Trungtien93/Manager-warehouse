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
       
}
}

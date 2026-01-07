// Models/StockTransfer.cs
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace MNBEMART.Models
{
    public class StockTransfer
    {
        public int Id { get; set; }
        public string TransferNumber { get; set; } = "";
        public int FromWarehouseId { get; set; }
        public int ToWarehouseId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public int CreatedById { get; set; }
        public DocumentStatus Status { get; set; } = DocumentStatus.Moi;
        public int? ApprovedById { get; set; }
        public DateTime? ApprovedAt { get; set; }
        public string? Note { get; set; }

        [BindNever, ValidateNever] public Warehouse FromWarehouse { get; set; } = null!;
        [BindNever, ValidateNever] public Warehouse ToWarehouse   { get; set; } = null!;
        [BindNever, ValidateNever] public User      CreatedBy     { get; set; } = null!;
        [BindNever, ValidateNever] public User?     ApprovedBy    { get; set; }

        [ValidateNever] public ICollection<StockTransferDetail> Details { get; set; } = new List<StockTransferDetail>();
    }

    public class StockTransferDetail
    {
        public int Id { get; set; }
        public int StockTransferId { get; set; }
        public int MaterialId { get; set; }
        public decimal Quantity { get; set; }
        public string? Unit { get; set; }
        public string? Note { get; set; }
        // Đơn giá tuỳ chọn để thống kê giá trị xuất/nhập trong StockBalance
        public decimal? UnitPrice { get; set; }
        
        // Thông tin lô hàng
        public int? LotId { get; set; }
        public DateTime? ManufactureDate { get; set; }
        public DateTime? ExpiryDate { get; set; }

        [BindNever, ValidateNever] public StockTransfer? StockTransfer { get; set; }
        [BindNever, ValidateNever] public Material?      Material      { get; set; }
        [BindNever, ValidateNever] public StockLot?      Lot           { get; set; }
    }
}

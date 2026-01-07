// ViewModels/StockTransferVM.cs
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace MNBEMART.ViewModels
{
    public class StockTransferDetailVM
    {
        [Required(ErrorMessage = "Vui lòng chọn vật tư")] public int MaterialId { get; set; }
        [Range(1, int.MaxValue, ErrorMessage = "Số lượng phải ≥ 1")]
        public int Quantity { get; set; }
        [Required(ErrorMessage = "Vui lòng nhập đơn vị tính")]
        public string? Unit { get; set; }
        public string? Note { get; set; }
        
        // Thông tin lô hàng
        public int? LotId { get; set; }
        public DateTime? ManufactureDate { get; set; }
        public DateTime? ExpiryDate { get; set; }
    }

    public class StockTransferVM
    {
        [Required(ErrorMessage = "Vui lòng chọn kho đi")] public int FromWarehouseId { get; set; }
        [Required(ErrorMessage = "Vui lòng chọn kho đến")] public int ToWarehouseId { get; set; }
        public string? Note { get; set; }

        [MinLength(1, ErrorMessage = "Cần ít nhất 1 dòng vật tư.")]
        public List<StockTransferDetailVM> Details { get; set; } = new();

        public IEnumerable<SelectListItem> WarehouseOptions { get; set; } = Enumerable.Empty<SelectListItem>();
        public IEnumerable<SelectListItem> MaterialOptions  { get; set; } = Enumerable.Empty<SelectListItem>();
    }
}

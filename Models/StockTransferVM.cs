// ViewModels/StockTransferVM.cs
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace MNBEMART.ViewModels
{
    public class StockTransferDetailVM
    {
        [Required] public int MaterialId { get; set; }
        [Range(0, int.MaxValue, ErrorMessage = "Số lượng phải ≥ 1")]
        public int Quantity { get; set; }
        public string? Unit { get; set; }
        public string? Note { get; set; }
    }

    public class StockTransferVM
    {
        [Required] public int FromWarehouseId { get; set; }
        [Required] public int ToWarehouseId { get; set; }
        public string? Note { get; set; }

        [MinLength(1, ErrorMessage = "Cần ít nhất 1 dòng vật tư.")]
        public List<StockTransferDetailVM> Details { get; set; } = new();

        public IEnumerable<SelectListItem> WarehouseOptions { get; set; } = Enumerable.Empty<SelectListItem>();
        public IEnumerable<SelectListItem> MaterialOptions  { get; set; } = Enumerable.Empty<SelectListItem>();
    }
}

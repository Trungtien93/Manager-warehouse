using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.EntityFrameworkCore;
using MNBEMART.Models;
namespace MNBEMART.Models
{
    public class StockReceiptDetail
    {
        public int Id { get; set; }

        public int StockReceiptId { get; set; }
        [ValidateNever] public StockReceipt? StockReceipt { get; set; }

        [Required] public int MaterialId { get; set; }
        [ValidateNever] public Material? Material { get; set; }

        public string? Specification { get; set; }

        [Range(0, double.MaxValue, ErrorMessage = "Số lượng > 0")]
        public double Quantity { get; set; }

        [Precision(18, 0)]
        [Range(typeof(decimal), "0", "999999999999", ErrorMessage = "Đơn giá >= 0")]
        public decimal UnitPrice { get; set; }

        public string? Unit { get; set; }

        [Precision(18, 0)]
        public decimal Total => (decimal)Quantity * UnitPrice;
    }
}
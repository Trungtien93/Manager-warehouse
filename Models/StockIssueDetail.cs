using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace MNBEMART.Models
{
    public class StockIssueDetail
    {
        public int Id { get; set; }

        public int StockIssueId { get; set; }
        public StockIssue StockIssue { get; set; }

        public int MaterialId { get; set; }
        public Material Material { get; set; }

        public string Specification { get; set; }

        public double Quantity { get; set; }

        [Precision(18, 2)]
        public decimal UnitPrice { get; set; }

        public string Unit { get; set; }

        [Precision(18, 2)]
        public decimal Total => (decimal)Quantity * UnitPrice;
    }
}

using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace MNBEMART.Models
{
    public class LotHistory
    {
        public int Id { get; set; }
        
        [Required]
        public int LotId { get; set; }
        
        [Required]
        [StringLength(50)]
        public string Action { get; set; } = string.Empty;  // Split/Merge/Reserve/Release/Issue
        
        [Precision(18,3)]
        public decimal? QuantityBefore { get; set; }
        
        [Precision(18,3)]
        public decimal? QuantityAfter { get; set; }
        
        [StringLength(500)]
        public string? RelatedLotIds { get; set; }  // Comma-separated lot IDs
        
        [Required]
        [StringLength(100)]
        public string PerformedBy { get; set; } = string.Empty;
        
        [Required]
        public DateTime PerformedAt { get; set; } = DateTime.Now;
        
        [StringLength(500)]
        public string? Notes { get; set; }
        
        public StockLot? StockLot { get; set; }
    }
}























































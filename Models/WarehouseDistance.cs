using System.ComponentModel.DataAnnotations;

namespace MNBEMART.Models
{
    public class WarehouseDistance
    {
        public int Id { get; set; }
        
        [Required]
        public int FromWarehouseId { get; set; }
        
        [Required]
        public int ToWarehouseId { get; set; }
        
        public decimal DistanceKm { get; set; }  // Distance in kilometers
        public decimal EstimatedTimeHours { get; set; }  // Estimated transfer time
        public decimal BaseCost { get; set; }  // Pre-calculated base cost
        
        public Warehouse FromWarehouse { get; set; } = null!;
        public Warehouse ToWarehouse { get; set; } = null!;
    }
}























































namespace MNBEMART.Models
{
    public class Warehouse
    {
        public int Id { get; set; }

        public string Name { get; set; }      // Tên kho
        public string Address { get; set; }   // Địa chỉ kho

        // Transfer Optimization Fields
        public decimal? Latitude { get; set; }  // GPS coordinates
        public decimal? Longitude { get; set; }
        public decimal? BaseTransferCost { get; set; }  // Base cost per transfer (VND)
        public decimal? CostPerKm { get; set; }  // Cost per kilometer (VND/km)
        public decimal? CostPerKg { get; set; }  // Cost per kilogram (VND/kg)
        public string? Region { get; set; }  // Region/Zone for grouping

        public ICollection<Material> Materials { get; set; } 
        public ICollection<UserWarehouse> UserWarehouses { get; set; } = new List<UserWarehouse>();
        public ICollection<StockReceipt> StockReceipts { get; set; } = new List<StockReceipt>();
        public ICollection<StockIssue> StockIssues { get; set; } = new List<StockIssue>();
    }
}

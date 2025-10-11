namespace MNBEMART.Models
{
    public class Stock
    {
        public int Id { get; set; }
        public int WarehouseId { get; set; }
        public Warehouse Warehouse { get; set; }

        public int MaterialId { get; set; }
        public Material Material { get; set; }

        public decimal Quantity { get; set; }
        public DateTime LastUpdated { get; set; }
    }
}

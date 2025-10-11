
namespace MNBEMART.Models
{
    // Represents the relationship between users and warehouses
    public class UserWarehouse
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public int WarehouseId { get; set; }
        public User User { get; set; }
        public Warehouse Warehouse { get; set; }

        
    }


}

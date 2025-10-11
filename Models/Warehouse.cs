
namespace MNBEMART.Models
{
    public class Warehouse
    {
        public int Id { get; set; }

        public string Name { get; set; }      // Tên kho
        public string Address { get; set; }   // Địa chỉ kho

        public ICollection<Material> Materials { get; set; } 
        public ICollection<UserWarehouse> UserWarehouses { get; set; } = new List<UserWarehouse>();
        public ICollection<StockReceipt> StockReceipts { get; set; } = new List<StockReceipt>();
        public ICollection<StockIssue> StockIssues { get; set; } = new List<StockIssue>();
    }
}

using System.Collections.Generic;

namespace MNBEMART.Models
{
    public class Supplier
    {
        public int Id { get; set; }
        public string Name { get; set; }         // Tên nhà cung cấp
        public string Address { get; set; }      // Địa chỉ
        public string PhoneNumber { get; set; }  // SĐT liên hệ
        public string Email { get; set; }        // Email

        public ICollection<Material> Materials { get; set; }
    }
}

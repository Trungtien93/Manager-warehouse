namespace MNBEMART.Models
{
    public class Permission
    {
        public int Id { get; set; }
        public string Module { get; set; } = "";      // Ví dụ: "Nguyên liệu", "Kho hàng"
        public string DisplayName { get; set; } = ""; // Ví dụ: "Xem", "Thêm", "Sửa", "Xóa"
        public string ActionKey { get; set; } = "";   // "Read", "Create", "Update", "Delete"
        public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
    }
}

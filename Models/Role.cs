namespace MNBEMART.Models
{
    public class Role
    {
        public int Id { get; set; }
        public string Code { get; set; } = "";    // vd: "admin"
        public string Name { get; set; } = "";    // vd: "Quản trị"
        public string? Description { get; set; }
        public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
    }

    public class UserRole
    {
        public int UserId { get; set; }
        public int RoleId { get; set; }

        public User User { get; set; } = null!;
        public Role Role { get; set; } = null!;
    }
}

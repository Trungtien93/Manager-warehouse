namespace MNBEMART.Models
{
    public class RolePermission
    {
        public int Id { get; set; }
        public int RoleId { get; set; }
        public int PermissionId { get; set; }

        public bool CanRead { get; set; }
        public bool CanCreate { get; set; }
        public bool CanUpdate { get; set; }
        public bool CanDelete { get; set; }
        public bool CanApprove { get; set; }

        public Role Role { get; set; } = null!;
        public Permission Permission { get; set; } = null!;
    }
}

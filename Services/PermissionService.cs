using Microsoft.EntityFrameworkCore;
using MNBEMART.Data;

namespace MNBEMART.Services
{
    public class PermissionService : IPermissionService
    {
        private readonly AppDbContext _db;
        public PermissionService(AppDbContext db) => _db = db;

        public async Task<bool> HasAsync(int userId, string module, string actionKey)
        {
            // Admin (string role) bypass
            var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null) return false;
            if (string.Equals(user.Role?.Trim(), "Admin", StringComparison.OrdinalIgnoreCase))
                return true;

            var roleIds = new List<int>();
            
            // Try to get role ids from UserRoles table (may not exist yet)
            try
            {
                roleIds = await _db.UserRoles.Where(ur => ur.UserId == userId)
                                             .Select(ur => ur.RoleId)
                                             .ToListAsync();
            }
            catch (Microsoft.Data.SqlClient.SqlException)
            {
                // UserRoles table doesn't exist yet, fall through to legacy role mapping
            }

            // Fallback: map from legacy string Role to Role.Code
            if (!roleIds.Any())
            {
                var code = user.Role?.Trim();
                if (!string.IsNullOrEmpty(code))
                {
                    try
                    {
                        // So khớp không phân biệt hoa/thường để tương thích dữ liệu hiện có ("User" vs "user")
                        var roles = await _db.Roles.AsNoTracking().ToListAsync();
                        var role = roles.FirstOrDefault(r => string.Equals(r.Code, code, StringComparison.OrdinalIgnoreCase));
                        if (role != null) roleIds.Add(role.Id);
                    }
                    catch (Microsoft.Data.SqlClient.SqlException)
                    {
                        // Roles table may not exist either, return false
                        return false;
                    }
                }
            }

            if (!roleIds.Any())
            {
                // No roles found - if user has Role string "User", allow basic read access
                // This handles legacy data before RolePermissions system is fully set up
                var userRole = user.Role?.Trim();
                if (string.Equals(userRole, "User", StringComparison.OrdinalIgnoreCase))
                {
                    // Basic read-only access for User role
                    return string.Equals(actionKey, "Read", StringComparison.OrdinalIgnoreCase);
                }
                return false;
            }

            // EF Core cannot translate method calls inside query predicates. Use a pure expression.
            var key = actionKey;
            bool isRead = string.Equals(key, "Read", StringComparison.OrdinalIgnoreCase);
            bool isCreate = string.Equals(key, "Create", StringComparison.OrdinalIgnoreCase);
            bool isUpdate = string.Equals(key, "Update", StringComparison.OrdinalIgnoreCase);
            bool isDelete = string.Equals(key, "Delete", StringComparison.OrdinalIgnoreCase);
            bool isApprove = string.Equals(key, "Approve", StringComparison.OrdinalIgnoreCase);

            // Mô hình UI gộp 5 quyền vào 1 dòng theo Module, nên chỉ cần kiểm tra theo Module + cờ CanX
            try
            {
                var has = await (from rp in _db.RolePermissions
                                 join p in _db.Permissions on rp.PermissionId equals p.Id
                                 where roleIds.Contains(rp.RoleId)
                                       && p.Module == module
                                       && (
                                            (isRead   && rp.CanRead)   ||
                                            (isCreate && rp.CanCreate) ||
                                            (isUpdate && rp.CanUpdate) ||
                                            (isDelete && rp.CanDelete) ||
                                            (isApprove && rp.CanApprove)
                                          )
                                 select rp.Id).AnyAsync();
                
                // If no permission found and module is Documents, allow default permissions for User role
                // Also check if module exists in Permissions table - if not, use fallback
                if (!has && string.Equals(module, "Documents", StringComparison.OrdinalIgnoreCase))
                {
                    // Check if Documents module exists in Permissions table
                    var moduleExists = await _db.Permissions.AnyAsync(p => p.Module == "Documents");
                    
                    // If module doesn't exist in database OR no permission found, use fallback
                    if (!moduleExists || !has)
                    {
                        var userRole = user.Role?.Trim();
                        if (string.Equals(userRole, "User", StringComparison.OrdinalIgnoreCase) || string.Equals(userRole, "Admin", StringComparison.OrdinalIgnoreCase))
                        {
                            // User and Admin can Read, Create, Delete Documents by default
                            return isRead || isCreate || isDelete;
                        }
                    }
                }
                
                return has;
            }
            catch (Microsoft.Data.SqlClient.SqlException)
            {
                // RolePermissions or Permissions table may not exist
                // Fallback: if User role and Read action, allow access
                // For Documents module, also allow Create and Delete
                var userRole = user.Role?.Trim();
                if (string.Equals(userRole, "User", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.Equals(module, "Documents", StringComparison.OrdinalIgnoreCase))
                    {
                        return isRead || isCreate || isDelete;
                    }
                    else if (isRead)
                    {
                        return true;
                    }
                }
                return false;
            }
        }
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MNBEMART.Data;
using MNBEMART.Models;
using MNBEMART.Filters;
using MNBEMART.Services;
using System.Security.Claims;

namespace MNBEMART.Controllers
{
    [Authorize(Roles = "Admin")]
    public class RolesController : Controller
    {
        private readonly AppDbContext _context;
        private readonly INotificationService _notificationService;
        private readonly IPermissionService _permissionService;
        
        public RolesController(AppDbContext context, INotificationService notificationService, IPermissionService permissionService)
        {
            _context = context;
            _notificationService = notificationService;
            _permissionService = permissionService;
        }

        [RequirePermission("Roles", "Read")]
        public async Task<IActionResult> Index(bool partial = false)
        {
            var roles = await _context.Roles.AsNoTracking().OrderBy(r => r.Name).ToListAsync();
            
            if (partial || Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return PartialView("_RolesList", roles);
            }
            
            return View(roles);
        }

        [HttpPost]
        [RequirePermission("Roles", "Create")]
        public async Task<IActionResult> SeedDefaults()
        {
            string message = null;
            if (!await _context.Roles.AnyAsync())
            {
                var adminRole = new Role { Code = "Admin", Name = "Admin" };
                var userRole = new Role { Code = "User", Name = "User" };
                
                _context.Roles.AddRange(adminRole, userRole);
                await _context.SaveChangesAsync();

                // Thông báo khi tạo role mới
                await _notificationService.CreateNotificationAsync(
                    NotificationType.RoleCreated,
                    adminRole.Id,
                    "Vai trò mới được tạo: Admin",
                    "Vai trò Admin đã được tạo trong hệ thống",
                    null,
                    NotificationPriority.Normal);
                    
                await _notificationService.CreateNotificationAsync(
                    NotificationType.RoleCreated,
                    userRole.Id,
                    "Vai trò mới được tạo: User",
                    "Vai trò User đã được tạo trong hệ thống",
                    null,
                    NotificationPriority.Normal);
                
                message = "Đã tạo vai trò mặc định (Admin, User).";
            }
            else
            {
                message = "Các vai trò mặc định đã tồn tại trong hệ thống.";
            }
            
            // Handle AJAX request
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                // Reload roles list
                var roles = await _context.Roles.AsNoTracking().OrderBy(r => r.Name).ToListAsync();
                ViewBag.SuccessMessage = message;
                return PartialView("_RolesList", roles);
            }
            
            TempData["Msg"] = message ?? "Đã tạo vai trò mặc định (Admin, User).";
            return RedirectToAction("Index", "Settings", new { tab = "permissions" });
        }

        [RequirePermission("Roles", "Read")]
        public async Task<IActionResult> Permissions(int id, bool partial = false)
        {
            // 1. Load Role
            var role = await _context.Roles
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == id);
            
            if (role == null) return NotFound();

            // 2. Load Permissions và group theo Module
            var allPermissions = await _context.Permissions
                .AsNoTracking()
                .ToListAsync();
            
            // 3. Load RolePermissions của role này
            var rolePermissions = await _context.RolePermissions
                .Where(rp => rp.RoleId == id)
                .AsNoTracking()
                .ToListAsync();

            // 4. Define categories
            var categoryMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Materials"] = "Quản lý cơ bản",
                ["Suppliers"] = "Quản lý cơ bản",
                ["Warehouses"] = "Quản lý cơ bản",
                ["Stocks"] = "Quản lý cơ bản",
                ["Receipts"] = "Nghiệp vụ kho",
                ["Issues"] = "Nghiệp vụ kho",
                ["Transfers"] = "Nghiệp vụ kho",
                ["Lots"] = "Nghiệp vụ kho",
                ["Reports"] = "Báo cáo & Phân tích",
                ["PurchaseRequests"] = "Đề xuất & Phê duyệt",
                ["Users"] = "Hệ thống",
                ["Roles"] = "Hệ thống",
                ["Logs"] = "Hệ thống",
                ["Notifications"] = "Hệ thống",
                ["Documents"] = "Hệ thống"
            };

            // Define DisplayNames for modules (tiếng Việt)
            var displayNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Materials"] = "Nguyên liệu",
                ["Suppliers"] = "Nhà cung cấp",
                ["Warehouses"] = "Kho hàng",
                ["Stocks"] = "Tồn kho",
                ["Receipts"] = "Nhập kho",
                ["Issues"] = "Xuất kho",
                ["Transfers"] = "Chuyển kho",
                ["Lots"] = "Quản lý Lô",
                ["Reports"] = "Báo cáo",
                ["PurchaseRequests"] = "Đề xuất đặt hàng",
                ["Users"] = "Người dùng",
                ["Roles"] = "Vai trò",
                ["Logs"] = "Lịch sử thao tác",
                ["Notifications"] = "Thông báo",
                ["Documents"] = "Quản lý tài liệu"
            };

            // 5. Tạo ViewModel cho mỗi module
            var vm = new List<RolePermissionVM>();
            
            foreach (var moduleGroup in allPermissions.GroupBy(p => p.Module))
            {
                var module = moduleGroup.Key;
                
                // Loại bỏ module Adjustments vì chức năng này đã bị xóa
                if (string.Equals(module, "Adjustments", StringComparison.OrdinalIgnoreCase))
                    continue;
                
                // Tìm Read Permission của module này
                var readPerm = moduleGroup.FirstOrDefault(p => p.ActionKey == "Read");
                if (readPerm == null) continue;
                
                // Tìm RolePermission với Read PermissionId
                var rp = rolePermissions.FirstOrDefault(x => x.PermissionId == readPerm.Id);
                
                // Kiểm tra module có hỗ trợ Approve không
                var hasApprove = moduleGroup.Any(p => p.ActionKey == "Approve");
                
                // Lấy DisplayName từ database, nếu không có thì dùng mapping, cuối cùng mới dùng module name
                string displayName = !string.IsNullOrEmpty(readPerm.DisplayName) 
                    ? readPerm.DisplayName 
                    : (displayNameMap.TryGetValue(module, out var mappedName) ? mappedName : module);
                
                vm.Add(new RolePermissionVM
                {
                    PermissionId = readPerm.Id,
                    Module = module,
                    DisplayName = displayName,
                    Category = categoryMap.TryGetValue(module, out var cat) ? cat : "Khác",
                    CanRead = rp?.CanRead ?? false,
                    CanCreate = rp?.CanCreate ?? false,
                    CanUpdate = rp?.CanUpdate ?? false,
                    CanDelete = rp?.CanDelete ?? false,
                    CanApprove = rp?.CanApprove ?? false,
                    HasApprove = hasApprove
                });
            }
            
            // Group by category và sort
            vm = vm.OrderBy(x => x.Category).ThenBy(x => x.Module).ToList();

            ViewBag.Role = role;
            
            // Check if current user has Update permission
            // First check ClaimTypes.Role claim (set during login)
            var userRoleClaim = User.FindFirst(ClaimTypes.Role)?.Value;
            bool canUpdate = false;
            
            // Admin bypass: Check claim first (faster)
            if (!string.IsNullOrEmpty(userRoleClaim) && string.Equals(userRoleClaim, "Admin", StringComparison.OrdinalIgnoreCase))
            {
                canUpdate = true;
            }
            else
            {
                // Fallback: Check via PermissionService (which also checks database)
                var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (int.TryParse(userIdStr, out var userId))
                {
                    canUpdate = await _permissionService.HasAsync(userId, "Roles", "Update");
                }
            }
            
            ViewBag.CanUpdate = canUpdate;
            
            // Preserve success message
            if (ViewBag.SuccessMessage == null && TempData["SuccessMessage"] != null)
            {
                ViewBag.SuccessMessage = TempData["SuccessMessage"];
            }
            
            if (partial || Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return PartialView("_PermissionsForm", vm);
            }
            
            return View(vm);
        }

        [HttpPost]
        [RequirePermission("Roles", "Update")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SavePermissions(int roleId, List<RolePermissionVM> permissions)
        {
            // 1. Validate
            if (permissions == null || permissions.Count == 0)
            {
                TempData["Error"] = "Không có dữ liệu phân quyền để lưu.";
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    var role = await _context.Roles.FirstOrDefaultAsync(r => r.Id == roleId);
                    if (role == null) return NotFound();
                    return await Permissions(roleId, partial: true);
                }
                return RedirectToAction("Index", "Settings", new { tab = "permissions" });
            }

            // 2. Load RolePermissions hiện có
            var existing = await _context.RolePermissions
                .Where(x => x.RoleId == roleId)
                .ToListAsync();

            // 3. Process mỗi item
            foreach (var item in permissions)
            {
                // item.PermissionId = Read PermissionId (từ form)
                var rp = existing.FirstOrDefault(x => x.PermissionId == item.PermissionId);
                
                if (rp == null)
                {
                    // Nếu không có: tạo mới với tất cả flags = false
                    rp = new RolePermission 
                    { 
                        RoleId = roleId, 
                        PermissionId = item.PermissionId,
                        CanRead = false,
                        CanCreate = false,
                        CanUpdate = false,
                        CanDelete = false,
                        CanApprove = false
                    };
                    _context.RolePermissions.Add(rp);
                }

                // Update tất cả flags từ item (kể cả khi tất cả = false)
                rp.CanRead = item.CanRead;
                rp.CanCreate = item.CanCreate;
                rp.CanUpdate = item.CanUpdate;
                rp.CanDelete = item.CanDelete;
                rp.CanApprove = item.CanApprove;
            }

            // 4. Save
            await _context.SaveChangesAsync();

            // 5. Return success và reload form
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                ViewBag.SuccessMessage = "Cập nhật quyền thành công.";
                return await Permissions(roleId, partial: true);
            }
            
            TempData["Msg"] = "Cập nhật quyền thành công.";
            return RedirectToAction("Permissions", new { id = roleId });
        }
    }

    public class RolePermissionVM
    {
        public int PermissionId { get; set; }
        public string Module { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Category { get; set; } = "";
        public bool CanRead { get; set; }
        public bool CanCreate { get; set; }
        public bool CanUpdate { get; set; }
        public bool CanDelete { get; set; }
        public bool CanApprove { get; set; }
        public bool HasApprove { get; set; }
    }
}


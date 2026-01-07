using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MNBEMART.Data;
using MNBEMART.Filters;
using MNBEMART.Models;
using MNBEMART.Services;
using System.Security.Claims;

namespace MNBEMART.Controllers
{
    [Authorize]
    public class SettingsController : Controller
    {
        private readonly INotificationSettingsService _notificationSettingsService;
        private readonly AppDbContext _db;
        private readonly IPermissionService _permissionService;

        public SettingsController(INotificationSettingsService notificationSettingsService, AppDbContext db, IPermissionService permissionService)
        {
            _notificationSettingsService = notificationSettingsService;
            _db = db;
            _permissionService = permissionService;
        }

        private int? GetCurrentUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(userIdClaim, out int userId) ? userId : null;
        }

        private async Task<bool> IsAdminAsync()
        {
            // Kiểm tra từ ClaimTypes.Role trước
            var userRole = User.FindFirst(ClaimTypes.Role)?.Value;
            if (!string.IsNullOrEmpty(userRole) && string.Equals(userRole, "Admin", StringComparison.OrdinalIgnoreCase))
                return true;
            
            // Fallback: kiểm tra từ User.Role trong database
            var userId = GetCurrentUserId();
            if (userId.HasValue)
            {
                var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId.Value);
                if (user != null && string.Equals(user.Role?.Trim(), "Admin", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            
            return false;
        }

        // GET: Settings
        [HttpGet]
        [Route("Settings")]
        [Route("Settings/Index")]
        public async Task<IActionResult> Index(string tab = "notifications")
        {
            var userId = GetCurrentUserId();
            if (!userId.HasValue)
                return Unauthorized();

            var isAdmin = await IsAdminAsync();
            
            // Check permissions cho từng tab
            var hasRolesPermission = isAdmin || (userId.HasValue && await _permissionService.HasAsync(userId.Value, "Roles", "Read"));
            var hasUsersPermission = isAdmin || (userId.HasValue && await _permissionService.HasAsync(userId.Value, "Users", "Read"));
            var hasLogsPermission = isAdmin || (userId.HasValue && await _permissionService.HasAsync(userId.Value, "Logs", "Read"));
            var hasSystemPermission = isAdmin || (userId.HasValue && await _permissionService.HasAsync(userId.Value, "System", "Read"));
            
            // Cho phép tab dựa trên permission
            var adminTabs = new[] { "permissions", "logs", "users", "system" };
            var userTabs = new[] { "notifications", "notification-management" };
            
            // Check xem user có quyền truy cập tab không
            bool canAccessTab = true;
            if (adminTabs.Contains(tab, StringComparer.OrdinalIgnoreCase))
            {
                if (tab == "permissions") canAccessTab = hasRolesPermission;
                else if (tab == "users") canAccessTab = hasUsersPermission;
                else if (tab == "logs") canAccessTab = hasLogsPermission;
                else if (tab == "system") canAccessTab = hasSystemPermission;
                
                if (!canAccessTab)
                {
                    tab = "notifications";
                }
            }
            
            // Đảm bảo tab hợp lệ
            if (!adminTabs.Contains(tab, StringComparer.OrdinalIgnoreCase) && 
                !userTabs.Contains(tab, StringComparer.OrdinalIgnoreCase))
            {
                tab = "notifications";
            }

            ViewBag.IsAdmin = isAdmin;
            ViewBag.ActiveTab = tab;
            ViewBag.HasRolesPermission = hasRolesPermission;
            ViewBag.HasUsersPermission = hasUsersPermission;
            ViewBag.HasLogsPermission = hasLogsPermission;
            ViewBag.HasSystemPermission = hasSystemPermission;

            // Load notification settings nếu đang ở tab notifications
            if (tab == "notifications")
            {
                var notificationSettings = await _notificationSettingsService.GetSettingsAsync(userId.Value);
                ViewBag.NotificationSettings = notificationSettings;
            }

            return View();
        }

        // POST: Settings/UpdateNotificationSettings
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Notifications", "Update")]
        public async Task<IActionResult> UpdateNotificationSettings(NotificationSettings settings)
        {
            var userId = GetCurrentUserId();
            if (!userId.HasValue)
                return Unauthorized();

            // Đảm bảo UserId luôn là user hiện tại (bảo mật)
            settings.UserId = userId.Value;

            // Handle EnabledTypes from form
            var enabledTypes = Request.Form["enabledTypes"].ToList();
            if (enabledTypes.Any())
            {
                settings.EnabledTypes = System.Text.Json.JsonSerializer.Serialize(enabledTypes);
            }
            else
            {
                settings.EnabledTypes = null; // All enabled by default
            }

            if (!ModelState.IsValid)
            {
                // Reload settings để đảm bảo hiển thị đúng
                var currentSettings = await _notificationSettingsService.GetSettingsAsync(userId.Value);
                settings.UserId = userId.Value;
                ViewBag.IsAdmin = await IsAdminAsync();
                ViewBag.ActiveTab = "notifications";
                ViewBag.NotificationSettings = settings;
                return View("Index", settings);
            }

            // Luôn sử dụng userId từ session, không tin tưởng giá trị từ form
            await _notificationSettingsService.UpdateSettingsAsync(userId.Value, settings);

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return Json(new { success = true });
            }

            TempData["Msg"] = "Đã cập nhật cài đặt thông báo thành công";
            return RedirectToAction(nameof(Index), new { tab = "notifications" });
        }
    }
}


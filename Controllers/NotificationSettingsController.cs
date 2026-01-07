using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MNBEMART.Filters;
using MNBEMART.Models;
using MNBEMART.Services;
using System.Security.Claims;

namespace MNBEMART.Controllers
{
    [Authorize]
    public class NotificationSettingsController : Controller
    {
        private readonly INotificationSettingsService _settingsService;

        public NotificationSettingsController(INotificationSettingsService settingsService)
        {
            _settingsService = settingsService;
        }

        private int? GetCurrentUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(userIdClaim, out int userId) ? userId : null;
        }

        // GET: NotificationSettings
        [RequirePermission("Notifications", "Read")]
        public async Task<IActionResult> Index()
        {
            // Redirect to Settings page for better UX
            return RedirectToAction("Index", "Settings", new { tab = "notifications" });
        }

        // POST: NotificationSettings/Update
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Notifications", "Update")]
        public async Task<IActionResult> Update(NotificationSettings settings)
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
                // Redirect to Settings page with error
                TempData["Error"] = "Có lỗi xảy ra khi cập nhật cài đặt. Vui lòng thử lại.";
                return RedirectToAction("Index", "Settings", new { tab = "notifications" });
            }

            // Luôn sử dụng userId từ session, không tin tưởng giá trị từ form
            await _settingsService.UpdateSettingsAsync(userId.Value, settings);

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return Json(new { success = true });
            }

            TempData["Msg"] = "Đã cập nhật cài đặt thông báo thành công";
            return RedirectToAction(nameof(Index));
        }
    }
}


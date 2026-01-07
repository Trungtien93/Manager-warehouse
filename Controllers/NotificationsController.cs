using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MNBEMART.Filters;
using MNBEMART.Services;
using System.Security.Claims;
using System.Linq;
using MNBEMART.Extensions;
using MNBEMART.Data;
using Microsoft.Extensions.Logging;

namespace MNBEMART.Controllers
{
    [Authorize]
    public class NotificationsController : Controller
    {
        private readonly INotificationService _notificationService;
        private readonly INotificationSettingsService _settingsService;
        private readonly IQuickActionService _quickActionService;
        private readonly AppDbContext _db;
        private readonly ILogger<NotificationsController>? _logger;

        public NotificationsController(
            INotificationService notificationService, 
            INotificationSettingsService settingsService,
            IQuickActionService quickActionService,
            AppDbContext db,
            ILogger<NotificationsController>? logger = null)
        {
            _notificationService = notificationService;
            _settingsService = settingsService;
            _quickActionService = quickActionService;
            _db = db;
            _logger = logger;
        }

        private int? GetCurrentUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(userIdClaim, out int userId) ? userId : null;
        }

        // GET: Notifications
        [RequirePermission("Notifications", "Read")]
        public async Task<IActionResult> Index(
            int page = 1, 
            int pageSize = 5,
            string? type = null,
            string? status = null,
            bool? isImportant = null,
            bool? isArchived = null,
            string? search = null,
            bool? partial = null)
        {
            var userId = GetCurrentUserId();
            
            // Auto-delete old notifications (90 days) for this user only
            if (userId.HasValue)
            {
                await _notificationService.AutoDeleteOldNotificationsAsync(userId.Value, 90);
            }
            
            // Get all notifications for this user only (no shared notifications)
            var query = _db.Notifications.Where(n => !n.IsDeleted).AsNoTracking().AsQueryable();
            
            if (userId.HasValue)
            {
                // Chỉ lấy thông báo của user hiện tại, không lấy thông báo chung (UserId == null)
                query = query.Where(n => n.UserId == userId.Value);
            }
            else
            {
                // Nếu không có userId, không trả về thông báo nào
                query = query.Where(n => false);
            }

            // Filters
            if (!string.IsNullOrEmpty(type) && Enum.TryParse<Models.NotificationType>(type, true, out var notificationType))
            {
                query = query.Where(n => n.Type == notificationType);
            }

            if (status == "unread")
            {
                query = query.Where(n => !n.IsRead);
            }
            else if (status == "read")
            {
                query = query.Where(n => n.IsRead);
            }

            if (isImportant.HasValue)
            {
                query = query.Where(n => n.IsImportant == isImportant.Value);
            }

            if (isArchived.HasValue)
            {
                query = query.Where(n => n.IsArchived == isArchived.Value);
            }
            else
            {
                // By default, don't show archived
                query = query.Where(n => !n.IsArchived);
            }

            if (!string.IsNullOrEmpty(search))
            {
                query = query.Where(n => n.Title.Contains(search) || (n.Message != null && n.Message.Contains(search)));
            }
            
            var pagedResult = await query
                .OrderByDescending(n => n.CreatedAt)
                .ToPagedResultAsync(page, pageSize);
            
            ViewBag.Page = pagedResult.Page;
            ViewBag.PageSize = pagedResult.PageSize;
            ViewBag.TotalItems = pagedResult.TotalItems;
            ViewBag.TotalPages = pagedResult.TotalPages;
            ViewBag.PagedResult = pagedResult;
            ViewBag.TypeFilter = type;
            ViewBag.StatusFilter = status;
            ViewBag.IsImportantFilter = isImportant;
            ViewBag.IsArchivedFilter = isArchived;
            ViewBag.Search = search;
            
            // Check if this is a partial request (from Settings page or AJAX)
            bool isPartial = partial == true || Request.Headers["X-Requested-With"] == "XMLHttpRequest";
            
            if (isPartial)
            {
                return PartialView(pagedResult.Items.ToList());
            }
            
            return View(pagedResult.Items.ToList());
        }

        // POST: Notifications/MarkAsRead/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Notifications", "Update")]
        public async Task<IActionResult> MarkAsRead(int id)
        {
            var userId = GetCurrentUserId();
            await _notificationService.MarkAsReadAsync(id, userId);
            
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                var unreadCount = await _notificationService.GetUnreadCountAsync(userId);
                return Json(new { success = true, unreadCount });
            }
            
            return RedirectToAction(nameof(Index));
        }

        // POST: Notifications/MarkAllAsRead
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Notifications", "Update")]
        public async Task<IActionResult> MarkAllAsRead()
        {
            var userId = GetCurrentUserId();
            await _notificationService.MarkAllAsReadAsync(userId);
            
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return Json(new { success = true, unreadCount = 0 });
            }
            
            return RedirectToAction(nameof(Index));
        }

        // GET: Notifications/GetUnreadCount
        [HttpGet]
        [RequirePermission("Notifications", "Read")]
        public async Task<IActionResult> GetUnreadCount()
        {
            var userId = GetCurrentUserId();
            var count = await _notificationService.GetUnreadCountAsync(userId);
            return Json(new { count });
        }

        // GET: Notifications/GetRecentNotifications
        [HttpGet]
        [RequirePermission("Notifications", "Read")]
        public async Task<IActionResult> GetRecentNotifications(int limit = 10)
        {
            try
        {
            var userId = GetCurrentUserId();
            var notifications = await _notificationService.GetNotificationsAsync(userId, page: 1, pageSize: limit);
            
            var result = notifications.Select(n => new
            {
                id = n.Id,
                type = n.Type.ToString(),
                title = n.Title,
                message = n.Message,
                isRead = n.IsRead,
                    isImportant = n.IsImportant,
                    priority = n.Priority.ToString(),
                createdAt = n.CreatedAt,
                documentId = n.DocumentId,
                detailUrl = n.Type switch
                {
                    Models.NotificationType.Receipt => $"/StockReceipts/Details/{n.DocumentId}",
                    Models.NotificationType.Issue => $"/StockIssues/Details/{n.DocumentId}",
                    Models.NotificationType.Transfer => $"/StockTransfers/Details/{n.DocumentId}",
                    Models.NotificationType.PurchaseRequest => $"/PurchaseRequests/Details/{n.DocumentId}",
                    Models.NotificationType.ExpiryAlert => "/Materials/Expiring",
                        Models.NotificationType.LowStockAlert => "/Stocks/Index",
                        Models.NotificationType.UserRegistration => $"/Users/Details/{n.DocumentId}",
                        Models.NotificationType.RoleCreated => $"/Roles/Details/{n.DocumentId}",
                        Models.NotificationType.WarehouseCreated => $"/Warehouses/Details/{n.DocumentId}",
                    _ => "#"
                },
                    timeAgo = GetTimeAgo(n.CreatedAt),
                    quickActions = GetQuickActions(n.Type, n.DocumentId)
                });
                
                return Json(result);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting recent notifications");
                
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    return Json(new { error = "Lỗi khi tải thông báo", message = ex.Message });
                }
                
                return StatusCode(500, new { error = "Lỗi khi tải thông báo", message = ex.Message });
            }
        }

        // POST: Notifications/MarkAsImportant/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Notifications", "Update")]
        public async Task<IActionResult> MarkAsImportant(int id)
        {
            var userId = GetCurrentUserId();
            await _notificationService.MarkAsImportantAsync(id, userId);
            
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return Json(new { success = true });
            }
            
            return RedirectToAction(nameof(Index));
        }

        // POST: Notifications/UnmarkAsImportant/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Notifications", "Update")]
        public async Task<IActionResult> UnmarkAsImportant(int id)
        {
            var userId = GetCurrentUserId();
            await _notificationService.UnmarkAsImportantAsync(id, userId);
            
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return Json(new { success = true });
            }
            
            return RedirectToAction(nameof(Index));
        }

        // POST: Notifications/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Notifications", "Delete")]
        public async Task<IActionResult> Delete(int id)
        {
            var userId = GetCurrentUserId();
            await _notificationService.DeleteNotificationAsync(id, userId);
            
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                var unreadCount = await _notificationService.GetUnreadCountAsync(userId);
                return Json(new { success = true, unreadCount });
            }
            
            return RedirectToAction(nameof(Index));
        }

        // POST: Notifications/DeleteMultiple
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Notifications", "Delete")]
        public async Task<IActionResult> DeleteMultiple()
        {
            var userId = GetCurrentUserId();
            List<int> ids = new List<int>();
            
            // Try to get from JSON body first
            if (Request.ContentType?.Contains("application/json") == true)
            {
                try
                {
                    Request.EnableBuffering();
                    Request.Body.Position = 0;
                    using var reader = new System.IO.StreamReader(Request.Body, System.Text.Encoding.UTF8, leaveOpen: true);
                    var body = await reader.ReadToEndAsync();
                    Request.Body.Position = 0;
                    
                    if (!string.IsNullOrEmpty(body))
                    {
                        var jsonDoc = System.Text.Json.JsonDocument.Parse(body);
                        if (jsonDoc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array)
                        {
                            foreach (var element in jsonDoc.RootElement.EnumerateArray())
                            {
                                if (element.ValueKind == System.Text.Json.JsonValueKind.Number)
                                {
                                    ids.Add(element.GetInt32());
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error parsing DeleteMultiple request body");
                }
            }
            
            // If no IDs from body, try form data
            if (ids.Count == 0)
            {
                var idsParam = Request.Form["ids"].ToString();
                if (!string.IsNullOrEmpty(idsParam))
                {
                    try
                    {
                        ids = System.Text.Json.JsonSerializer.Deserialize<List<int>>(idsParam) ?? new List<int>();
                    }
                    catch
                    {
                        // Ignore
                    }
                }
            }
            
            if (ids.Count == 0)
            {
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    return Json(new { success = false, message = "No IDs provided" });
                }
                TempData["Error"] = "Vui lòng chọn thông báo cần xóa";
                return RedirectToAction(nameof(Index));
            }
            
            await _notificationService.DeleteMultipleNotificationsAsync(ids, userId);
            
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                var unreadCount = await _notificationService.GetUnreadCountAsync(userId);
                return Json(new { success = true, unreadCount });
            }
            
            TempData["Msg"] = $"Đã xóa {ids.Count} thông báo";
            return RedirectToAction(nameof(Index));
        }

        // POST: Notifications/Archive/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Notifications", "Update")]
        public async Task<IActionResult> Archive(int id)
        {
            var userId = GetCurrentUserId();
            await _notificationService.ArchiveNotificationAsync(id, userId);
            
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return Json(new { success = true });
            }
            
            return RedirectToAction(nameof(Index));
        }

        // POST: Notifications/Restore/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Notifications", "Update")]
        public async Task<IActionResult> Restore(int id)
        {
            var userId = GetCurrentUserId();
            await _notificationService.RestoreNotificationAsync(id, userId);
            
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return Json(new { success = true });
            }
            
            return RedirectToAction(nameof(Index));
        }

        // GET: Notifications/GetImportant
        [HttpGet]
        [RequirePermission("Notifications", "Read")]
        public async Task<IActionResult> GetImportant(int page = 1, int pageSize = 5, bool? partial = null)
        {
            var userId = GetCurrentUserId();
            var notifications = await _notificationService.GetImportantNotificationsAsync(userId, page, pageSize);
            
            bool isPartial = partial == true || Request.Headers["X-Requested-With"] == "XMLHttpRequest";
            
            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.StatusFilter = "important";
            
            if (isPartial)
            {
                return PartialView("Index", notifications);
            }
            
            return View("Index", notifications);
        }

        // GET: Notifications/GetSettings
        [HttpGet]
        [RequirePermission("Notifications", "Read")]
        public async Task<IActionResult> GetSettings()
        {
            var userId = GetCurrentUserId();
            if (!userId.HasValue)
                return Unauthorized();

            var settings = await _settingsService.GetSettingsAsync(userId.Value);
            
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return Json(settings);
            }
            
            // Redirect to Settings page for better UX
            return RedirectToAction("Index", "Settings", new { tab = "notifications" });
        }

        // POST: Notifications/UpdateSettings
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Notifications", "Update")]
        public async Task<IActionResult> UpdateSettings([FromForm] Models.NotificationSettings settings)
        {
            var userId = GetCurrentUserId();
            if (!userId.HasValue)
                return Unauthorized();

            await _settingsService.UpdateSettingsAsync(userId.Value, settings);
            
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return Json(new { success = true });
            }
            
            TempData["Msg"] = "Đã cập nhật cài đặt thông báo";
            return RedirectToAction("Index", "Settings", new { tab = "notifications" });
        }

        // POST: Notifications/QuickAction
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Notifications", "Update")]
        public async Task<IActionResult> QuickAction(string action, int documentId, int? notificationId = null)
        {
            var userId = GetCurrentUserId();
            if (!userId.HasValue)
                return Unauthorized();

            bool success = false;
            
            switch (action.ToLower())
            {
                case "approve_pr":
                    success = await _quickActionService.QuickApprovePurchaseRequestAsync(documentId, userId.Value);
                    break;
                case "reject_pr":
                    var reason = Request.Form["reason"].ToString();
                    success = await _quickActionService.QuickRejectPurchaseRequestAsync(documentId, userId.Value, reason);
                    break;
                case "confirm_receipt":
                    success = await _quickActionService.QuickConfirmReceiptAsync(documentId, userId.Value);
                    break;
                case "cancel_receipt":
                    success = await _quickActionService.QuickCancelReceiptAsync(documentId, userId.Value);
                    break;
                case "approve_user":
                    success = await _quickActionService.QuickApproveUserAsync(documentId, userId.Value);
                    break;
                case "reject_user":
                    var rejectReason = Request.Form["reason"].ToString();
                    success = await _quickActionService.QuickRejectUserAsync(documentId, userId.Value, rejectReason);
                    break;
            }

            // Mark notification as read if provided
            if (notificationId.HasValue && success)
            {
                await _notificationService.MarkAsReadAsync(notificationId.Value, userId);
            }

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                var unreadCount = await _notificationService.GetUnreadCountAsync(userId);
                return Json(new { success, unreadCount });
            }

            TempData["Msg"] = success ? "Đã xử lý thành công" : "Xử lý thất bại";
            return RedirectToAction(nameof(Index));
        }

        private object? GetQuickActions(Models.NotificationType type, int documentId)
        {
            var userId = GetCurrentUserId();
            if (!userId.HasValue)
                return null;

            // Kiểm tra xem user có phải admin không
            var user = _db.Users.AsNoTracking().FirstOrDefault(u => u.Id == userId.Value);
            if (user == null)
                return null;

            bool isAdmin = string.Equals(user.Role?.Trim(), "Admin", StringComparison.OrdinalIgnoreCase);
            
            // Nếu không phải admin, không có quick actions
            if (!isAdmin)
                return null;

            // Chỉ admin mới có quick actions
            return type switch
            {
                Models.NotificationType.PurchaseRequest => new { approve = true, reject = true },
                Models.NotificationType.Receipt => new { confirm = true, cancel = true },
                Models.NotificationType.UserRegistration => new { approve = true, reject = true },
                _ => null
            };
        }

        // POST: Notifications/MarkMultipleAsRead
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Notifications", "Update")]
        public async Task<IActionResult> MarkMultipleAsRead([FromForm] string ids)
        {
            var userId = GetCurrentUserId();
            try
            {
                var idList = System.Text.Json.JsonSerializer.Deserialize<List<int>>(ids) ?? new List<int>();
                foreach (var id in idList)
                {
                    await _notificationService.MarkAsReadAsync(id, userId);
                }
                
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    var unreadCount = await _notificationService.GetUnreadCountAsync(userId);
                    return Json(new { success = true, unreadCount });
                }
                
                TempData["Msg"] = $"Đã đánh dấu {idList.Count} thông báo đã đọc";
                return RedirectToAction(nameof(Index));
            }
            catch
            {
                return Json(new { success = false });
            }
        }

        // GET: Notifications/ExportExcel
        [RequirePermission("Notifications", "Read")]
        public async Task<IActionResult> ExportExcel(string? type = null, string? status = null, string? search = null)
        {
            var userId = GetCurrentUserId();
            var query = _db.Notifications.Where(n => !n.IsDeleted).AsQueryable();
            
            if (userId.HasValue)
            {
                // Chỉ lấy thông báo của user hiện tại
                query = query.Where(n => n.UserId == userId.Value);
            }
            else
            {
                // Nếu không có userId, không trả về thông báo nào
                query = query.Where(n => false);
            }

            if (!string.IsNullOrEmpty(type) && Enum.TryParse<Models.NotificationType>(type, true, out var notificationType))
            {
                query = query.Where(n => n.Type == notificationType);
            }

            if (status == "unread")
            {
                query = query.Where(n => !n.IsRead);
            }
            else if (status == "read")
            {
                query = query.Where(n => n.IsRead);
            }

            if (!string.IsNullOrEmpty(search))
            {
                query = query.Where(n => n.Title.Contains(search) || (n.Message != null && n.Message.Contains(search)));
            }

            var notifications = await query.OrderByDescending(n => n.CreatedAt).ToListAsync();
            
            var excelService = HttpContext.RequestServices.GetRequiredService<IExcelService>();
            var headers = new[] { "ID", "Loại", "Tiêu đề", "Nội dung", "Trạng thái", "Quan trọng", "Ngày tạo" };
            var data = notifications.Select(n => new
            {
                Id = n.Id,
                Type = n.Type.ToString(),
                Title = n.Title,
                Message = n.Message ?? "",
                Status = n.IsRead ? "Đã đọc" : "Chưa đọc",
                IsImportant = n.IsImportant ? "Có" : "Không",
                CreatedAt = n.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm")
            });
            
            var bytes = excelService.ExportToExcel(data, "Thông báo", headers);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"ThongBao_{DateTime.Now:yyyyMMddHHmmss}.xlsx");
        }

        private string GetTimeAgo(DateTime dateTime)
        {
            var timeSpan = DateTime.UtcNow - dateTime.ToUniversalTime();
            
            if (timeSpan.TotalMinutes < 1)
                return "Vừa xong";
            if (timeSpan.TotalMinutes < 60)
                return $"{(int)timeSpan.TotalMinutes} phút trước";
            if (timeSpan.TotalHours < 24)
                return $"{(int)timeSpan.TotalHours} giờ trước";
            if (timeSpan.TotalDays < 7)
                return $"{(int)timeSpan.TotalDays} ngày trước";
            
            return dateTime.ToLocalTime().ToString("dd/MM/yyyy");
        }
    }
}


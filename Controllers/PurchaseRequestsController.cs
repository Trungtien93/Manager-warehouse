using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using MNBEMART.Data;
using MNBEMART.Models;
using MNBEMART.Services;
using MNBEMART.Filters;
using System.Security.Claims;
using MNBEMART.Extensions;

namespace MNBEMART.Controllers
{
    [Authorize]
    public class PurchaseRequestsController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IAutoOrderService _autoOrderService;
        private readonly IDocumentNumberingService _documentNumbering;
        private readonly IEmailService _emailService;
        private readonly INotificationService _notificationService;
        private readonly IQuickActionService _quickActionService;

        public PurchaseRequestsController(
            AppDbContext context,
            IAutoOrderService autoOrderService,
            IDocumentNumberingService documentNumbering,
            IEmailService emailService,
            INotificationService notificationService,
            IQuickActionService quickActionService)
        {
            _context = context;
            _autoOrderService = autoOrderService;
            _documentNumbering = documentNumbering;
            _emailService = emailService;
            _notificationService = notificationService;
            _quickActionService = quickActionService;
        }

        private int GetCurrentUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(userIdClaim, out int userId) ? userId : 1;
        }

        // GET: PurchaseRequests/Index
        [RequirePermission("PurchaseRequests", "Read")]
        public async Task<IActionResult> Index(string status = "all", int page = 1, int pageSize = 30)
        {
            var query = _context.PurchaseRequests
                .Include(pr => pr.RequestedBy)
                .Include(pr => pr.ApprovedBy)
                .Include(pr => pr.Details)
                .AsQueryable();

            if (status != "all")
            {
                if (Enum.TryParse<PRStatus>(status, true, out var prStatus))
                {
                    query = query.Where(pr => pr.Status == prStatus);
                }
            }

            var pagedResult = await query
                .OrderByDescending(pr => pr.CreatedAt)
                .ToPagedResultAsync(page, pageSize);

            ViewBag.StatusFilter = status;
            ViewBag.Page = pagedResult.Page;
            ViewBag.PageSize = pagedResult.PageSize;
            ViewBag.TotalItems = pagedResult.TotalItems;
            ViewBag.TotalPages = pagedResult.TotalPages;
            ViewBag.PagedResult = pagedResult;
            ViewBag.AllCount = await _context.PurchaseRequests.CountAsync();
            ViewBag.PendingCount = await _context.PurchaseRequests.Where(pr => pr.Status == PRStatus.Pending).CountAsync();
            ViewBag.ApprovedCount = await _context.PurchaseRequests.Where(pr => pr.Status == PRStatus.Approved).CountAsync();
            ViewBag.OrderedCount = await _context.PurchaseRequests.Where(pr => pr.Status == PRStatus.Ordered).CountAsync();
            ViewBag.CancelledCount = await _context.PurchaseRequests.Where(pr => pr.Status == PRStatus.Cancelled).CountAsync();

            return View(pagedResult.Items.ToList());
        }

        // GET: PurchaseRequests/Details/5
        [RequirePermission("PurchaseRequests", "Read")]
        public async Task<IActionResult> Details(int id)
        {
            var request = await _context.PurchaseRequests
                .Include(pr => pr.RequestedBy)
                .Include(pr => pr.ApprovedBy)
                .Include(pr => pr.Details)
                    .ThenInclude(d => d.Material)
                .Include(pr => pr.Details)
                    .ThenInclude(d => d.PreferredSupplier)
                .FirstOrDefaultAsync(pr => pr.Id == id);

            if (request == null)
                return NotFound();

            return View(request);
        }

        // GET: PurchaseRequests/Create
        [RequirePermission("PurchaseRequests", "Create")]
        public async Task<IActionResult> Create()
        {
            await PopulateDropdowns();
            return View();
        }

        // POST: PurchaseRequests/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("PurchaseRequests", "Create")]
        public async Task<IActionResult> Create(PurchaseRequest model, List<PurchaseRequestDetail> details)
        {
            if (details == null || !details.Any())
            {
                TempData["Error"] = "Vui lòng thêm ít nhất một vật tư";
                await PopulateDropdowns();
                return View(model);
            }

            try
            {
                // Generate request number
                var requestNumber = await _documentNumbering.NextAsync("PR");

                var requestedById = GetCurrentUserId();

                var request = new PurchaseRequest
                {
                    RequestNumber = requestNumber,
                    RequestDate = DateTime.Now,
                    RequestedById = requestedById,
                    Status = PRStatus.Pending,
                    Notes = model.Notes,
                    IsAutoGenerated = false,
                    CreatedAt = DateTime.Now
                };

                // Add details
                foreach (var detail in details.Where(d => d.MaterialId > 0 && d.RequestedQuantity > 0))
                {
                    var material = await _context.Materials.FindAsync(detail.MaterialId);
                    if (material == null) continue;

                    // Get current stock
                    var currentStock = await _context.Stocks
                        .Where(s => s.MaterialId == detail.MaterialId)
                        .SumAsync(s => (decimal?)s.Quantity) ?? 0;

                    request.Details.Add(new PurchaseRequestDetail
                    {
                        MaterialId = detail.MaterialId,
                        RequestedQuantity = detail.RequestedQuantity,
                        CurrentStock = currentStock,
                        MinimumStock = material.MinimumStock ?? 0,
                        PreferredSupplierId = detail.PreferredSupplierId ?? material.PreferredSupplierId,
                        EstimatedPrice = material.PurchasePrice,
                        Notes = detail.Notes
                    });
                }

                await _context.PurchaseRequests.AddAsync(request);
                await _context.SaveChangesAsync();

                // Reload request with includes for email
                var requestForEmail = await _context.PurchaseRequests
                    .Include(r => r.RequestedBy)
                    .Include(r => r.Details)
                        .ThenInclude(d => d.Material)
                    .FirstOrDefaultAsync(r => r.Id == request.Id);
                
                // Send email notification
                if (requestForEmail != null)
                {
                    try
                    {
                        var materialNames = requestForEmail.Details
                            .Where(d => d.Material != null)
                            .Select(d => d.Material!.Name)
                            .ToList();
                        var totalQty = requestForEmail.Details.Sum(d => d.RequestedQuantity);
                        var requestedByName = requestForEmail.RequestedBy?.FullName ?? "System";
                        
                        await _emailService.SendPurchaseRequestNotificationAsync(
                            requestForEmail.Id,
                            requestForEmail.RequestNumber,
                            requestedByName,
                            materialNames,
                            totalQty
                        );
                    }
                    catch
                    {
                        // Email failure should not prevent PR creation
                    }
                }

                // Create in-app notifications for admins and approvers
                try
                {
                    // Get Admin users
                    var adminUserIds = await _context.Users
                        .Where(u => u.IsActive && u.Role != null && u.Role.ToLower() == "admin")
                        .Select(u => u.Id)
                        .ToListAsync();

                    // Get users with PurchaseRequests.Approve permission
                    var approverUserIds = await (from u in _context.Users
                                                where u.IsActive
                                                join ur in _context.UserRoles on u.Id equals ur.UserId
                                                join rp in _context.RolePermissions on ur.RoleId equals rp.RoleId
                                                join p in _context.Permissions on rp.PermissionId equals p.Id
                                                where p.Module == "PurchaseRequests" && rp.CanApprove
                                                select u.Id)
                                                .Distinct()
                                                .ToListAsync();

                    // Combine and remove duplicates
                    var notifyUserIds = adminUserIds.Union(approverUserIds).Distinct().ToList();

                    if (notifyUserIds.Any())
                    {
                        var requestedByName = requestForEmail?.RequestedBy?.FullName ?? "System";
                        var title = $"Đề xuất đặt hàng mới: {requestNumber}";
                        var message = $"Người tạo: {requestedByName}";
                        
                        await _notificationService.CreateNotificationForUsersAsync(
                            NotificationType.PurchaseRequest,
                            request.Id,
                            title,
                            message,
                            notifyUserIds
                        );
                    }
                }
                catch
                {
                    // Notification failure should not prevent PR creation
                }

                TempData["Msg"] = $"Đã tạo đề xuất đặt hàng {requestNumber}";
                return RedirectToAction(nameof(Details), new { id = request.Id });
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Lỗi: {ex.Message}";
                await PopulateDropdowns();
                return View(model);
            }
        }

        // POST: PurchaseRequests/Approve/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("PurchaseRequests", "Approve")]
        public async Task<IActionResult> Approve(int id)
        {
            var request = await _context.PurchaseRequests.FindAsync(id);
            if (request == null)
                return NotFound();

            if (request.Status != PRStatus.Pending)
            {
                TempData["Error"] = "Chỉ có thể duyệt đề xuất ở trạng thái Chờ duyệt";
                return RedirectToAction(nameof(Details), new { id });
            }

            request.Status = PRStatus.Approved;
            request.ApprovedById = GetCurrentUserId();
            request.ApprovedDate = DateTime.Now;
            request.UpdatedAt = DateTime.Now;

            await _context.SaveChangesAsync();

            // Thông báo cho user tạo đề xuất
            if (request.RequestedById > 0)
            {
                await _notificationService.CreateNotificationAsync(
                    NotificationType.PurchaseRequest,
                    id,
                    $"Đề xuất đặt hàng đã được duyệt: {request.RequestNumber}",
                    $"Đề xuất đặt hàng của bạn đã được admin duyệt",
                    request.RequestedById,
                    NotificationPriority.Normal
                );
            }

            // Send email to requester
            try
            {
                await _context.Entry(request).Reference(r => r.RequestedBy).LoadAsync();
                if (request.RequestedBy != null && !string.IsNullOrEmpty(request.RequestedBy.FullName))
                {
                    // Note: User model doesn't have Email field, using FullName as identifier
                    // In production, you should add Email field to User model
                    var userEmail = request.RequestedBy.FullName; // Placeholder - should be Email field
                    await _emailService.SendPurchaseRequestStatusChangeAsync(
                        userEmail,
                        request.RequestedBy.FullName,
                        request.RequestNumber,
                        "Approved"
                    );
                }
            }
            catch
            {
                // Log error but don't fail the approval
            }

            TempData["Msg"] = $"Đã duyệt đề xuất {request.RequestNumber}";
            return RedirectToAction(nameof(Details), new { id });
        }

        // POST: PurchaseRequests/Reject/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("PurchaseRequests", "Approve")]
        public async Task<IActionResult> Reject(int id, string reason)
        {
            var request = await _context.PurchaseRequests.FindAsync(id);
            if (request == null)
                return NotFound();

            if (request.Status != PRStatus.Pending)
            {
                TempData["Error"] = "Chỉ có thể từ chối đề xuất ở trạng thái Chờ duyệt";
                return RedirectToAction(nameof(Details), new { id });
            }

            request.Status = PRStatus.Cancelled;
            request.Notes = (request.Notes ?? "") + $"\nLý do từ chối: {reason}";
            request.UpdatedAt = DateTime.Now;

            await _context.SaveChangesAsync();

            // Thông báo cho user tạo đề xuất
            if (request.RequestedById > 0)
            {
                await _notificationService.CreateNotificationAsync(
                    NotificationType.PurchaseRequest,
                    id,
                    $"Đề xuất đặt hàng bị từ chối: {request.RequestNumber}",
                    reason != null ? $"Lý do: {reason}" : "Đề xuất đặt hàng của bạn đã bị từ chối",
                    request.RequestedById,
                    NotificationPriority.High
                );
            }

            // Send email to requester
            try
            {
                await _context.Entry(request).Reference(r => r.RequestedBy).LoadAsync();
                if (request.RequestedBy != null && !string.IsNullOrEmpty(request.RequestedBy.FullName))
                {
                    var userEmail = request.RequestedBy.FullName; // Placeholder - should be Email field
                    await _emailService.SendPurchaseRequestStatusChangeAsync(
                        userEmail,
                        request.RequestedBy.FullName,
                        request.RequestNumber,
                        "Rejected",
                        reason
                    );
                }
            }
            catch
            {
                // Log error but don't fail the rejection
            }

            TempData["Msg"] = $"Đã từ chối đề xuất {request.RequestNumber}";
            return RedirectToAction(nameof(Index));
        }

        // POST: PurchaseRequests/MarkOrdered/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("PurchaseRequests", "Approve")]
        public async Task<IActionResult> MarkOrdered(int id)
        {
            var request = await _context.PurchaseRequests.FindAsync(id);
            if (request == null)
                return NotFound();

            if (request.Status != PRStatus.Approved)
            {
                TempData["Error"] = "Chỉ có thể đánh dấu Đã đặt hàng cho đề xuất đã được duyệt";
                return RedirectToAction(nameof(Details), new { id });
            }

            request.Status = PRStatus.Ordered;
            request.UpdatedAt = DateTime.Now;

            await _context.SaveChangesAsync();

            TempData["Msg"] = $"Đã đánh dấu {request.RequestNumber} là Đã đặt hàng";
            return RedirectToAction(nameof(Details), new { id });
        }

        // POST: PurchaseRequests/QuickApprove/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("PurchaseRequests", "Approve")]
        public async Task<IActionResult> QuickApprove(int id, int? notificationId = null)
        {
            var userId = GetCurrentUserId();
            var success = await _quickActionService.QuickApprovePurchaseRequestAsync(id, userId);
            
            if (notificationId.HasValue && success)
            {
                await _notificationService.MarkAsReadAsync(notificationId.Value, userId);
            }

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return Json(new { success, message = success ? "Đã duyệt thành công" : "Duyệt thất bại" });
            }

            TempData["Msg"] = success ? "Đã duyệt thành công" : "Duyệt thất bại";
            return RedirectToAction(nameof(Index));
        }

        // POST: PurchaseRequests/QuickReject/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("PurchaseRequests", "Approve")]
        public async Task<IActionResult> QuickReject(int id, string? reason = null, int? notificationId = null)
        {
            var userId = GetCurrentUserId();
            var success = await _quickActionService.QuickRejectPurchaseRequestAsync(id, userId, reason);
            
            if (notificationId.HasValue && success)
            {
                await _notificationService.MarkAsReadAsync(notificationId.Value, userId);
            }

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return Json(new { success, message = success ? "Đã từ chối thành công" : "Từ chối thất bại" });
            }

            TempData["Msg"] = success ? "Đã từ chối thành công" : "Từ chối thất bại";
            return RedirectToAction(nameof(Index));
        }

        // GET: PurchaseRequests/Delete/5
        [RequirePermission("PurchaseRequests", "Delete")]
        public async Task<IActionResult> Delete(int id)
        {
            var request = await _context.PurchaseRequests
                .Include(pr => pr.Details)
                .FirstOrDefaultAsync(pr => pr.Id == id);

            if (request == null)
                return NotFound();

            if (request.Status == PRStatus.Ordered)
            {
                TempData["Error"] = "Không thể xóa đề xuất đã đặt hàng";
                return RedirectToAction(nameof(Details), new { id });
            }

            return View(request);
        }

        // POST: PurchaseRequests/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        [RequirePermission("PurchaseRequests", "Delete")]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var request = await _context.PurchaseRequests
                .Include(pr => pr.Details)
                .FirstOrDefaultAsync(pr => pr.Id == id);

            if (request == null)
                return NotFound();

            if (request.Status == PRStatus.Ordered)
            {
                TempData["Error"] = "Không thể xóa đề xuất đã đặt hàng";
                return RedirectToAction(nameof(Index));
            }

            _context.PurchaseRequests.Remove(request);
            await _context.SaveChangesAsync();

            TempData["Msg"] = $"Đã xóa đề xuất {request.RequestNumber}";
            return RedirectToAction(nameof(Index));
        }

        // POST: PurchaseRequests/CheckLowStock
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("PurchaseRequests", "Create")]
        public async Task<IActionResult> CheckLowStock()
        {
            try
            {
                var createdIds = await _autoOrderService.CheckLowStockAndCreateRequests();
                
                if (createdIds.Any())
                {
                    TempData["Msg"] = $"Đã tạo {createdIds.Count} đề xuất đặt hàng tự động";
                    return RedirectToAction(nameof(Details), new { id = createdIds.First() });
                }
                else
                {
                    TempData["Msg"] = "Không có vật tư nào dưới mức tồn tối thiểu";
                    return RedirectToAction(nameof(Index));
                }
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Lỗi: {ex.Message}";
                return RedirectToAction(nameof(Index));
            }
        }

        private async Task PopulateDropdowns()
        {
            ViewBag.Materials = new SelectList(
                await _context.Materials.OrderBy(m => m.Code).ToListAsync(),
                "Id", "Name");

            ViewBag.Suppliers = new SelectList(
                await _context.Suppliers.OrderBy(s => s.Name).ToListAsync(),
                "Id", "Name");
        }
    }
}


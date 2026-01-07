using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using MNBEMART.Data;
using MNBEMART.Models;
using MNBEMART.ViewModels;
using MNBEMART.Services;
using MNBEMART.Filters;
using System.Security.Claims;

namespace MNBEMART.Controllers
{
    [Authorize]
    public class UsersController : Controller
    {
        private readonly AppDbContext _db;
        private readonly MNBEMART.Services.IAuditService _audit;
        private readonly IEmailService _emailService;
        private readonly IQuickActionService _quickActionService;
        private readonly INotificationService _notificationService;
        
        public UsersController(
            AppDbContext db, 
            MNBEMART.Services.IAuditService audit, 
            IEmailService emailService,
            IQuickActionService quickActionService,
            INotificationService notificationService)
        { 
            _db = db; 
            _audit = audit;
            _emailService = emailService;
            _quickActionService = quickActionService;
            _notificationService = notificationService;
        }

        // LIST
        [RequirePermission("Users", "Read")]
        public async Task<IActionResult> Index(string status = "all", bool partial = false)
        {
            var query = _db.Users
                .Include(u => u.ApprovedBy)
                .AsQueryable();

            // Filter by status
            if (status == "pending")
            {
                query = query.Where(u => !u.IsActive);
            }
            else if (status == "active")
            {
                query = query.Where(u => u.IsActive);
            }

            var users = await query.OrderByDescending(u => u.RegisteredAt).ToListAsync();
            
            // Load ViewBag cho modal
            ViewBag.Roles = RoleSelectList();
            ViewBag.Status = status;
            ViewBag.PendingCount = await _db.Users.CountAsync(u => !u.IsActive);
            
            if (partial || Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return PartialView("_UsersList", users);
            }
            
            return View(users);
        }

        // CREATE (GET)
        [HttpGet]
        [RequirePermission("Users", "Create")]
        public async Task<IActionResult> Create()
        {
            ViewBag.Roles = RoleSelectList();
            return View(new UserFormVM());
        }

        // CREATE (POST)
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Users", "Create")]
        public async Task<IActionResult> Create(UserFormVM vm)
        {
            if (!ModelState.IsValid)
            {
                TempData["Error"] = "Dữ liệu không hợp lệ.";
                return RedirectToAction("Index", "Settings", new { tab = "users" });
            }

            if (string.IsNullOrWhiteSpace(vm.Username) || string.IsNullOrWhiteSpace(vm.Password))
            {
                TempData["Error"] = "Vui lòng nhập Username và Password.";
                return RedirectToAction("Index", "Settings", new { tab = "users" });
            }

            if (await _db.Users.AnyAsync(x => x.Username == vm.Username))
            {
                TempData["Error"] = "Username đã tồn tại.";
                return RedirectToAction("Index", "Settings", new { tab = "users" });
            }

            var user = new User
            {
                FullName = vm.FullName?.Trim(),
                Username = vm.Username.Trim(),
                PasswordHash = PasswordHasher.Hash(vm.Password),
                Role = (vm.Role?.Trim().Equals("admin", StringComparison.OrdinalIgnoreCase) == true) ? "Admin" : "User",
                Email = vm.Email?.Trim(),
            };

            _db.Users.Add(user);
            await _db.SaveChangesAsync();
            await _audit.LogAsync(GetUid(), "Create", "User", user.Id.ToString(), "Người dùng", null, $"Tạo người dùng {user.Username}");
            
            // Handle AJAX request
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                var users = await _db.Users
                    .Include(u => u.ApprovedBy)
                    .AsNoTracking()
                    .OrderByDescending(u => u.RegisteredAt)
                    .ToListAsync();
                ViewBag.Roles = RoleSelectList();
                ViewBag.Status = "all";
                ViewBag.PendingCount = await _db.Users.CountAsync(u => !u.IsActive);
                ViewBag.SuccessMessage = $"Đã tạo người dùng {user.Username}.";
                return PartialView("_UsersList", users);
            }
            
            TempData["Msg"] = $"Đã tạo người dùng {user.Username}.";
            return RedirectToAction("Index", "Settings", new { tab = "users" });
        }

        // EDIT (GET)
        [HttpGet]
        [RequirePermission("Users", "Update")]
        public async Task<IActionResult> Edit(int id)
        {
            var user = await _db.Users
                .FirstOrDefaultAsync(u => u.Id == id);
            if (user == null) return NotFound();

            var vm = new UserFormVM
            {
                Id = user.Id,
                FullName = user.FullName ?? "",
                Username = user.Username,
                Role = user.Role ?? "User",
                IsActive = true, // nếu bạn có cột IsActive thì map vào
                Email = user.Email
            };
            ViewBag.Roles = RoleSelectList(vm.Role);

            // If AJAX request, return partial view for modal
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return PartialView("_EditModal", user);
            }

            return View(vm);
        }

        // EDIT (POST)
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Users", "Update")]
        public async Task<IActionResult> Edit(UserFormVM vm)
        {
            if (!ModelState.IsValid)
            {
                TempData["Error"] = "Dữ liệu không hợp lệ.";
                return RedirectToAction("Index", "Settings", new { tab = "users" });
            }

            var user = await _db.Users
                .FirstOrDefaultAsync(u => u.Id == vm.Id);
            if (user == null) return NotFound();

            // cập nhật thông tin
            user.FullName = vm.FullName?.Trim();
            user.Username = vm.Username.Trim();
            user.Email = vm.Email?.Trim();

            // đổi role
            user.Role = (vm.Role?.Trim().Equals("admin", StringComparison.OrdinalIgnoreCase) == true) ? "Admin" : "User";

            // nếu có nhập Password mới thì đổi (hash)
            if (!string.IsNullOrWhiteSpace(vm.Password))
                user.PasswordHash = PasswordHasher.Hash(vm.Password);

            await _db.SaveChangesAsync();
            await _audit.LogAsync(GetUid(), "Update", "User", vm.Id.ToString(), "Người dùng", null, $"Cập nhật {vm.Username}");
            
            // Handle AJAX request
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                var status = Request.Query["status"].ToString() ?? "all";
                var query = _db.Users
                    .Include(u => u.UserWarehouses).ThenInclude(uw => uw.Warehouse)
                    .Include(u => u.ApprovedBy)
                    .AsQueryable();
                
                if (status == "pending")
                    query = query.Where(u => !u.IsActive);
                else if (status == "active")
                    query = query.Where(u => u.IsActive);
                
                var users = await query.OrderByDescending(u => u.RegisteredAt).ToListAsync();
                await BindWarehousesAsync();
                ViewBag.Roles = RoleSelectList();
                ViewBag.Status = status;
                ViewBag.PendingCount = await _db.Users.CountAsync(u => !u.IsActive);
                ViewBag.SuccessMessage = $"Đã cập nhật người dùng {vm.Username}.";
                return PartialView("_UsersList", users);
            }
            
            TempData["Msg"] = $"Đã cập nhật người dùng {vm.Username}.";
            return RedirectToAction("Index", "Settings", new { tab = "users" });
        }

        // RESET PASSWORD (POST)
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Users", "Update")]
        public async Task<IActionResult> ResetPassword(int id, string newPassword, string status = "all")
        {
            if (string.IsNullOrWhiteSpace(newPassword))
            {
                TempData["Msg"] = "Mật khẩu mới không được trống.";
                return RedirectToAction("Index", "Settings", new { tab = "users" });
            }

            var user = await _db.Users.FindAsync(id);
            if (user == null) return NotFound();

            user.PasswordHash = PasswordHasher.Hash(newPassword);
            await _db.SaveChangesAsync();
            await _audit.LogAsync(GetUid(), "ResetPassword", "User", id.ToString(), "Người dùng", null, $"Đặt lại mật khẩu cho {user.Username}");

            // Handle AJAX request
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                status = status ?? "all";
                var query = _db.Users
                    .Include(u => u.UserWarehouses).ThenInclude(uw => uw.Warehouse)
                    .Include(u => u.ApprovedBy)
                    .AsQueryable();
                
                if (status == "pending")
                    query = query.Where(u => !u.IsActive);
                else if (status == "active")
                    query = query.Where(u => u.IsActive);
                
                var users = await query.OrderByDescending(u => u.RegisteredAt).ToListAsync();
                await BindWarehousesAsync();
                ViewBag.Roles = RoleSelectList();
                ViewBag.Status = status;
                ViewBag.PendingCount = await _db.Users.CountAsync(u => !u.IsActive);
                ViewBag.SuccessMessage = $"Đã đặt lại mật khẩu cho {user.Username}.";
                return PartialView("_UsersList", users);
            }

            TempData["Msg"] = $"Đã đặt lại mật khẩu cho {user.Username}.";
            return RedirectToAction("Index", "Settings", new { tab = "users" });
        }

        // DELETE
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Users", "Delete")]
        public async Task<IActionResult> Delete(int id, string status = "all")
        {
            var user = await _db.Users.FindAsync(id);
            if (user == null) return NotFound();

            _db.Users.Remove(user);
            await _db.SaveChangesAsync();
            await _audit.LogAsync(GetUid(), "Delete", "User", id.ToString(), "Người dùng", null, $"Xoá {user.Username}");
            
            // Handle AJAX request
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                status = status ?? "all";
                var query = _db.Users
                    .Include(u => u.UserWarehouses).ThenInclude(uw => uw.Warehouse)
                    .Include(u => u.ApprovedBy)
                    .AsQueryable();
                
                if (status == "pending")
                    query = query.Where(u => !u.IsActive);
                else if (status == "active")
                    query = query.Where(u => u.IsActive);
                
                var users = await query.OrderByDescending(u => u.RegisteredAt).ToListAsync();
                await BindWarehousesAsync();
                ViewBag.Roles = RoleSelectList();
                ViewBag.Status = status;
                ViewBag.PendingCount = await _db.Users.CountAsync(u => !u.IsActive);
                ViewBag.SuccessMessage = "Đã xoá người dùng.";
                return PartialView("_UsersList", users);
            }
            
            TempData["Msg"] = "Đã xoá người dùng.";
            return RedirectToAction("Index", "Settings", new { tab = "users" });
        }

        // APPROVE USER
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Users", "Update")]
        public async Task<IActionResult> Approve(int id, string status = "pending")
        {
            var user = await _db.Users.FindAsync(id);
            if (user == null)
            {
                TempData["Error"] = "Không tìm thấy người dùng";
                return RedirectToAction("Index", "Settings", new { tab = "users" });
            }

            if (user.IsActive)
            {
                TempData["Msg"] = "Tài khoản đã được kích hoạt trước đó";
                return RedirectToAction("Index", "Settings", new { tab = "users" });
            }

            var adminId = GetUid();
            user.IsActive = true;
            user.ApprovedById = adminId;
            user.ApprovedAt = DateTime.Now;
            user.RejectionReason = null;

            await _db.SaveChangesAsync();

            // Gửi email kích hoạt
            try
            {
                var loginUrl = $"{Request.Scheme}://{Request.Host}/Account/Login";
                await _emailService.SendAccountActivatedEmailAsync(
                    user.Email,
                    user.FullName,
                    user.Username,
                    loginUrl);
            }
            catch (Exception ex)
            {
                await _audit.LogAsync(adminId, "Error", "Email", user.Id.ToString(), "Hệ thống", null, $"Lỗi gửi email kích hoạt: {ex.Message}");
            }

            await _audit.LogAsync(adminId, "Approve", "User", user.Id.ToString(), "Người dùng", null, $"Duyệt tài khoản {user.Username}");
            
            // Handle AJAX request
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                status = status ?? "pending";
                var query = _db.Users
                    .Include(u => u.UserWarehouses).ThenInclude(uw => uw.Warehouse)
                    .Include(u => u.ApprovedBy)
                    .AsQueryable();
                
                if (status == "pending")
                    query = query.Where(u => !u.IsActive);
                else if (status == "active")
                    query = query.Where(u => u.IsActive);
                
                var users = await query.OrderByDescending(u => u.RegisteredAt).ToListAsync();
                await BindWarehousesAsync();
                ViewBag.Roles = RoleSelectList();
                ViewBag.Status = status;
                ViewBag.PendingCount = await _db.Users.CountAsync(u => !u.IsActive);
                ViewBag.SuccessMessage = $"Đã kích hoạt tài khoản {user.Username}. Email đã được gửi đến {user.Email}";
                return PartialView("_UsersList", users);
            }
            
            TempData["Msg"] = $"Đã kích hoạt tài khoản {user.Username}. Email đã được gửi đến {user.Email}";
            return RedirectToAction("Index", "Settings", new { tab = "users" });
        }

        // REJECT USER
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Users", "Update")]
        public async Task<IActionResult> Reject(int id, string? reason, string status = "pending")
        {
            var user = await _db.Users.FindAsync(id);
            if (user == null)
            {
                TempData["Error"] = "Không tìm thấy người dùng";
                return RedirectToAction("Index", "Settings", new { tab = "users" });
            }

            if (user.IsActive)
            {
                TempData["Error"] = "Không thể từ chối tài khoản đã được kích hoạt";
                return RedirectToAction("Index", "Settings", new { tab = "users" });
            }

            var adminId = GetUid();
            user.RejectionReason = reason;
            // Giữ IsActive = false

            await _db.SaveChangesAsync();

            // Gửi email từ chối
            try
            {
                await _emailService.SendAccountRejectedEmailAsync(
                    user.Email,
                    user.FullName,
                    reason ?? "Không đáp ứng yêu cầu");
            }
            catch (Exception ex)
            {
                await _audit.LogAsync(adminId, "Error", "Email", user.Id.ToString(), "Hệ thống", null, $"Lỗi gửi email từ chối: {ex.Message}");
            }

            await _audit.LogAsync(adminId, "Reject", "User", user.Id.ToString(), "Người dùng", null, $"Từ chối tài khoản {user.Username}");
            
            // Handle AJAX request
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                status = status ?? "pending";
                var query = _db.Users
                    .Include(u => u.UserWarehouses).ThenInclude(uw => uw.Warehouse)
                    .Include(u => u.ApprovedBy)
                    .AsQueryable();
                
                if (status == "pending")
                    query = query.Where(u => !u.IsActive);
                else if (status == "active")
                    query = query.Where(u => u.IsActive);
                
                var users = await query.OrderByDescending(u => u.RegisteredAt).ToListAsync();
                await BindWarehousesAsync();
                ViewBag.Roles = RoleSelectList();
                ViewBag.Status = status;
                ViewBag.PendingCount = await _db.Users.CountAsync(u => !u.IsActive);
                ViewBag.SuccessMessage = $"Đã từ chối tài khoản {user.Username}";
                return PartialView("_UsersList", users);
            }
            
            TempData["Msg"] = $"Đã từ chối tài khoản {user.Username}";
            return RedirectToAction("Index", "Settings", new { tab = "users" });
        }

        // POST: Users/QuickApprove/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Users", "Update")]
        public async Task<IActionResult> QuickApprove(int id, int? notificationId = null)
        {
            var adminId = GetUid();
            var success = await _quickActionService.QuickApproveUserAsync(id, adminId);
            
            if (notificationId.HasValue && success)
            {
                await _notificationService.MarkAsReadAsync(notificationId.Value, adminId);
            }

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return Json(new { success, message = success ? "Đã duyệt thành công" : "Duyệt thất bại" });
            }

            TempData["Msg"] = success ? "Đã duyệt thành công" : "Duyệt thất bại";
            return RedirectToAction("Index", "Settings", new { tab = "users" });
        }

        // POST: Users/QuickReject/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Users", "Update")]
        public async Task<IActionResult> QuickReject(int id, string? reason = null, int? notificationId = null)
        {
            var adminId = GetUid();
            var success = await _quickActionService.QuickRejectUserAsync(id, adminId, reason);
            
            if (notificationId.HasValue && success)
            {
                await _notificationService.MarkAsReadAsync(notificationId.Value, adminId);
            }

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return Json(new { success, message = success ? "Đã từ chối thành công" : "Từ chối thất bại" });
            }

            TempData["Msg"] = success ? "Đã từ chối thành công" : "Từ chối thất bại";
            return RedirectToAction("Index", "Settings", new { tab = "users" });
        }

        private int GetUid() => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

        // helper: bind warehouses
        private async Task BindWarehousesAsync(IEnumerable<int>? selected = null)
        {
            var items = await _db.Warehouses.AsNoTracking()
                .Select(w => new { w.Id, Name = w.Name + " - " + w.Address })
                .ToListAsync();

            ViewBag.WarehouseList = new MultiSelectList(items, "Id", "Name", selected);
        }

        // helper: role select list
        private SelectList RoleSelectList(string? selected = null)
        {
            var roles = new[]
            {
                new { Value = "User",  Text = "User"  },
                new { Value = "Admin", Text = "Admin" }
            };
            return new SelectList(roles, "Value", "Text", selected ?? "User");
        }
    }
}

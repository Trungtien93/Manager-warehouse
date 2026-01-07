using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MNBEMART.Data;
using MNBEMART.Models;
using MNBEMART.Services;
using Microsoft.AspNetCore.Hosting;

namespace MNBEMART.Controllers
{
    [Authorize]
    public class ProfileController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _env;

        public ProfileController(AppDbContext context, IWebHostEnvironment env)
        {
            _context = context;
            _env = env;
        }

        // GET: /Profile
        
        public async Task<IActionResult> Index()
        {
            Console.WriteLine($"[DEBUG] User.Identity.Name = {User.Identity?.Name}");
            var username = User.Identity?.Name;
            if (string.IsNullOrEmpty(username))
                return RedirectToAction("Login", "Account");

            var user = await _context.Users
                .Include(u => u.UserWarehouses).ThenInclude(uw => uw.Warehouse)
                .FirstOrDefaultAsync(u => u.Username == username);

            if (user == null)
                return NotFound();

            return View(user);
        }

        // POST: /Profile/Update
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Update(int id, string fullName, string? currentPassword, string? newPassword, IFormFile? avatarFile)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound();

            user.FullName = fullName;

            // Upload avatar
            if (avatarFile != null && avatarFile.Length > 0)
            {
                // Validate file type
                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
                var ext = Path.GetExtension(avatarFile.FileName).ToLowerInvariant();
                if (!allowedExtensions.Contains(ext))
                {
                    TempData["Error"] = "Chỉ chấp nhận file ảnh (JPG, PNG, GIF, WEBP).";
                    return RedirectToAction(nameof(Index));
                }

                // Validate file size (max 5MB)
                if (avatarFile.Length > 5 * 1024 * 1024)
                {
                    TempData["Error"] = "Kích thước file không được vượt quá 5MB.";
                    return RedirectToAction(nameof(Index));
                }

                // Create uploads/avatars directory
                var uploadsRoot = Path.Combine(_env.WebRootPath ?? "", "uploads", "avatars");
                if (!Directory.Exists(uploadsRoot))
                    Directory.CreateDirectory(uploadsRoot);

                // Delete old avatar if exists
                if (!string.IsNullOrEmpty(user.AvatarUrl) && user.AvatarUrl.StartsWith("/uploads/avatars/"))
                {
                    var oldPath = Path.Combine(_env.WebRootPath ?? "", user.AvatarUrl.TrimStart('/'));
                    if (System.IO.File.Exists(oldPath))
                    {
                        try { System.IO.File.Delete(oldPath); } catch { }
                    }
                }

                // Generate unique filename
                var finalFileName = $"avatar_{user.Id}_{DateTime.UtcNow.Ticks}{ext}";
                var savePath = Path.Combine(uploadsRoot, finalFileName);

                // Save file
                using var stream = new FileStream(savePath, FileMode.Create);
                await avatarFile.CopyToAsync(stream);

                user.AvatarUrl = $"/uploads/avatars/{finalFileName}";
            }

            // Nếu nhập mật khẩu → kiểm tra & đổi (PBKDF2, hỗ trợ legacy plain)
            if (!string.IsNullOrEmpty(currentPassword) && !string.IsNullOrEmpty(newPassword))
            {
                var ok = PasswordHasher.Verify(currentPassword, user.PasswordHash, out var legacyPlain);
                if (!ok)
                {
                    TempData["Error"] = "Mật khẩu hiện tại không đúng.";
                    return RedirectToAction(nameof(Index));
                }

                // Rehash nếu là legacy
                if (legacyPlain)
                {
                    user.PasswordHash = PasswordHasher.Hash(currentPassword);
                }

                user.PasswordHash = PasswordHasher.Hash(newPassword);
                TempData["Msg"] = "Đã cập nhật mật khẩu mới.";
            }
            else
            {
                TempData["Msg"] = "Đã lưu thay đổi hồ sơ.";
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }
    }
}

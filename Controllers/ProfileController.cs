using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MNBEMART.Data;
using MNBEMART.Models;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace MNBEMART.Controllers
{

    public class ProfileController : Controller
    {
        private readonly AppDbContext _context;

        public ProfileController(AppDbContext context)
        {
            _context = context;
        }

        // GET: /Profile
        public async Task<IActionResult> Index()
        {
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
        public async Task<IActionResult> Update(int id, string fullName, string? currentPassword, string? newPassword)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound();

            // Cập nhật họ tên
            user.FullName = fullName;

            // Đổi mật khẩu nếu có
            if (!string.IsNullOrEmpty(currentPassword) && !string.IsNullOrEmpty(newPassword))
            {
                string currentHash = HashPassword(currentPassword);
                if (currentHash != user.PasswordHash)
                {
                    TempData["Error"] = "Mật khẩu hiện tại không đúng.";
                    return RedirectToAction(nameof(Index));
                }

                user.PasswordHash = HashPassword(newPassword);
                TempData["Msg"] = "Đã cập nhật mật khẩu mới.";
            }
            else
            {
                TempData["Msg"] = "Đã lưu thay đổi hồ sơ.";
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private string HashPassword(string password)
        {
            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
            return Convert.ToBase64String(bytes);
        }
    }
}

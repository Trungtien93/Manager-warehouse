using Microsoft.AspNetCore.Mvc;
using MNBEMART.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Security.Claims;
using MNBEMART.Data;

namespace MNBEMART.Controllers
{
    public class AccountController : Controller
    {
        private readonly AppDbContext _context;

        public AccountController(AppDbContext context)
        {
            _context = context;
        }

        public IActionResult Login() => View();

        [HttpPost]
        public async Task<IActionResult> Login(string username, string password)
        {
            var user = _context.Users.FirstOrDefault(u => u.Username == username && u.PasswordHash == password);
            if (user == null)
            {
                ViewBag.Error = "Sai thông tin đăng nhập";
                return View();
            }

            var role = string.IsNullOrWhiteSpace(user.Role) ? "User" : user.Role.Trim();

            if (role.Equals("admin", StringComparison.OrdinalIgnoreCase)) role = "admin";
            if (role.Equals("user",  StringComparison.OrdinalIgnoreCase)) role = "user";
            var claims = new List<Claim>
        {
            // new Claim(ClaimTypes.Name, user.Username),
             //new Claim(ClaimTypes.Role, user.Role),
            // new Claim("UserId", user.Id.ToString())
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),          // <-- Id chuẩn
            new Claim(ClaimTypes.Name, user.FullName ?? user.Username),        // tên hiển thị
            //new Claim(ClaimTypes.Role, string.IsNullOrEmpty(user.Role) ? "User" : user.Role)
            new Claim(ClaimTypes.Role, role)
            
            
        };

            var identity = new ClaimsIdentity(claims, "CookieAuthenticationDefaults.AuthenticationScheme);");
            var principal = new ClaimsPrincipal(identity);

            await HttpContext.SignInAsync("Cookies", principal,new AuthenticationProperties { IsPersistent = false });
           
            return RedirectToAction("Index", "Home");
        }

        public IActionResult Register() => View();

        [HttpPost]
        public IActionResult Register(string fullname,string username, string email, string password)
        {
            if (_context.Users.Any(u => u.Username == username))
            {
                ViewBag.Error = "Tài khoản đã tồn tại!";
                return View();
            }

            var user = new User
            {
                FullName = fullname, // Assuming FullName is the same as Username for simplicity
                Username = username,
                // Email = email,
                PasswordHash = password,
                Role = "User"
            };
            _context.Users.Add(user);
            _context.SaveChanges();

            return RedirectToAction("Login");
        }

        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync("CookieAuthenticationDefaults.AuthenticationScheme);");
            return RedirectToAction("Login");
        }

        public IActionResult AccessDenied() => Content("Không có quyền truy cập.");

    }
}

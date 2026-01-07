using Microsoft.AspNetCore.Mvc;
using MNBEMART.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Security.Claims;
using MNBEMART.Data;
using MNBEMART.Services;

namespace MNBEMART.Controllers
{
    public class AccountController : Controller
    {
        private readonly AppDbContext _context;
        private readonly MNBEMART.Services.IAuditService _audit;
        private readonly IEmailService _emailService;
        private readonly INotificationService _notificationService;
        
        public AccountController(
            AppDbContext context, 
            MNBEMART.Services.IAuditService audit,
            IEmailService emailService,
            INotificationService notificationService)
        { 
            _context = context; 
            _audit = audit;
            _emailService = emailService;
            _notificationService = notificationService;
        }

        public IActionResult Login() => View();

        [HttpPost]
        public async Task<IActionResult> Login(string username, string password, bool rememberMe = false)
        {
            // Tra cứu theo Username, sau đó verify mật khẩu (hỗ trợ legacy plain)
            var user = _context.Users.FirstOrDefault(u => u.Username == username);
            if (user == null)
            {
                ViewBag.Error = "Sai thông tin đăng nhập";
                return View();
            }

            // Check if account is locked
            if (user.LockedUntil.HasValue && user.LockedUntil.Value > DateTime.Now)
            {
                var minutesLeft = (int)(user.LockedUntil.Value - DateTime.Now).TotalMinutes;
                ViewBag.Error = $"Tài khoản đã bị khóa. Vui lòng thử lại sau {minutesLeft} phút.";
                return View();
            }

            // Check if account is active
            if (!user.IsActive)
            {
                ViewBag.Error = "Tài khoản chưa được kích hoạt. Vui lòng chờ admin duyệt hoặc liên hệ admin.";
                return View();
            }

            var ok = PasswordHasher.Verify(password, user.PasswordHash, out var legacyPlain);
            if (!ok)
            {
                // Tăng số lần đăng nhập sai
                user.FailedLoginAttempts++;
                if (user.FailedLoginAttempts >= 5)
                {
                    user.LockedUntil = DateTime.Now.AddMinutes(15);
                    user.FailedLoginAttempts = 0;
                    ViewBag.Error = "Đăng nhập sai quá nhiều lần. Tài khoản đã bị khóa 15 phút.";
                }
                else
                {
                    ViewBag.Error = $"Sai thông tin đăng nhập. Còn {5 - user.FailedLoginAttempts} lần thử.";
                }
                _context.SaveChanges();
                return View();
            }

            // Reset failed attempts on successful login
            user.FailedLoginAttempts = 0;
            user.LockedUntil = null;
            user.LastLoginAt = DateTime.Now;
            user.LastLoginIp = HttpContext.Connection.RemoteIpAddress?.ToString();

            // Nếu đang dùng plain-text, nâng cấp sang PBKDF2 ngay khi đăng nhập thành công
            if (legacyPlain)
            {
                user.PasswordHash = PasswordHasher.Hash(password);
                _context.SaveChanges();
            }

            // Chuẩn hoá role
            var role = (user.Role ?? "User").Trim();
            role = role.Equals("admin", StringComparison.OrdinalIgnoreCase) ? "Admin" : "User";

            // Claim chuẩn (Name = Username)
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Role, role)
            };

            var identity = new ClaimsIdentity(
                claims, CookieAuthenticationDefaults.AuthenticationScheme);

            var principal = new ClaimsPrincipal(identity);

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal,
                new AuthenticationProperties { IsPersistent = rememberMe });

            // Ghi log đăng nhập
            await _audit.LogAsync(user.Id, "Login", "Account", user.Id.ToString(), "Hệ thống", null, $"Đăng nhập bởi {user.Username}");

            return RedirectToAction("Dashboard", "Home");
        }

        public IActionResult Register() => View();

        [HttpPost]
        public async Task<IActionResult> Register(string fullname, string username, string email, string password, string confirmPassword)
        {
            // Validation
            if (string.IsNullOrWhiteSpace(fullname))
            {
                ViewBag.Error = "Họ tên không được để trống";
                return View();
            }

            if (string.IsNullOrWhiteSpace(username))
            {
                ViewBag.Error = "Tên đăng nhập không được để trống";
                return View();
            }

            if (string.IsNullOrWhiteSpace(email) || !email.Contains("@"))
            {
                ViewBag.Error = "Email không hợp lệ";
                return View();
            }

            if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
            {
                ViewBag.Error = "Mật khẩu phải có ít nhất 8 ký tự";
                return View();
            }

            if (password != confirmPassword)
            {
                ViewBag.Error = "Mật khẩu xác nhận không khớp";
                return View();
            }

            // Check username unique
            if (_context.Users.Any(u => u.Username == username))
            {
                ViewBag.Error = "Tên đăng nhập đã tồn tại!";
                return View();
            }

            // Check email unique
            if (_context.Users.Any(u => u.Email == email))
            {
                ViewBag.Error = "Email đã được sử dụng!";
                return View();
            }

            // Password strength check
            if (!IsPasswordStrong(password))
            {
                ViewBag.Error = "Mật khẩu phải có ít nhất 8 ký tự, bao gồm chữ hoa, chữ thường và số";
                return View();
            }

            // Create user with IsActive = false (chờ duyệt)
            var user = new User
            {
                FullName = fullname,
                Username = username,
                Email = email,
                PasswordHash = PasswordHasher.Hash(password),
                Role = "User",
                IsActive = false,  // Chưa được duyệt
                RegisteredAt = DateTime.Now
            };

            _context.Users.Add(user);
            _context.SaveChanges();

            // Gửi thông báo cho admin
            try
            {
                await _emailService.SendRegistrationNotificationToAdminAsync(
                    user.FullName, 
                    user.Email, 
                    user.Username, 
                    user.Id);

                // Tạo notification trong hệ thống
                await _notificationService.CreateNotificationAsync(
                    NotificationType.UserRegistration,
                    user.Id,
                    "Tài khoản mới cần duyệt",
                    $"User {user.FullName} ({user.Username}) đã đăng ký và cần được duyệt",
                    null, // null = gửi cho tất cả admin
                    NotificationPriority.High);
            }
            catch (Exception ex)
            {
                // Log error nhưng không block registration
                await _audit.LogAsync(user.Id, "Error", "Email", user.Id.ToString(), "Hệ thống", null, $"Lỗi gửi email: {ex.Message}");
            }

            ViewBag.Success = "Đăng ký thành công! Tài khoản của bạn đang chờ admin duyệt. Bạn sẽ nhận email khi tài khoản được kích hoạt.";
            return View();
        }

        private bool IsPasswordStrong(string password)
        {
            if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
                return false;

            bool hasUpper = password.Any(char.IsUpper);
            bool hasLower = password.Any(char.IsLower);
            bool hasDigit = password.Any(char.IsDigit);

            return hasUpper && hasLower && hasDigit;
        }

        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login");
        }

        public IActionResult AccessDenied() => Content("Không có quyền truy cập.");

        // FORGOT PASSWORD
        public IActionResult ForgotPassword() => View();

        [HttpPost]
        public async Task<IActionResult> ForgotPassword(string email)
        {
            if (string.IsNullOrWhiteSpace(email) || !email.Contains("@"))
            {
                ViewBag.Error = "Vui lòng nhập email hợp lệ";
                return View();
            }

            var user = _context.Users.FirstOrDefault(u => u.Email == email);
            
            // Không báo lỗi cụ thể để bảo mật (không cho biết email có tồn tại hay không)
            if (user == null)
            {
                ViewBag.Success = "Nếu email tồn tại trong hệ thống, chúng tôi đã gửi link đặt lại mật khẩu đến email của bạn.";
                return View();
            }

            // Generate reset token
            var token = Guid.NewGuid().ToString() + Guid.NewGuid().ToString();
            user.PasswordResetToken = token;
            user.PasswordResetTokenExpiry = DateTime.Now.AddHours(24);

            _context.SaveChanges();

            // Gửi email reset password
            try
            {
                var resetUrl = $"{Request.Scheme}://{Request.Host}/Account/ResetPassword?token={token}&email={email}";
                await _emailService.SendPasswordResetEmailAsync(
                    user.Email,
                    user.FullName,
                    token,
                    resetUrl);
            }
            catch (Exception ex)
            {
                await _audit.LogAsync(user.Id, "Error", "Email", user.Id.ToString(), "Hệ thống", null, $"Lỗi gửi email reset password: {ex.Message}");
            }

            ViewBag.Success = "Nếu email tồn tại trong hệ thống, chúng tôi đã gửi link đặt lại mật khẩu đến email của bạn.";
            return View();
        }

        // RESET PASSWORD
        public IActionResult ResetPassword(string token, string email)
        {
            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(email))
            {
                ViewBag.Error = "Link không hợp lệ";
                return View();
            }

            var user = _context.Users.FirstOrDefault(u => 
                u.Email == email && 
                u.PasswordResetToken == token &&
                u.PasswordResetTokenExpiry.HasValue &&
                u.PasswordResetTokenExpiry.Value > DateTime.Now);

            if (user == null)
            {
                ViewBag.Error = "Link không hợp lệ hoặc đã hết hạn. Vui lòng yêu cầu link mới.";
                return View();
            }

            ViewBag.Token = token;
            ViewBag.Email = email;
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> ResetPassword(string token, string email, string password, string confirmPassword)
        {
            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(email))
            {
                ViewBag.Error = "Link không hợp lệ";
                return View();
            }

            if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
            {
                ViewBag.Error = "Mật khẩu phải có ít nhất 8 ký tự";
                ViewBag.Token = token;
                ViewBag.Email = email;
                return View();
            }

            if (password != confirmPassword)
            {
                ViewBag.Error = "Mật khẩu xác nhận không khớp";
                ViewBag.Token = token;
                ViewBag.Email = email;
                return View();
            }

            if (!IsPasswordStrong(password))
            {
                ViewBag.Error = "Mật khẩu phải có ít nhất 8 ký tự, bao gồm chữ hoa, chữ thường và số";
                ViewBag.Token = token;
                ViewBag.Email = email;
                return View();
            }

            var user = _context.Users.FirstOrDefault(u => 
                u.Email == email && 
                u.PasswordResetToken == token &&
                u.PasswordResetTokenExpiry.HasValue &&
                u.PasswordResetTokenExpiry.Value > DateTime.Now);

            if (user == null)
            {
                ViewBag.Error = "Link không hợp lệ hoặc đã hết hạn. Vui lòng yêu cầu link mới.";
                return View();
            }

            // Update password
            user.PasswordHash = PasswordHasher.Hash(password);
            user.PasswordResetToken = null;
            user.PasswordResetTokenExpiry = null;
            user.FailedLoginAttempts = 0;
            user.LockedUntil = null;

            _context.SaveChanges();

            // Gửi email xác nhận
            try
            {
                await _emailService.SendPasswordChangedConfirmationEmailAsync(user.Email, user.FullName);
            }
            catch (Exception ex)
            {
                await _audit.LogAsync(user.Id, "Error", "Email", user.Id.ToString(), "Hệ thống", null, $"Lỗi gửi email xác nhận: {ex.Message}");
            }

            await _audit.LogAsync(user.Id, "ResetPassword", "Account", user.Id.ToString(), "Hệ thống", null, $"Đặt lại mật khẩu thành công");

            TempData["Msg"] = "Đặt lại mật khẩu thành công! Bạn có thể đăng nhập ngay bây giờ.";
            return RedirectToAction("Login");
        }
    }
}

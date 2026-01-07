using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MNBEMART.Data;
using System.Security.Claims;

namespace MNBEMART.ViewComponents
{
    public class UserAvatarViewComponent : ViewComponent
    {
        private readonly AppDbContext _db;

        public UserAvatarViewComponent(AppDbContext db)
        {
            _db = db;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            var username = HttpContext.User?.Identity?.Name;
            if (string.IsNullOrEmpty(username))
            {
                ViewBag.DisplayName = "Khách";
                return View("/Views/Shared/Components/UserAvatar/Default.cshtml", "/uploads/avarta.png");
            }

            var user = await _db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Username == username);

            var avatarUrl = user?.AvatarUrl ?? "/uploads/avarta.png";
            ViewBag.DisplayName = !string.IsNullOrEmpty(user?.FullName) ? user.FullName : username;
            return View("/Views/Shared/Components/UserAvatar/Default.cshtml", avatarUrl);
        }
    }
}


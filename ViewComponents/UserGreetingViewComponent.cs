using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MNBEMART.Data;

namespace MNBEMART.ViewComponents
{
    public class UserGreetingViewComponent : ViewComponent
    {
        private readonly AppDbContext _db;

        public UserGreetingViewComponent(AppDbContext db)
        {
            _db = db;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            var username = HttpContext.User?.Identity?.Name;
            if (string.IsNullOrEmpty(username))
            {
                ViewBag.DisplayName = "Khách";
                return View("/Views/Shared/Components/UserGreeting/Default.cshtml");
            }

            var user = await _db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Username == username);

            ViewBag.DisplayName = !string.IsNullOrEmpty(user?.FullName) ? user.FullName : username;
            return View("/Views/Shared/Components/UserGreeting/Default.cshtml");
        }
    }
}





















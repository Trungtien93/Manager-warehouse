using Microsoft.AspNetCore.Mvc;
using MNBEMART.Services;
using System.Security.Claims;

namespace MNBEMART.ViewComponents
{
    public class NotificationBadgeViewComponent : ViewComponent
    {
        private readonly INotificationService _notificationService;

        public NotificationBadgeViewComponent(INotificationService notificationService)
        {
            _notificationService = notificationService;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            int? userId = null;
            if (User.Identity?.IsAuthenticated == true && User is ClaimsPrincipal claimsPrincipal)
            {
                var userIdClaim = claimsPrincipal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (int.TryParse(userIdClaim, out int parsedUserId))
                {
                    userId = parsedUserId;
                }
            }

            var unreadCount = await _notificationService.GetUnreadCountAsync(userId);
            return View(unreadCount);
        }
    }
}


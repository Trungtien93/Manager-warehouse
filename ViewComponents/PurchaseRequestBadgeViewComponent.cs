using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MNBEMART.Data;
using MNBEMART.Models;

namespace MNBEMART.ViewComponents
{
    public class PurchaseRequestBadgeViewComponent : ViewComponent
    {
        private readonly AppDbContext _context;

        public PurchaseRequestBadgeViewComponent(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            var pendingCount = await _context.PurchaseRequests
                .Where(pr => pr.Status == PRStatus.Pending)
                .CountAsync();

            return View(pendingCount);
        }
    }
}























































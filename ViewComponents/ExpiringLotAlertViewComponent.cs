using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MNBEMART.Data;

namespace MNBEMART.ViewComponents
{
    public class ExpiringLotAlertViewComponent : ViewComponent
    {
        private readonly AppDbContext _context;

        public ExpiringLotAlertViewComponent(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            var today = DateTime.Today;
            var warningDate = today.AddDays(30);

            var stats = new
            {
                Expired = await _context.StockLots
                    .Where(l => l.Quantity > 0 && l.ExpiryDate != null && l.ExpiryDate < today)
                    .CountAsync(),
                ExpiringSoon = await _context.StockLots
                    .Where(l => l.Quantity > 0 && l.ExpiryDate != null && 
                           l.ExpiryDate >= today && l.ExpiryDate <= warningDate)
                    .CountAsync()
            };

            return View(stats);
        }
    }
}





































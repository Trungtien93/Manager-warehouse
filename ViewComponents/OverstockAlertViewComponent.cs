using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MNBEMART.Data;

namespace MNBEMART.ViewComponents
{
    public class OverstockAlertViewComponent : ViewComponent
    {
        private readonly AppDbContext _context;

        public OverstockAlertViewComponent(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            var overstockCount = await _context.Stocks
                .Include(s => s.Material)
                .Where(s => s.Material != null && 
                       s.Material.MaximumStock.HasValue && 
                       s.Quantity > s.Material.MaximumStock.Value)
                .CountAsync();

            return View(overstockCount);
        }
    }
}





































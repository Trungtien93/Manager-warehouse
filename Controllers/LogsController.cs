using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Rendering;
using MNBEMART.Data;
using MNBEMART.Filters;
using MNBEMART.Extensions;

namespace MNBEMART.Controllers
{
    [Authorize]
    public class LogsController : Controller
    {
        private readonly AppDbContext _db;
        public LogsController(AppDbContext db) { _db = db; }

        public class LogFilter
        {
            public DateTime? From { get; set; }
            public DateTime? To { get; set; }
            public int? WarehouseId { get; set; }
            public int? UserId { get; set; }
            public string? Module { get; set; }
            public string? q { get; set; }
        }

        [HttpGet]
        [RequirePermission("Logs", "Read")]
        public async Task<IActionResult> Index([FromQuery] LogFilter f, int page = 1, int pageSize = 100, bool partial = false)
        {
            var from = f.From?.Date ?? DateTime.Now.Date.AddDays(-7);
            var to   = f.To?.Date   ?? DateTime.Now.Date;

            var q = _db.AuditLogs.AsNoTracking()
                .Include(x => x.User)
                .Include(x => x.Warehouse)
                .Where(x => x.Timestamp.Date >= from && x.Timestamp.Date <= to);

            if (f.WarehouseId.HasValue) q = q.Where(x => x.WarehouseId == f.WarehouseId);
            if (f.UserId.HasValue) q = q.Where(x => x.UserId == f.UserId);
            if (!string.IsNullOrWhiteSpace(f.Module)) q = q.Where(x => x.Module == f.Module);
            if (!string.IsNullOrWhiteSpace(f.q))
                q = q.Where(x => (x.Content ?? "").Contains(f.q) || x.Action.Contains(f.q) || x.ObjectType.Contains(f.q));

            var pagedResult = await q
                .OrderByDescending(x => x.Timestamp)
                .ToPagedResultAsync(page, pageSize);

            ViewBag.Users = new SelectList(await _db.Users.AsNoTracking().OrderBy(u => u.Username).ToListAsync(), "Id", "Username", f.UserId);
            ViewBag.Warehouses = new SelectList(await _db.Warehouses.AsNoTracking().OrderBy(w => w.Name).ToListAsync(), "Id", "Name", f.WarehouseId);
            ViewBag.Modules = new SelectList((await _db.AuditLogs.AsNoTracking().Select(x => x.Module).Distinct().ToListAsync()).Where(m => !string.IsNullOrWhiteSpace(m)));
            ViewBag.Filter = f;
            ViewBag.Page = pagedResult.Page;
            ViewBag.PageSize = pagedResult.PageSize;
            ViewBag.TotalItems = pagedResult.TotalItems;
            ViewBag.TotalPages = pagedResult.TotalPages;
            ViewBag.PagedResult = pagedResult;
            
            if (partial || Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return PartialView("_LogsList", pagedResult.Items.ToList());
            }
            
            return View(pagedResult.Items.ToList());
        }
    }
}
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MNBEMART.Data;
using MNBEMART.Models;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.AspNetCore.Authorization;

using MNBEMART.Filters;
using MNBEMART.Extensions;
using MNBEMART.Services;

namespace MNBEMART.Controllers
{
    [Authorize]
    public class WarehousesController : Controller
    {
        private readonly AppDbContext _db;
        private readonly INotificationService _notificationService;
        
        public WarehousesController(AppDbContext db, INotificationService notificationService)
        {
            _db = db;
            _notificationService = notificationService;
        }

        // Helper method to build view model with statistics
        private async Task<WarehouseIndexVM> BuildWarehouseIndexVM(string? q, int page, int pageSize)
        {
            var baseQ = _db.Warehouses.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(q))
            {
                q = q.Trim();
                baseQ = baseQ.Where(w =>
                    EF.Functions.Like(w.Name, $"%{q}%") ||
                    EF.Functions.Like(w.Address ?? "", $"%{q}%"));
            }

            var pagedResult = await baseQ
                .OrderBy(w => w.Name)
                .ToPagedResultAsync(page, pageSize);

            var pageWarehouses = pagedResult.Items.ToList();
            var total = pagedResult.TotalItems;

            // Lấy thống kê tồn theo kho (dựa vào bảng Stocks)
            var wids = pageWarehouses.Select(w => w.Id).ToList();

            var statRows = await _db.Stocks.AsNoTracking()
                .Where(s => wids.Contains(s.WarehouseId))
                .GroupBy(s => s.WarehouseId)
                .Select(g => new
                {
                    WarehouseId = g.Key,
                    DistinctMaterials = g.Select(x => x.MaterialId).Distinct().Count(),
                    TotalQty = g.Sum(x => (decimal?)x.Quantity) ?? 0m
                })
                .ToListAsync();

            var statMap = statRows.ToDictionary(x => x.WarehouseId, x => x);

            var items = new List<WarehouseRowVM>();
            foreach (var w in pageWarehouses)
            {
                statMap.TryGetValue(w.Id, out var st);
                items.Add(new WarehouseRowVM
                {
                    W = w,
                    DistinctMaterials = st?.DistinctMaterials ?? 0,
                    TotalQty = st?.TotalQty ?? 0m
                });
            }

            return new WarehouseIndexVM
            {
                Items = items,
                Q = q,
                Page = pagedResult.Page,
                PageSize = pagedResult.PageSize,
                TotalItems = pagedResult.TotalItems
            };
        }

        // GET: Warehouses
        [RequirePermission("Warehouses", "Read")]
        public async Task<IActionResult> Index(string? q, int page = 1, int pageSize = 30, bool partial = false)
        {
            var vm = await BuildWarehouseIndexVM(q, page, pageSize);

            // Handle AJAX request
            if (partial || Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return PartialView("_WarehousesList", vm);
            }

            return View(vm);
        }

        // GET: Warehouses/Create
        [RequirePermission("Warehouses", "Create")]
        public IActionResult Create() => View(new Warehouse());

        // POST: Warehouses/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Warehouses", "Create")]
        public async Task<IActionResult> Create(Warehouse input)
        {
            input.Name = (input.Name ?? "").Trim();
            if (string.IsNullOrWhiteSpace(input.Name))
                ModelState.AddModelError(nameof(Warehouse.Name), "Tên kho là bắt buộc.");

            bool exists = await _db.Warehouses.AnyAsync(w => w.Name == input.Name);
            if (exists)
                ModelState.AddModelError(nameof(Warehouse.Name), "Tên kho đã tồn tại.");

            if (!ModelState.IsValid)
            {
                // Handle AJAX request - return error in modal
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    return Json(new { success = false, errors = ModelState.Where(x => x.Value.Errors.Count > 0).ToDictionary(k => k.Key, v => v.Value.Errors.Select(e => e.ErrorMessage).ToArray()) });
                }
                return View(input);
            }

            _db.Warehouses.Add(input);
            await _db.SaveChangesAsync();

            // Thông báo khi tạo kho mới
            await _notificationService.CreateNotificationAsync(
                NotificationType.WarehouseCreated,
                input.Id,
                $"Kho mới được tạo: {input.Name}",
                $"Kho {input.Name} đã được thêm vào hệ thống",
                null,
                NotificationPriority.Normal);

            // Handle AJAX request
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                var vm = await BuildWarehouseIndexVM(null, 1, 30);
                ViewBag.SuccessMessage = "Đã thêm kho.";
                return PartialView("_WarehousesList", vm);
            }

            TempData["Message"] = "Đã thêm kho.";
            return RedirectToAction(nameof(Index));
        }

        // GET: Warehouses/Edit/5
        [RequirePermission("Warehouses", "Update")]
        public async Task<IActionResult> Edit(int id)
        {
            var w = await _db.Warehouses.FindAsync(id);
            if (w == null) return NotFound();
            return View(w);
        }

        // POST: Warehouses/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Warehouses", "Update")]
        public async Task<IActionResult> Edit(int id, Warehouse input)
        {
            if (id != input.Id) return NotFound();

            input.Name = (input.Name ?? "").Trim();
            if (string.IsNullOrWhiteSpace(input.Name))
                ModelState.AddModelError(nameof(Warehouse.Name), "Tên kho là bắt buộc.");

            bool exists = await _db.Warehouses.AnyAsync(w => w.Id != id && w.Name == input.Name);
            if (exists)
                ModelState.AddModelError(nameof(Warehouse.Name), "Tên kho đã tồn tại.");

            if (!ModelState.IsValid)
            {
                // Handle AJAX request - return error in modal
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    return Json(new { success = false, errors = ModelState.Where(x => x.Value.Errors.Count > 0).ToDictionary(k => k.Key, v => v.Value.Errors.Select(e => e.ErrorMessage).ToArray()) });
                }
                return View(input);
            }

            var w = await _db.Warehouses.FirstOrDefaultAsync(x => x.Id == id);
            if (w == null) return NotFound();

            w.Name = input.Name;
            w.Address = input.Address;

            await _db.SaveChangesAsync();

            // Handle AJAX request
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                var vm = await BuildWarehouseIndexVM(null, 1, 30);
                ViewBag.SuccessMessage = "Đã cập nhật kho.";
                return PartialView("_WarehousesList", vm);
            }

            TempData["Message"] = "Đã cập nhật kho.";
            return RedirectToAction(nameof(Index));
        }

        // GET: Warehouses/Delete/5
        [RequirePermission("Warehouses", "Delete")]
        public async Task<IActionResult> Delete(int id)
        {
            var w = await _db.Warehouses
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id);

            if (w == null) return NotFound();
            return View(w);
        }

        // POST: Warehouses/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        [RequirePermission("Warehouses", "Delete")]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var w = await _db.Warehouses.FirstOrDefaultAsync(x => x.Id == id);
            if (w == null) return NotFound();

            // Chặn xoá nếu đang được tham chiếu (theo cấu hình OnDelete Restrict ở Receipts/Issues)
            bool hasReceipts = await _db.StockReceipts.AnyAsync(x => x.WarehouseId == id);
            bool hasIssues   = await _db.StockIssues.AnyAsync(x => x.WarehouseId == id);
            bool hasStocks   = await _db.Stocks.AnyAsync(x => x.WarehouseId == id);

            if (hasReceipts || hasIssues || hasStocks)
            {
                // Handle AJAX request
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    var vm = await BuildWarehouseIndexVM(null, 1, 30);
                    ViewBag.ErrorMessage = "Kho đang được sử dụng (phiếu nhập/xuất hoặc còn tồn). Hãy chuyển/xử lý hết trước khi xoá.";
                    return PartialView("_WarehousesList", vm);
                }
                TempData["Error"] = "Kho đang được sử dụng (phiếu nhập/xuất hoặc còn tồn). Hãy chuyển/xử lý hết trước khi xoá.";
                return RedirectToAction(nameof(Delete), new { id });
            }

            _db.Warehouses.Remove(w);
            await _db.SaveChangesAsync();

            // Handle AJAX request
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                var vm = await BuildWarehouseIndexVM(null, 1, 30);
                ViewBag.SuccessMessage = "Đã xoá kho.";
                return PartialView("_WarehousesList", vm);
            }

            TempData["Message"] = "Đã xoá kho.";
            return RedirectToAction(nameof(Index));
        }
    }
}

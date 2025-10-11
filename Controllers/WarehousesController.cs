using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MNBEMART.Data;
using MNBEMART.Models;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace MNBEMART.Controllers
{
    public class WarehousesController : Controller
    {
        private readonly AppDbContext _db;
        public WarehousesController(AppDbContext db) => _db = db;

        // GET: Warehouses
        public async Task<IActionResult> Index(string? q, int page = 1, int pageSize = 30)
        {
            var baseQ = _db.Warehouses.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(q))
            {
                q = q.Trim();
                baseQ = baseQ.Where(w =>
                    EF.Functions.Like(w.Name, $"%{q}%") ||
                    EF.Functions.Like(w.Address ?? "", $"%{q}%"));
            }

            var total = await baseQ.CountAsync();
            var pageWarehouses = await baseQ
                .OrderBy(w => w.Name)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

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

            var vm = new WarehouseIndexVM
            {
                Items = items,
                Q = q,
                Page = page,
                PageSize = pageSize,
                TotalItems = total
            };

            return View(vm);
        }

        // GET: Warehouses/Create
        public IActionResult Create() => View(new Warehouse());

        // POST: Warehouses/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Warehouse input)
        {
            input.Name = (input.Name ?? "").Trim();
            if (string.IsNullOrWhiteSpace(input.Name))
                ModelState.AddModelError(nameof(Warehouse.Name), "Tên kho là bắt buộc.");

            bool exists = await _db.Warehouses.AnyAsync(w => w.Name == input.Name);
            if (exists)
                ModelState.AddModelError(nameof(Warehouse.Name), "Tên kho đã tồn tại.");

            if (!ModelState.IsValid) return View(input);

            _db.Warehouses.Add(input);
            await _db.SaveChangesAsync();
            TempData["Message"] = "Đã thêm kho.";
            return RedirectToAction(nameof(Index));
        }

        // GET: Warehouses/Edit/5
        public async Task<IActionResult> Edit(int id)
        {
            var w = await _db.Warehouses.FindAsync(id);
            if (w == null) return NotFound();
            return View(w);
        }

        // POST: Warehouses/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Warehouse input)
        {
            if (id != input.Id) return NotFound();

            input.Name = (input.Name ?? "").Trim();
            if (string.IsNullOrWhiteSpace(input.Name))
                ModelState.AddModelError(nameof(Warehouse.Name), "Tên kho là bắt buộc.");

            bool exists = await _db.Warehouses.AnyAsync(w => w.Id != id && w.Name == input.Name);
            if (exists)
                ModelState.AddModelError(nameof(Warehouse.Name), "Tên kho đã tồn tại.");

            if (!ModelState.IsValid) return View(input);

            var w = await _db.Warehouses.FirstOrDefaultAsync(x => x.Id == id);
            if (w == null) return NotFound();

            w.Name = input.Name;
            w.Address = input.Address;

            await _db.SaveChangesAsync();
            TempData["Message"] = "Đã cập nhật kho.";
            return RedirectToAction(nameof(Index));
        }

        // GET: Warehouses/Delete/5
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
                TempData["Error"] = "Kho đang được sử dụng (phiếu nhập/xuất hoặc còn tồn). Hãy chuyển/xử lý hết trước khi xoá.";
                return RedirectToAction(nameof(Delete), new { id });
            }

            _db.Warehouses.Remove(w);
            await _db.SaveChangesAsync();

            TempData["Message"] = "Đã xoá kho.";
            return RedirectToAction(nameof(Index));
        }
    }
}

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MNBEMART.Data;
using MNBEMART.Models;
using System.Linq;
using System.Threading.Tasks;

namespace MNBEMART.Controllers
{
    public class SuppliersController : Controller
    {
        private readonly AppDbContext _db;
        public SuppliersController(AppDbContext db) => _db = db;

        // GET: Suppliers
        public async Task<IActionResult> Index(string? q, int page = 1, int pageSize = 30)
        {
            var query = _db.Suppliers
                .AsNoTracking()
                .Include(s => s.Materials)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(q))
            {
                q = q.Trim();
                query = query.Where(s =>
                    EF.Functions.Like(s.Name, $"%{q}%") ||
                    EF.Functions.Like(s.PhoneNumber ?? "", $"%{q}%") ||
                    EF.Functions.Like(s.Email ?? "", $"%{q}%") ||
                    EF.Functions.Like(s.Address ?? "", $"%{q}%"));
            }

            var total = await query.CountAsync();

            var items = await query
                .OrderBy(s => s.Name)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var vm = new SupplierIndexVM
            {
                Items = items,
                Q = q,
                Page = page,
                PageSize = pageSize,
                TotalItems = total
            };

            return View(vm);
        }

        // GET: Suppliers/Create
        public IActionResult Create() => View(new Supplier());

        // POST: Suppliers/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Supplier input)
        {
            // Chuẩn hoá nhẹ
            input.Name = (input.Name ?? "").Trim();

            if (string.IsNullOrWhiteSpace(input.Name))
                ModelState.AddModelError(nameof(Supplier.Name), "Tên nhà cung cấp là bắt buộc.");

            // Chống trùng tên (tuỳ bạn có muốn Unique không)
            bool exists = await _db.Suppliers.AnyAsync(s => s.Name == input.Name);
            if (exists)
                ModelState.AddModelError(nameof(Supplier.Name), "Tên nhà cung cấp đã tồn tại.");

            if (!ModelState.IsValid) return View(input);

            _db.Suppliers.Add(input);
            await _db.SaveChangesAsync();
            TempData["Message"] = "Đã thêm nhà cung cấp.";
            return RedirectToAction(nameof(Index));
        }

        // GET: Suppliers/Edit/5
        public async Task<IActionResult> Edit(int id)
        {
            var s = await _db.Suppliers.FindAsync(id);
            if (s == null) return NotFound();
            return View(s);
        }

        // POST: Suppliers/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Supplier input)
        {
            if (id != input.Id) return NotFound();

            input.Name = (input.Name ?? "").Trim();
            if (string.IsNullOrWhiteSpace(input.Name))
                ModelState.AddModelError(nameof(Supplier.Name), "Tên nhà cung cấp là bắt buộc.");

            bool exists = await _db.Suppliers.AnyAsync(s => s.Id != id && s.Name == input.Name);
            if (exists)
                ModelState.AddModelError(nameof(Supplier.Name), "Tên nhà cung cấp đã tồn tại.");

            if (!ModelState.IsValid) return View(input);

            var s = await _db.Suppliers.FirstOrDefaultAsync(x => x.Id == id);
            if (s == null) return NotFound();

            s.Name = input.Name;
            s.Address = input.Address;
            s.PhoneNumber = input.PhoneNumber;
            s.Email = input.Email;

            await _db.SaveChangesAsync();
            TempData["Message"] = "Đã cập nhật nhà cung cấp.";
            return RedirectToAction(nameof(Index));
        }

        // GET: Suppliers/Delete/5
        public async Task<IActionResult> Delete(int id)
        {
            var s = await _db.Suppliers
                .AsNoTracking()
                .Include(x => x.Materials)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (s == null) return NotFound();
            return View(s);
        }

        // POST: Suppliers/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var s = await _db.Suppliers.Include(x => x.Materials).FirstOrDefaultAsync(x => x.Id == id);
            if (s == null) return NotFound();

            // OnDelete bạn đang set là SetNull → xoá Supplier sẽ tự set SupplierId = null cho Materials
            _db.Suppliers.Remove(s);
            await _db.SaveChangesAsync();

            TempData["Message"] = "Đã xoá nhà cung cấp.";
            return RedirectToAction(nameof(Index));
        }
    }
}

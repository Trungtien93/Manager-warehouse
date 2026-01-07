using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MNBEMART.Data;
using MNBEMART.Models;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;

using MNBEMART.Filters;
using MNBEMART.Extensions;

namespace MNBEMART.Controllers
{
    [Authorize]
    public class SuppliersController : Controller
    {
        private readonly AppDbContext _db;
        public SuppliersController(AppDbContext db) => _db = db;

        // GET: Suppliers
        [RequirePermission("Suppliers", "Read")]
        public async Task<IActionResult> Index(string? q, int page = 1, int pageSize = 30, bool partial = false)
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

            var pagedResult = await query
                .OrderBy(s => s.Name)
                .ToPagedResultAsync(page, pageSize);

            var vm = new SupplierIndexVM
            {
                Items = pagedResult.Items,
                Q = q,
                Page = pagedResult.Page,
                PageSize = pagedResult.PageSize,
                TotalItems = pagedResult.TotalItems
            };

            // Handle AJAX request
            if (partial || Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return PartialView("_SuppliersList", vm);
            }

            return View(vm);
        }

        // GET: Suppliers/Create
        [RequirePermission("Suppliers", "Create")]
        public IActionResult Create() => View(new Supplier());

        // POST: Suppliers/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Suppliers", "Create")]
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

            if (!ModelState.IsValid)
            {
                // Handle AJAX request - return error in modal
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    return Json(new { success = false, errors = ModelState.Where(x => x.Value.Errors.Count > 0).ToDictionary(k => k.Key, v => v.Value.Errors.Select(e => e.ErrorMessage).ToArray()) });
                }
                TempData["Error"] = "Vui lòng kiểm tra lại thông tin.";
                return RedirectToAction(nameof(Index));
            }

            _db.Suppliers.Add(input);
            await _db.SaveChangesAsync();

            // Handle AJAX request
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                var query = _db.Suppliers
                    .AsNoTracking()
                    .Include(s => s.Materials)
                    .AsQueryable();

                var pagedResult = await query
                    .OrderBy(s => s.Name)
                    .ToPagedResultAsync(1, 30);

                var vm = new SupplierIndexVM
                {
                    Items = pagedResult.Items,
                    Q = null,
                    Page = pagedResult.Page,
                    PageSize = pagedResult.PageSize,
                    TotalItems = pagedResult.TotalItems
                };

                ViewBag.SuccessMessage = "Đã thêm nhà cung cấp.";
                return PartialView("_SuppliersList", vm);
            }

            TempData["Message"] = "Đã thêm nhà cung cấp.";
            return RedirectToAction(nameof(Index));
        }

        // GET: Suppliers/Edit/5
        [RequirePermission("Suppliers", "Update")]
        public async Task<IActionResult> Edit(int id)
        {
            var s = await _db.Suppliers.FindAsync(id);
            if (s == null) return NotFound();
            return View(s);
        }

        // POST: Suppliers/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Suppliers", "Update")]
        public async Task<IActionResult> Edit(int id, Supplier input)
        {
            if (id != input.Id) return NotFound();

            input.Name = (input.Name ?? "").Trim();
            if (string.IsNullOrWhiteSpace(input.Name))
                ModelState.AddModelError(nameof(Supplier.Name), "Tên nhà cung cấp là bắt buộc.");

            bool exists = await _db.Suppliers.AnyAsync(s => s.Id != id && s.Name == input.Name);
            if (exists)
                ModelState.AddModelError(nameof(Supplier.Name), "Tên nhà cung cấp đã tồn tại.");

            if (!ModelState.IsValid)
            {
                // Handle AJAX request - return error in modal
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    return Json(new { success = false, errors = ModelState.Where(x => x.Value.Errors.Count > 0).ToDictionary(k => k.Key, v => v.Value.Errors.Select(e => e.ErrorMessage).ToArray()) });
                }
                TempData["Error"] = "Vui lòng kiểm tra lại thông tin.";
                return RedirectToAction(nameof(Index));
            }

            var s = await _db.Suppliers.FirstOrDefaultAsync(x => x.Id == id);
            if (s == null) return NotFound();

            s.Name = input.Name;
            s.Address = input.Address;
            s.PhoneNumber = input.PhoneNumber;
            s.Email = input.Email;

            await _db.SaveChangesAsync();

            // Handle AJAX request
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                var query = _db.Suppliers
                    .AsNoTracking()
                    .Include(s => s.Materials)
                    .AsQueryable();

                var pagedResult = await query
                    .OrderBy(s => s.Name)
                    .ToPagedResultAsync(1, 30);

                var vm = new SupplierIndexVM
                {
                    Items = pagedResult.Items,
                    Q = null,
                    Page = pagedResult.Page,
                    PageSize = pagedResult.PageSize,
                    TotalItems = pagedResult.TotalItems
                };

                ViewBag.SuccessMessage = "Đã cập nhật nhà cung cấp.";
                return PartialView("_SuppliersList", vm);
            }

            TempData["Message"] = "Đã cập nhật nhà cung cấp.";
            return RedirectToAction(nameof(Index));
        }

        // POST: Suppliers/Delete
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Suppliers", "Delete")]
        public async Task<IActionResult> Delete(int id)
        {
            var s = await _db.Suppliers.Include(x => x.Materials).FirstOrDefaultAsync(x => x.Id == id);
            if (s == null) return NotFound();

            // OnDelete bạn đang set là SetNull → xoá Supplier sẽ tự set SupplierId = null cho Materials
            _db.Suppliers.Remove(s);
            await _db.SaveChangesAsync();

            // Handle AJAX request
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                var query = _db.Suppliers
                    .AsNoTracking()
                    .Include(s => s.Materials)
                    .AsQueryable();

                var pagedResult = await query
                    .OrderBy(s => s.Name)
                    .ToPagedResultAsync(1, 30);

                var vm = new SupplierIndexVM
                {
                    Items = pagedResult.Items,
                    Q = null,
                    Page = pagedResult.Page,
                    PageSize = pagedResult.PageSize,
                    TotalItems = pagedResult.TotalItems
                };

                ViewBag.SuccessMessage = "Đã xoá nhà cung cấp.";
                return PartialView("_SuppliersList", vm);
            }

            TempData["Message"] = "Đã xoá nhà cung cấp.";
            return RedirectToAction(nameof(Index));
        }

        // API: Get Supplier for Edit Modal
        [HttpGet]
        [RequirePermission("Suppliers", "Read")]
        public async Task<IActionResult> GetSupplier(int id)
        {
            var s = await _db.Suppliers.FindAsync(id);
            if (s == null) return NotFound();
            return Json(new
            {
                id = s.Id,
                name = s.Name,
                phoneNumber = s.PhoneNumber,
                email = s.Email,
                address = s.Address
            });
        }

        // API: Create Quick Supplier (for inline creation in Material form)
        [HttpPost]
        [RequirePermission("Suppliers", "Create")]
        public async Task<IActionResult> CreateQuick([FromBody] SupplierQuickCreateVM model)
        {
            if (model == null || string.IsNullOrWhiteSpace(model.Name))
            {
                return Json(new { success = false, error = "Tên nhà cung cấp là bắt buộc." });
            }

            var name = model.Name.Trim();

            // Kiểm tra trùng tên
            bool exists = await _db.Suppliers.AnyAsync(s => s.Name == name);
            if (exists)
            {
                return Json(new { success = false, error = "Tên nhà cung cấp đã tồn tại." });
            }

            var supplier = new Supplier
            {
                Name = name,
                PhoneNumber = model.PhoneNumber?.Trim(),
                Email = model.Email?.Trim(),
                Address = model.Address?.Trim()
            };

            _db.Suppliers.Add(supplier);
            await _db.SaveChangesAsync();

            return Json(new
            {
                success = true,
                id = supplier.Id,
                name = supplier.Name
            });
        }
    }

    // ViewModel for quick create
    public class SupplierQuickCreateVM
    {
        public string Name { get; set; }
        public string PhoneNumber { get; set; }
        public string Email { get; set; }
        public string Address { get; set; }
    }
}

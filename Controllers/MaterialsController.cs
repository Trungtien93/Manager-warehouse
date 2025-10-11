using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using MNBEMART.Models;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using System.Linq;
using MNBEMART.Data;

namespace MNBEMART.Controllers
{
    public class MaterialsController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _env;

        public MaterialsController(AppDbContext context, IWebHostEnvironment env)
        {
            _context = context;
            _env = env;
        }
        private async Task PopulateDropdownsAsync(int? selectedSupplierId = null, int? selectedWarehouseId = null)
        {
            ViewData["SupplierId"] = new SelectList(
                await _context.Suppliers.AsNoTracking().OrderBy(s => s.Name).ToListAsync(),
                "Id", "Name", selectedSupplierId);

            ViewData["WarehouseId"] = new SelectList(
                await _context.Warehouses.AsNoTracking().OrderBy(w => w.Name).ToListAsync(),
                "Id", "Name", selectedWarehouseId);
        }

        // public async Task<IActionResult> Index(string? q, int? warehouseId, int page = 1, int pageSize = 30)
        // {
        //     var query = _context.Materials
        //         .AsNoTracking()
        //         .Include(m => m.Supplier)
        //         .Include(m => m.Warehouse) // cần Warehouse?.Name trong View
        //         .AsQueryable();

        //     if (!string.IsNullOrWhiteSpace(q))
        //     {
        //         q = q.Trim();
        //         query = query.Where(m =>
        //             EF.Functions.Like(m.Code, $"%{q}%") ||
        //             EF.Functions.Like(m.Name, $"%{q}%") ||
        //             (m.Description != null && EF.Functions.Like(m.Description, $"%{q}%")));
        //     }

        //     // if (warehouseId.HasValue)
        //     //     query = query.Where(m => m.WarehouseId == warehouseId.Value);

        //     if (warehouseId.HasValue)
        //     {
        //         if (warehouseId.Value == -1)
        //             query = query.Where(m => m.WarehouseId == null);  // ✅ lọc chưa phân kho
        //         else
        //             query = query.Where(m => m.WarehouseId == warehouseId.Value);
        //     }


        //     var totalItems = await query.CountAsync();

        //     var items = await query
        //         .OrderBy(m => m.Code)
        //         .Skip((page - 1) * pageSize)
        //         .Take(pageSize)
        //         .ToListAsync();

        //     // Tổng số lượng & giá trị tồn theo filter hiện tại
        //     var totals = await query.GroupBy(_ => 1)
        //         .Select(g => new
        //         {
        //             Qty = g.Sum(x => (int?)x.StockQuantity) ?? 0,
        //             Val = g.Sum(x => (decimal?)((x.PurchasePrice ?? 0) * x.StockQuantity)) ?? 0m
        //         })
        //         .FirstOrDefaultAsync() ?? new { Qty = 0, Val = 0m };

        //     var vm = new MaterialIndexVM
        //     {
        //         Items = items,
        //         Q = q,
        //         WarehouseId = warehouseId,
        //         WarehouseOptions = new SelectList(
        //                             await _context.Warehouses.AsNoTracking().OrderBy(w => w.Name).ToListAsync(),
        //                             "Id", "Name", warehouseId),
        //         Page = page,
        //         PageSize = pageSize,
        //         TotalItems = totalItems,
        //         TotalStockQty = totals.Qty,
        //         TotalStockValue = totals.Val
        //     };
        //      await PopulateDropdownsAsync();

        //     return View(vm);
        // }

        public async Task<IActionResult> Index(string? q, int? warehouseId, int page = 1, int pageSize = 30)
        {
            // Base: filter theo Material (mã, tên, mô tả)
            var mQuery = _context.Materials
                .AsNoTracking()
                .Include(m => m.Supplier)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(q))
            {
                q = q.Trim();
                mQuery = mQuery.Where(m =>
                    EF.Functions.Like(m.Code, $"%{q}%") ||
                    EF.Functions.Like(m.Name, $"%{q}%") ||
                    (m.Description != null && EF.Functions.Like(m.Description, $"%{q}%")));
            }

            // Filter theo kho bằng bảng Stocks
            if (warehouseId.HasValue)
            {
                if (warehouseId.Value == -1)
                {
                    // Chưa phân kho = chưa có bản ghi nào trong Stocks
                    mQuery = mQuery.Where(m => !_context.Stocks.Any(s => s.MaterialId == m.Id));
                }
                else
                {
                    int wid = warehouseId.Value;
                    mQuery = mQuery.Where(m => _context.Stocks.Any(s => s.MaterialId == m.Id && s.WarehouseId == wid));
                }
            }

            // Đếm tổng bản ghi (theo bộ lọc)
            var totalItems = await mQuery.CountAsync();

            // Phân trang
            var pageMaterials = await mQuery
                .OrderBy(m => m.Code)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // ===== TỒN THEO KHO CHO CÁC MATERIAL TRÊN TRANG =====
            var matIds = pageMaterials.Select(m => m.Id).ToList();

            var stocksQuery = _context.Stocks.AsNoTracking().Where(s => matIds.Contains(s.MaterialId));
            if (warehouseId.HasValue && warehouseId.Value > 0)
                stocksQuery = stocksQuery.Where(s => s.WarehouseId == warehouseId.Value);

            var stockRows = await stocksQuery
                .Join(_context.Warehouses.AsNoTracking(),
                    s => s.WarehouseId,
                    w => w.Id,
                    (s, w) => new { s.MaterialId, s.WarehouseId, w.Name, s.Quantity })
                .ToListAsync();

            var stockMap = stockRows
                .GroupBy(x => x.MaterialId)
                .ToDictionary(
                    g => g.Key,
                    g => new
                    {
                        Total = g.Sum(x => (decimal)x.Quantity),
                        List = g.Select(x => new WarehouseQtyVM
                        {
                            WarehouseId = x.WarehouseId,
                            WarehouseName = x.Name,
                            Qty = (decimal)x.Quantity
                        })
                        .OrderByDescending(v => v.Qty)
                        .ToList()
                    });

            var rows = pageMaterials.Select(m => new MaterialRowVM
            {
                M = m,
                TotalQty = stockMap.TryGetValue(m.Id, out var g) ? g.Total : 0,
                WhereStock = stockMap.TryGetValue(m.Id, out g) ? g.List : new List<WarehouseQtyVM>()
            }).ToList();

            // ===== TỔNG SL & GIÁ TRỊ TỒN (THEO BỘ LỌC HIỆN TẠI, KHÔNG GIỚI HẠN TRANG) =====
            // LẤY ID các material ĐÃ LỌC (trước phân trang)
            var filteredMatIds = await mQuery.Select(m => m.Id).ToListAsync();

            var totalAggQuery = _context.Stocks.AsNoTracking()
                .Where(s => filteredMatIds.Contains(s.MaterialId));

            if (warehouseId.HasValue && warehouseId.Value > 0)
                totalAggQuery = totalAggQuery.Where(s => s.WarehouseId == warehouseId.Value);

            var totals = await totalAggQuery
                .Join(_context.Materials.AsNoTracking(),
                    s => s.MaterialId,
                    m => m.Id,
                    (s, m) => new { s.Quantity, m.PurchasePrice })
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    Qty = g.Sum(x => (decimal?)x.Quantity) ?? 0m,
                    Val = g.Sum(x => (decimal?)((x.PurchasePrice ?? 0) * x.Quantity)) ?? 0m
                })
                .FirstOrDefaultAsync() ?? new { Qty = 0m, Val = 0m };

            var vm = new MaterialIndexVM
            {
                Items = rows,
                Q = q,
                WarehouseId = warehouseId,
                WarehouseOptions = new SelectList(
                    await _context.Warehouses.AsNoTracking().OrderBy(w => w.Name).ToListAsync(),
                    "Id", "Name", warehouseId),
                Page = page,
                PageSize = pageSize,
                TotalItems = totalItems,
                TotalStockQty = totals.Qty,      // decimal
                TotalStockValue = totals.Val     // decimal
            };

            // Cho modal Create
            await PopulateDropdownsAsync();

            return View(vm);
        }





        // GET: Materials/Create
        public IActionResult Create()
        {
            ViewData["SupplierId"] = new SelectList(_context.Suppliers, "Id", "Name");
            ViewData["WarehouseId"] = new SelectList(_context.Warehouses, "Id", "Name");
            return View();
        }
        // POST: Materials/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Material material, IFormFile ImageFile)
        {
            // Chuẩn hoá dữ liệu tối thiểu
            material.Code = (material.Code ?? "").Trim().ToUpperInvariant();
            material.Unit = (material.Unit ?? "").Trim();

            // Nếu SupplierId là 0 hoặc âm → để null (FK optional)
            if (material.SupplierId.HasValue && material.SupplierId.Value <= 0)
                material.SupplierId = null;
            if (material.WarehouseId.HasValue && material.WarehouseId.Value <= 0)
                material.WarehouseId = null;    
            if (string.IsNullOrEmpty(material.Unit))
                        ModelState.AddModelError(nameof(Material.Unit), "Đơn vị tính là bắt buộc.");       
            // Check trùng mã (case-insensitive)
                    if (await _context.Materials.AnyAsync(x => x.Code == material.Code))
                        ModelState.AddModelError(nameof(Material.Code), "Mã vật tư đã tồn tại.");

            if (!ModelState.IsValid)
            {
                var errors = ModelState.Where(kvp => kvp.Value.Errors.Count > 0)
                                    .Select(kvp => $"{kvp.Key}: {string.Join(",", kvp.Value.Errors.Select(e => e.ErrorMessage))}");
                TempData["FormErrors"] = string.Join(" | ", errors);
                ViewData["SupplierId"] = new SelectList(_context.Suppliers, "Id", "Name", material.SupplierId);
                ViewData["WarehouseId"] = new SelectList(_context.Warehouses, "Id", "Name", material.WarehouseId);
                return View(material);
            }

            try
            {
                // Upload ảnh (cách cũ)
                var uploadsRoot = Path.Combine(_env.WebRootPath ?? "", "uploads");
                if (!Directory.Exists(uploadsRoot))
                    Directory.CreateDirectory(uploadsRoot);

                if (ImageFile != null && ImageFile.Length > 0)
                {
                    var safeName = Path.GetFileNameWithoutExtension(ImageFile.FileName);
                    var ext = Path.GetExtension(ImageFile.FileName);
                    var finalFileName = $"{safeName}_{DateTime.UtcNow.Ticks}{ext}";
                    var savePath = Path.Combine(uploadsRoot, finalFileName);

                    using var stream = new FileStream(savePath, FileMode.Create);
                    await ImageFile.CopyToAsync(stream);

                    material.ImageUrl = $"/uploads/{finalFileName}";
                }

                // Quan trọng: tồn kho mặc định = 0 để tránh NULL
                material.StockQuantity = 0;

                _context.Materials.Add(material);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            catch (DbUpdateException ex)
            {
                // Lôi đúng thông điệp SQL ra cho dễ thấy nguyên nhân
                var detail = ex.InnerException?.Message ?? ex.Message;
                ModelState.AddModelError("", $"Lỗi lưu dữ liệu (chi tiết): {detail}");
                ViewData["SupplierId"] = new SelectList(_context.Suppliers, "Id", "Name", material.SupplierId);
                ViewData["WarehouseId"] = new SelectList(_context.Warehouses, "Id", "Name", material.WarehouseId);
                return View(material);
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", $"Lỗi lưu dữ liệu: {ex.Message}");
                ViewData["SupplierId"] = new SelectList(_context.Suppliers, "Id", "Name", material.SupplierId);
                ViewData["WarehouseId"] = new SelectList(_context.Warehouses, "Id", "Name", material.WarehouseId);
                return View(material);
            }
        }




      
        // GET: Materials/Edit/5
    [HttpGet]
    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null) return NotFound();

        var material = await _context.Materials.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == id);
        if (material == null) return NotFound();

        await PopulateDropdownsAsync(material.SupplierId, material.WarehouseId);
        return View(material);
    }

    // POST: Materials/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Material input, IFormFile? ImageFile)
    {
        if (id != input.Id) return NotFound();

        // Chuẩn hoá dữ liệu tối thiểu
        input.Code = (input.Code ?? "").Trim().ToUpperInvariant();
        input.Unit = (input.Unit ?? "").Trim();
        if (input.SupplierId.HasValue && input.SupplierId.Value <= 0) input.SupplierId = null;
        if (input.WarehouseId.HasValue && input.WarehouseId.Value <= 0) input.WarehouseId = null;

        // Kiểm tra trùng mã (loại trừ chính material đang sửa)
        bool codeExists = await _context.Materials
            .AnyAsync(x => x.Id != id && x.Code == input.Code);
        if (codeExists)
            ModelState.AddModelError(nameof(Material.Code), "Mã vật tư đã tồn tại.");

        if (!ModelState.IsValid)
        {
            await PopulateDropdownsAsync(input.SupplierId, input.WarehouseId);
            return View(input);
        }

        // Nạp entity gốc (tracked) để tránh overposting
        var material = await _context.Materials.FirstOrDefaultAsync(m => m.Id == id);
        if (material == null) return NotFound();

        // Cập nhật các trường được phép sửa
        material.Code           = input.Code;
        material.Name           = input.Name;
        material.Unit           = input.Unit;
        material.Description    = input.Description;
        material.SupplierId     = input.SupplierId;
        material.WarehouseId    = input.WarehouseId;
        material.Specification  = input.Specification;
        material.PurchasePrice  = input.PurchasePrice;
        material.SellingPrice   = input.SellingPrice;
        // Không động vào StockQuantity ở màn sửa thông tin

        // Ảnh: nếu có upload mới thì thay
        if (ImageFile != null && ImageFile.Length > 0)
        {
            var uploadsRoot = Path.Combine(_env.WebRootPath ?? "", "uploads");
            if (!Directory.Exists(uploadsRoot)) Directory.CreateDirectory(uploadsRoot);

            var safeName      = Path.GetFileNameWithoutExtension(ImageFile.FileName);
            var ext           = Path.GetExtension(ImageFile.FileName);
            var finalFileName = $"{safeName}_{DateTime.UtcNow.Ticks}{ext}";
            var savePath      = Path.Combine(uploadsRoot, finalFileName);

            using var stream = new FileStream(savePath, FileMode.Create);
            await ImageFile.CopyToAsync(stream);

            material.ImageUrl = $"/uploads/{finalFileName}";
        }

        try
        {
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }
        catch (DbUpdateException ex)
        {
            ModelState.AddModelError("", "Lỗi lưu dữ liệu: " + (ex.InnerException?.Message ?? ex.Message));
            await PopulateDropdownsAsync(input.SupplierId, input.WarehouseId);
            return View(input);
        }
    }

        // GET: Materials/Delete/5
                
            public async Task<IActionResult> Delete(int? id)
            {
                if (id == null) return NotFound();

                var material = await _context.Materials
                    .Include(m => m.Supplier)
                    .FirstOrDefaultAsync(m => m.Id == id);
                if (material == null) return NotFound();

                return View(material);
            }

            // POST: Materials/Delete/5
            [HttpPost, ActionName("Delete")]
            [ValidateAntiForgeryToken]
            public async Task<IActionResult> DeleteConfirmed(int id)
            {
                var material = await _context.Materials.FindAsync(id);
                if (material == null) return NotFound();

                try
                {
                    _context.Materials.Remove(material);
                    await _context.SaveChangesAsync();
                    TempData["Message"] = "Đã xoá nguyên liệu.";
                }
                catch (DbUpdateException ex)
                {
                    // Thường do ràng buộc FK (đang được tham chiếu)
                    // Tuỳ nhu cầu: gợi ý soft delete hoặc báo không thể xoá
                    TempData["Error"] = "Không thể xoá vì nguyên liệu đang được sử dụng ở chứng từ khác.";
                    // Ghi log nếu cần: ex.InnerException?.Message
                    return RedirectToAction(nameof(Delete), new { id });
                }

                return RedirectToAction(nameof(Index));
            }

    }
}



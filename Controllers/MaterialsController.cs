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
using Microsoft.AspNetCore.Authorization;

using MNBEMART.Filters;
using MNBEMART.Services;
using MNBEMART.Extensions;

namespace MNBEMART.Controllers
{
    [Authorize]
    public class MaterialsController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly IImportService _importService;
        private readonly IImageAnalysisService _imageAnalysisService;
        private readonly IDuplicateDetectionService _duplicateDetectionService;
        private readonly IMaterialClassificationService _classificationService;

        public MaterialsController(
            AppDbContext context, 
            IWebHostEnvironment env, 
            IImportService importService,
            IImageAnalysisService imageAnalysisService,
            IDuplicateDetectionService duplicateDetectionService,
            IMaterialClassificationService classificationService)
        {
            _context = context;
            _env = env;
            _importService = importService;
            _imageAnalysisService = imageAnalysisService;
            _duplicateDetectionService = duplicateDetectionService;
            _classificationService = classificationService;
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

        // Helper method to build MaterialIndexVM
        private async Task<MaterialIndexVM> BuildMaterialIndexVM(string? q, int? warehouseId, int page, int pageSize)
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

            // Phân trang
            var pagedResult = await mQuery
                .OrderBy(m => m.Code)
                .ToPagedResultAsync(page, pageSize);

            var pageMaterials = pagedResult.Items.ToList();
            var totalItems = pagedResult.TotalItems;

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

            return new MaterialIndexVM
            {
                Items = rows,
                Q = q,
                WarehouseId = warehouseId,
                WarehouseOptions = new SelectList(
                    await _context.Warehouses.AsNoTracking().OrderBy(w => w.Name).ToListAsync(),
                    "Id", "Name", warehouseId),
                Page = pagedResult.Page,
                PageSize = pagedResult.PageSize,
                TotalItems = pagedResult.TotalItems,
                TotalStockQty = totals.Qty,      // decimal
                TotalStockValue = totals.Val     // decimal
            };
        }

        [RequirePermission("Materials", "Read")]
        public async Task<IActionResult> Index(string? q, int? warehouseId, int page = 1, int pageSize = 10, bool partial = false)
        {
            var vm = await BuildMaterialIndexVM(q, warehouseId, page, pageSize);

            // Cho modal Create
            await PopulateDropdownsAsync();

            // Handle AJAX request
            if (partial || Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return PartialView("_MaterialsList", vm);
            }

            return View(vm);
        }

        // GET: Materials/Expiring
        [RequirePermission("Materials", "Read")]
        public async Task<IActionResult> Expiring()
        {
            var today = DateTime.Today;
            var warningDate = today.AddDays(30);

            // Get all materials with expiring/expired lots
            var expiringData = await _context.StockLots
                .Include(l => l.Material)
                .ThenInclude(m => m.Supplier)
                .Include(l => l.Warehouse)
                .Where(l => l.Quantity > 0 && l.ExpiryDate != null && l.ExpiryDate <= warningDate)
                .OrderBy(l => l.ExpiryDate)
                .Select(l => new
                {
                    l.Id,
                    l.LotNumber,
                    l.MaterialId,
                    MaterialCode = l.Material.Code,
                    MaterialName = l.Material.Name,
                    SupplierName = l.Material.Supplier != null ? l.Material.Supplier.Name : "",
                    l.Quantity,
                    l.Material.Unit,
                    l.ManufactureDate,
                    l.ExpiryDate,
                    WarehouseName = l.Warehouse.Name,
                    DaysRemaining = EF.Functions.DateDiffDay(today, l.ExpiryDate.Value)
                })
                .ToListAsync();

            var expired = expiringData.Where(x => x.ExpiryDate < today).ToList();
            var expiringSoon = expiringData.Where(x => x.ExpiryDate >= today).ToList();

            ViewBag.ExpiredCount = expired.Count;
            ViewBag.ExpiringSoonCount = expiringSoon.Count;
            ViewBag.Expired = expired;
            ViewBag.ExpiringSoon = expiringSoon;

            return View();
        }

        // GET: Materials/Overstock
        [RequirePermission("Materials", "Read")]
        public async Task<IActionResult> Overstock()
        {
            var overstockData = await _context.Stocks
                .Include(s => s.Material)
                .ThenInclude(m => m!.Supplier)
                .Include(s => s.Warehouse)
                .Where(s => s.Material != null && 
                       s.Material.MaximumStock.HasValue && 
                       s.Quantity > s.Material.MaximumStock.Value)
                .OrderByDescending(s => s.Quantity - s.Material!.MaximumStock!.Value)
                .Select(s => new
                {
                    s.MaterialId,
                    MaterialCode = s.Material!.Code,
                    MaterialName = s.Material.Name,
                    SupplierName = s.Material.Supplier != null ? s.Material.Supplier.Name : "",
                    s.Quantity,
                    Unit = s.Material.Unit,
                    MaximumStock = s.Material.MaximumStock!.Value,
                    OverstockAmount = s.Quantity - s.Material.MaximumStock.Value,
                    OverstockPercent = ((s.Quantity - s.Material.MaximumStock.Value) / s.Material.MaximumStock.Value) * 100,
                    WarehouseName = s.Warehouse!.Name
                })
                .ToListAsync();

            ViewBag.OverstockCount = overstockData.Count;
            ViewBag.OverstockData = overstockData;

            return View();
        }

        // GET: Materials/Create
        [RequirePermission("Materials", "Create")]
        public IActionResult Create()
        {
            ViewData["SupplierId"] = new SelectList(_context.Suppliers, "Id", "Name");
            ViewData["WarehouseId"] = new SelectList(_context.Warehouses, "Id", "Name");
            return View();
        }
        // POST: Materials/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Materials", "Create")]
        public async Task<IActionResult> Create(Material material, IFormFile ImageFile)
        {
            // Kiểm tra modal mode
            bool isModal = Request.Query.TryGetValue("modal", out var modalValue) && 
                          modalValue.Count > 0 && 
                          string.Equals(modalValue[0], "1", StringComparison.OrdinalIgnoreCase);

            // Chuẩn hoá dữ liệu tối thiểu
            material.Code = (material.Code ?? "").Trim().ToUpperInvariant();
            material.Unit = (material.Unit ?? "").Trim();

            // Tự động tạo mã nếu rỗng hoặc null
            if (string.IsNullOrWhiteSpace(material.Code))
            {
                // Tìm mã lớn nhất bắt đầu bằng "N" (case-insensitive)
                var existingCodes = await _context.Materials
                    .Where(m => m.Code != null && m.Code.ToUpper().StartsWith("N"))
                    .Select(m => m.Code)
                    .ToListAsync();

                int maxNumber = 0;
                foreach (var code in existingCodes)
                {
                    // Parse số từ mã (ví dụ: N1 -> 1, N100 -> 100)
                    if (code.Length > 1 && int.TryParse(code.Substring(1), out int number))
                    {
                        if (number > maxNumber)
                            maxNumber = number;
                    }
                }

                material.Code = "N" + (maxNumber + 1);
            }

            // Nếu SupplierId là 0 hoặc âm → để null
            if (material.SupplierId.HasValue && material.SupplierId.Value <= 0)
                material.SupplierId = null;
            if (material.WarehouseId.HasValue && material.WarehouseId.Value <= 0)
                material.WarehouseId = null;
            
            // Validation bắt buộc: Nhà cung cấp
            if (!material.SupplierId.HasValue || material.SupplierId.Value <= 0)
                ModelState.AddModelError(nameof(Material.SupplierId), "Vui lòng chọn nhà cung cấp.");
            
            if (string.IsNullOrEmpty(material.Unit))
                        ModelState.AddModelError(nameof(Material.Unit), "Đơn vị tính là bắt buộc.");       
            
            // Check trùng mã (case-insensitive) - chỉ check nếu mã không phải tự động tạo
            bool duplicateCode = false;
            if (!string.IsNullOrWhiteSpace(material.Code) && await _context.Materials.AnyAsync(x => x.Code == material.Code))
            {
                duplicateCode = true;
                ModelState.AddModelError(nameof(Material.Code), "Mã vật tư đã tồn tại.");
            }
            
            // Check trùng lặp theo 3 tiêu chí: Tên + Đơn vị + Quy cách
            bool duplicateDetails = false;
            Material? duplicateMaterial = null;
            if (!string.IsNullOrWhiteSpace(material.Name) && !string.IsNullOrEmpty(material.Unit))
            {
                var normalizedName = material.Name.Trim().ToUpper();
                var normalizedUnit = (material.Unit ?? "").Trim().ToUpper();
                // Normalize spec: null và empty string được coi như nhau
                var normalizedSpec = string.IsNullOrWhiteSpace(material.Specification) ? "" : material.Specification.Trim().ToUpper();

                duplicateMaterial = await _context.Materials
                    .FirstOrDefaultAsync(x => 
                        x.Name != null && 
                        !string.IsNullOrWhiteSpace(x.Name) &&
                        !string.IsNullOrWhiteSpace(x.Unit) &&
                        x.Name.Trim().ToUpper() == normalizedName &&
                        (x.Unit ?? "").Trim().ToUpper() == normalizedUnit &&
                        (string.IsNullOrWhiteSpace(x.Specification) ? "" : x.Specification.Trim().ToUpper()) == normalizedSpec);
                
                if (duplicateMaterial != null)
                {
                    duplicateDetails = true;
                    ModelState.AddModelError(nameof(Material.Name), 
                        $"Nguyên liệu này đã tồn tại (Mã: {duplicateMaterial.Code}). Nguyên liệu được coi là trùng nếu có cùng Tên + Đơn vị + Quy cách.");
                    ModelState.AddModelError(nameof(Material.Unit), "Đơn vị này đã được sử dụng với tên nguyên liệu này.");
                    if (!string.IsNullOrWhiteSpace(normalizedSpec))
                    {
                        ModelState.AddModelError(nameof(Material.Specification), "Quy cách này đã được sử dụng với tên nguyên liệu này.");
                    }
                }
            }

            // Nếu phát hiện trùng lặp và là modal mode, trả về JSON response
            if ((duplicateCode || duplicateDetails) && isModal)
            {
                var duplicateType = duplicateCode ? "code" : "details";
                var errorMessage = duplicateCode 
                    ? "Mã nguyên liệu này đã tồn tại. Vui lòng thêm nguyên liệu khác."
                    : $"Nguyên liệu này đã tồn tại trong hệ thống. Mã: {duplicateMaterial?.Code ?? "N/A"}. Nguyên liệu được coi là trùng nếu có cùng Tên + Đơn vị + Quy cách.";
                
                Response.ContentType = "application/json";
                return Json(new { 
                    success = false, 
                    error = errorMessage,
                    duplicateType = duplicateType,
                    duplicateMaterial = duplicateMaterial != null ? new {
                        duplicateMaterial.Id,
                        duplicateMaterial.Code,
                        duplicateMaterial.Name,
                        duplicateMaterial.Unit,
                        duplicateMaterial.Specification
                    } : null
                });
            }

            if (!ModelState.IsValid)
            {
                var errors = ModelState.Where(kvp => kvp.Value.Errors.Count > 0)
                                    .Select(kvp => $"{kvp.Key}: {string.Join(",", kvp.Value.Errors.Select(e => e.ErrorMessage))}");
                TempData["FormErrors"] = string.Join(" | ", errors);
                await PopulateDropdownsAsync(material.SupplierId, material.WarehouseId);
                
                // Nếu là modal mode, trả về partial view
                if (isModal)
                {
                    return PartialView("_CreateModal", material);
                }
                
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
                // Set CostingMethod mặc định nếu null
                if (!material.CostingMethod.HasValue)
                    material.CostingMethod = CostingMethod.WeightedAverage;

                // Reset Id = 0 để Entity Framework tự động generate ID mới (tránh PRIMARY KEY constraint violation)
                material.Id = 0;

                _context.Materials.Add(material);
                await _context.SaveChangesAsync();

                // Handle AJAX request
                if (isModal || Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    var vm = await BuildMaterialIndexVM(null, null, 1, 10);
                    ViewBag.SuccessMessage = "Đã thêm nguyên liệu thành công!";
                    return PartialView("_MaterialsList", vm);
                }

                return RedirectToAction(nameof(Index));
            }
            catch (DbUpdateException ex)
            {
                // Lôi đúng thông điệp SQL ra cho dễ thấy nguyên nhân
                var detail = ex.InnerException?.Message ?? ex.Message;
                ModelState.AddModelError("", $"Lỗi lưu dữ liệu (chi tiết): {detail}");
                await PopulateDropdownsAsync(material.SupplierId, material.WarehouseId);
                
                // Nếu là modal mode, trả về partial view
                if (isModal)
                {
                    return PartialView("_CreateModal", material);
                }
                
                return View(material);
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", $"Lỗi lưu dữ liệu: {ex.Message}");
                await PopulateDropdownsAsync(material.SupplierId, material.WarehouseId);
                
                // Nếu là modal mode, trả về partial view
                if (isModal)
                {
                    return PartialView("_CreateModal", material);
                }
                
                return View(material);
            }
        }



      

        // GET: Materials/Details/5
        [HttpGet]
        [RequirePermission("Materials", "Read")]
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            // Kiểm tra modal mode
            bool isModal = Request.Query.TryGetValue("modal", out var modalValue) && 
                          modalValue.Count > 0 && 
                          string.Equals(modalValue[0], "1", StringComparison.OrdinalIgnoreCase);

            var material = await _context.Materials.AsNoTracking()
                .Include(m => m.Supplier)
                .Include(m => m.Warehouse)
                .FirstOrDefaultAsync(m => m.Id == id);
            
            if (material == null) return NotFound();

            // Load thông tin tồn kho theo từng kho
            var stocks = await _context.Stocks.AsNoTracking()
                .Include(s => s.Warehouse)
                .Where(s => s.MaterialId == id.Value)
                .Select(s => new
                {
                    WarehouseId = s.WarehouseId,
                    WarehouseName = s.Warehouse.Name,
                    Quantity = s.Quantity
                })
                .ToListAsync();

            // Tính tổng tồn kho
            var totalStock = stocks.Sum(s => s.Quantity);

            // Load danh sách các lô hàng của nguyên liệu này
            var stockLots = await _context.StockLots.AsNoTracking()
                .Include(l => l.Warehouse)
                .Where(l => l.MaterialId == id.Value && l.Quantity > 0)
                .OrderBy(l => l.ExpiryDate == null ? DateTime.MaxValue : l.ExpiryDate)
                .ThenBy(l => l.CreatedAt)
                .ToListAsync();

            ViewBag.Stocks = stocks;
            ViewBag.TotalStock = totalStock;
            ViewBag.StockLots = stockLots;

            if (isModal)
            {
                return PartialView("_DetailsModal", material);
            }

            return View(material);
        }

        // GET: Materials/Edit/5
    [HttpGet]
    [RequirePermission("Materials", "Update")]
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
    [RequirePermission("Materials", "Update")]
    public async Task<IActionResult> Edit(int id, Material input, IFormFile? ImageFile)
    {
        if (id != input.Id) return NotFound();

        // Kiểm tra modal mode
        bool isModal = Request.Query.TryGetValue("modal", out var modalValue) && 
                      modalValue.Count > 0 && 
                      string.Equals(modalValue[0], "1", StringComparison.OrdinalIgnoreCase);

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
            // Nếu là modal mode, trả về partial view
            if (isModal)
            {
                return View(input);
            }
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
        material.CostingMethod  = input.CostingMethod;
        material.ManufactureDate = input.ManufactureDate;
        material.ExpiryDate     = input.ExpiryDate;
        material.MinimumStock   = input.MinimumStock;
        material.MaximumStock   = input.MaximumStock;
        material.ReorderQuantity = input.ReorderQuantity;
        
        // Xử lý PreferredSupplierId: nếu <= 0 thì null
        if (input.PreferredSupplierId.HasValue && input.PreferredSupplierId.Value <= 0)
            material.PreferredSupplierId = null;
        else
            material.PreferredSupplierId = input.PreferredSupplierId;
        
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

            // Handle AJAX request
            if (isModal || Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                var vm = await BuildMaterialIndexVM(null, null, 1, 10);
                ViewBag.SuccessMessage = "Đã cập nhật nguyên liệu thành công!";
                return PartialView("_MaterialsList", vm);
            }

            return RedirectToAction(nameof(Index));
        }
        catch (DbUpdateException ex)
        {
            ModelState.AddModelError("", "Lỗi lưu dữ liệu: " + (ex.InnerException?.Message ?? ex.Message));
            await PopulateDropdownsAsync(input.SupplierId, input.WarehouseId);
            // Nếu là modal mode, trả về view với modal context
            if (isModal)
            {
                return View(input);
            }
            return View(input);
        }
    }

        // GET: Materials/Delete/5
                
            [RequirePermission("Materials", "Delete")]
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
            [RequirePermission("Materials", "Delete")]
            public async Task<IActionResult> DeleteConfirmed(int id)
            {
                var material = await _context.Materials.FindAsync(id);
                if (material == null) return NotFound();

                try
                {
                    _context.Materials.Remove(material);
                    await _context.SaveChangesAsync();

                    // Handle AJAX request
                    if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                    {
                        var vm = await BuildMaterialIndexVM(null, null, 1, 10);
                        ViewBag.SuccessMessage = "Đã xoá nguyên liệu.";
                        return PartialView("_MaterialsList", vm);
                    }

                    TempData["Message"] = "Đã xoá nguyên liệu.";
                }
                catch (DbUpdateException ex)
                {
                    // Thường do ràng buộc FK (đang được tham chiếu)
                    // Tuỳ nhu cầu: gợi ý soft delete hoặc báo không thể xoá
                    
                    // Handle AJAX request
                    if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                    {
                        var vm = await BuildMaterialIndexVM(null, null, 1, 10);
                        ViewBag.ErrorMessage = "Không thể xoá vì nguyên liệu đang được sử dụng ở chứng từ khác.";
                        return PartialView("_MaterialsList", vm);
                    }

                    TempData["Error"] = "Không thể xoá vì nguyên liệu đang được sử dụng ở chứng từ khác.";
                    // Ghi log nếu cần: ex.InnerException?.Message
                    return RedirectToAction(nameof(Delete), new { id });
                }

                return RedirectToAction(nameof(Index));
            }

        // ===== IMPORT FEATURES =====
        
        // GET: Import page
        [HttpGet]
        [RequirePermission("Materials", "Create")]
        public IActionResult Import()
        {
            return View();
        }

        // POST: Import Excel
        [HttpPost]
        [RequirePermission("Materials", "Create")]
        public async Task<IActionResult> Import(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                TempData["Error"] = "Vui lòng chọn file Excel";
                return View();
            }

            if (!file.FileName.EndsWith(".xlsx") && !file.FileName.EndsWith(".xls"))
            {
                TempData["Error"] = "Chỉ chấp nhận file Excel (.xlsx, .xls)";
                return View();
            }

            try
            {
                using var stream = file.OpenReadStream();
                var result = await _importService.ImportMaterials(stream);

                if (result.SuccessCount > 0)
                {
                    TempData["Msg"] = $"Đã import thành công {result.SuccessCount} vật tư";
                }

                if (result.ErrorCount > 0)
                {
                    TempData["Error"] = $"Có {result.ErrorCount} lỗi khi import";
                }

                ViewBag.ImportResult = result;
                return View();
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Lỗi: {ex.Message}";
                return View();
            }
        }

        // Download template
        [HttpGet]
        public IActionResult DownloadTemplate()
        {
            var bytes = _importService.GenerateMaterialTemplate();
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "MauImport_VatTu.xlsx");
        }

        // Get material info for transfer cost calculation
        [HttpGet]
        [RequirePermission("Materials", "Read")]
        public async Task<IActionResult> GetMaterialInfo(int id)
        {
            var material = await _context.Materials
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == id);
            
            if (material == null)
                return Json(new { error = "Material not found" });

            return Json(new
            {
                id = material.Id,
                weightPerUnit = material.WeightPerUnit ?? 0,
                volumePerUnit = material.VolumePerUnit ?? 0
            });
        }

        // API: Get Specifications list
        [HttpGet]
        [RequirePermission("Materials", "Read")]
        public async Task<IActionResult> GetSpecifications()
        {
            var specs = await _context.MaterialSpecifications
                .AsNoTracking()
                .Where(s => s.IsActive)
                .OrderBy(s => s.Name)
                .Select(s => new { id = s.Id, name = s.Name })
                .ToListAsync();

            return Json(specs);
        }

        // API: Add new Specification
        [HttpPost]
        [RequirePermission("Materials", "Create")]
        public async Task<IActionResult> AddSpecification([FromBody] SpecificationCreateVM model)
        {
            if (model == null || string.IsNullOrWhiteSpace(model.Name))
            {
                return Json(new { success = false, error = "Tên quy cách là bắt buộc." });
            }

            var name = model.Name.Trim();

            // Kiểm tra trùng tên
            bool exists = await _context.MaterialSpecifications.AnyAsync(s => s.Name == name);
            if (exists)
            {
                return Json(new { success = false, error = "Quy cách này đã tồn tại." });
            }

            var spec = new MaterialSpecification
            {
                Name = name,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            _context.MaterialSpecifications.Add(spec);
            await _context.SaveChangesAsync();

            return Json(new
            {
                success = true,
                id = spec.Id,
                name = spec.Name
            });
        }

        // API: Analyze image
        [HttpPost]
        [RequirePermission("Materials", "Create")]
        public async Task<IActionResult> AnalyzeImage(IFormFile imageFile)
        {
            if (imageFile == null || imageFile.Length == 0)
            {
                return BadRequest(new { error = "Vui lòng chọn file ảnh" });
            }

            if (imageFile.Length > 10 * 1024 * 1024) // 10MB limit
            {
                return BadRequest(new { error = "File ảnh quá lớn (tối đa 10MB)" });
            }

            try
            {
                using var memoryStream = new MemoryStream();
                await imageFile.CopyToAsync(memoryStream);
                var imageBytes = memoryStream.ToArray();
                var mimeType = imageFile.ContentType ?? "image/jpeg";

                var result = await _imageAnalysisService.AnalyzeImageAsync(imageBytes, mimeType);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = $"Lỗi khi phân tích ảnh: {ex.Message}" });
            }
        }

        // API: Check for duplicates
        [HttpGet]
        [RequirePermission("Materials", "Read")]
        public async Task<IActionResult> CheckDuplicates(int materialId)
        {
            try
            {
                var duplicates = await _duplicateDetectionService.DetectDuplicatesAsync(materialId);
                return Ok(duplicates);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = $"Lỗi khi kiểm tra trùng lặp: {ex.Message}" });
            }
        }

        // API: Check duplicate by name (for AI image analysis)
        [HttpPost]
        [RequirePermission("Materials", "Read")]
        public async Task<IActionResult> CheckDuplicateByName([FromBody] CheckDuplicateRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request?.Name))
                {
                    return Json(new { isDuplicate = false, duplicateType = "name", message = "" });
                }

                var normalizedName = request.Name.Trim().ToUpper();
                var isDuplicate = await _context.Materials.AnyAsync(x => 
                    x.Name != null && x.Name.Trim().ToUpper() == normalizedName);

                if (isDuplicate)
                {
                    return Json(new 
                    { 
                        isDuplicate = true, 
                        duplicateType = "name", 
                        message = "Nguyên liệu này đã tồn tại. Vui lòng thêm nguyên liệu khác."
                    });
                }

                return Json(new { isDuplicate = false, duplicateType = "name", message = "" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = $"Lỗi khi kiểm tra trùng lặp: {ex.Message}" });
            }
        }

        public class CheckDuplicateRequest
        {
            public string Name { get; set; } = "";
        }

        // API: Check duplicate by Name + Unit + Specification
        [HttpPost]
        [RequirePermission("Materials", "Read")]
        public async Task<IActionResult> CheckDuplicateByDetails([FromBody] CheckDuplicateByDetailsRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request?.Name))
                {
                    return Json(new { 
                        isDuplicate = false, 
                        duplicateType = "details", 
                        message = "",
                        duplicateMaterial = (object?)null
                    });
                }

                var normalizedName = request.Name.Trim().ToUpper();
                var normalizedUnit = (request.Unit ?? "").Trim().ToUpper();
                // Normalize spec: null và empty string được coi như nhau
                var normalizedSpec = string.IsNullOrWhiteSpace(request.Specification) ? "" : request.Specification.Trim().ToUpper();

                // Kiểm tra trùng lặp: Name + Unit + Specification phải giống nhau
                var duplicateMaterial = await _context.Materials
                    .Where(x => 
                        x.Name != null && 
                        !string.IsNullOrWhiteSpace(x.Name) &&
                        !string.IsNullOrWhiteSpace(x.Unit) &&
                        x.Name.Trim().ToUpper() == normalizedName &&
                        (x.Unit ?? "").Trim().ToUpper() == normalizedUnit &&
                        (string.IsNullOrWhiteSpace(x.Specification) ? "" : x.Specification.Trim().ToUpper()) == normalizedSpec)
                    .Select(x => new
                    {
                        x.Id,
                        x.Code,
                        x.Name,
                        x.Unit,
                        x.Specification,
                        x.PurchasePrice,
                        x.SellingPrice,
                        x.StockQuantity,
                        WarehouseName = x.Warehouse != null ? x.Warehouse.Name : null,
                        SupplierName = x.Supplier != null ? x.Supplier.Name : null
                    })
                    .FirstOrDefaultAsync();

                if (duplicateMaterial != null)
                {
                    return Json(new 
                    { 
                        isDuplicate = true, 
                        duplicateType = "details", 
                        message = $"Nguyên liệu này đã tồn tại trong hệ thống. Mã: {duplicateMaterial.Code}",
                        duplicateMaterial = duplicateMaterial
                    });
                }

                return Json(new { 
                    isDuplicate = false, 
                    duplicateType = "details", 
                    message = "",
                    duplicateMaterial = (object?)null
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = $"Lỗi khi kiểm tra trùng lặp: {ex.Message}" });
            }
        }

        public class CheckDuplicateByDetailsRequest
        {
            public string Name { get; set; } = "";
            public string? Unit { get; set; }
            public string? Specification { get; set; }
        }

        // API: Classify material
        [HttpGet]
        [RequirePermission("Materials", "Read")]
        public async Task<IActionResult> Classify(int materialId)
        {
            try
            {
                var result = await _classificationService.ClassifyAsync(materialId);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = $"Lỗi khi phân loại: {ex.Message}" });
            }
        }

        // API: Classify from text
        [HttpPost]
        [RequirePermission("Materials", "Create")]
        public async Task<IActionResult> ClassifyFromText([FromBody] ClassifyRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest(new { error = "Tên nguyên liệu là bắt buộc" });
            }

            try
            {
                var result = await _classificationService.ClassifyFromTextAsync(request.Name, request.Description);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = $"Lỗi khi phân loại: {ex.Message}" });
            }
        }
    }

    public class ClassifyRequest
    {
        public string Name { get; set; } = "";
        public string? Description { get; set; }
    }

    // ViewModel for specification create
    public class SpecificationCreateVM
    {
        public string Name { get; set; }
    }
}



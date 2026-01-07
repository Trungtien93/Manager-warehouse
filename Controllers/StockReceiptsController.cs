using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Rendering;
using MNBEMART.Data;
using MNBEMART.Models;
using MNBEMART.Services;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using MNBEMART.Filters;
using MNBEMART.Extensions;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;


public class StockReceiptsController : Controller
{
    private readonly AppDbContext _db;
    private readonly IStockService _stock;
    private readonly IDocumentNumberingService _num;
    private readonly MNBEMART.Services.IAuditService _audit;
    private readonly INotificationService _notificationService;
    private readonly IQuickActionService _quickActionService;

    public StockReceiptsController(AppDbContext db, IStockService stock, IDocumentNumberingService num, MNBEMART.Services.IAuditService audit, INotificationService notificationService, IQuickActionService quickActionService)
    {
        _db = db;
        _stock = stock;
        _num = num;
        _audit = audit;
        _notificationService = notificationService;
        _quickActionService = quickActionService;
    }

    [Authorize]
    [RequirePermission("Receipts","Read")]
public async Task<IActionResult> Index(string? q, DocumentStatus? status, int? warehouseId, int page = 1, int pageSize = 10)
{
     page = Math.Max(1, page);
     pageSize = Math.Clamp(pageSize, 5, 100);

     IQueryable<StockReceipt> query = _db.StockReceipts
        .AsNoTracking();                      // <- ép về IQueryable ngay từ đầu

    if (!string.IsNullOrWhiteSpace(q))
        query = query.Where(r => r.ReceiptNumber!.Contains(q) || (r.Note ?? "").Contains(q));

    if (status.HasValue)
        query = query.Where(r => r.Status == status);

    if (warehouseId.HasValue)
        query = query.Where(r => r.WarehouseId == warehouseId);

    // Include ĐẶT SAU CÙNG, vẫn là IQueryable<StockReceipt>
    query = query
        .Include(r => r.Warehouse)
        .Include(r => r.CreatedBy)
        .Include(r => r.Details).ThenInclude(d => d.Material);

    var pagedResult = await query
        .OrderByDescending(r => r.CreatedAt)
        .ToPagedResultAsync(page, pageSize);

    ViewBag.Page = pagedResult.Page;
    ViewBag.PageSize = pagedResult.PageSize;
    ViewBag.TotalPages = pagedResult.TotalPages;
    ViewBag.TotalItems = pagedResult.TotalItems;
    ViewBag.PagedResult = pagedResult;

    var list = pagedResult.Items.ToList();

    // chips - Optimized: Single query with grouping instead of 5 separate queries
    var statusCounts = await _db.StockReceipts.AsNoTracking()
        .GroupBy(x => x.Status)
        .Select(g => new { Status = g.Key, Count = g.Count() })
        .ToListAsync();
    
    ViewBag.CountAll = statusCounts.Sum(x => x.Count);
    ViewBag.CountMoi = statusCounts.FirstOrDefault(x => x.Status == DocumentStatus.Moi)?.Count ?? 0;
    ViewBag.CountXacNhan = statusCounts.FirstOrDefault(x => x.Status == DocumentStatus.DaXacNhan)?.Count ?? 0;
    ViewBag.CountNhap = statusCounts.FirstOrDefault(x => x.Status == DocumentStatus.DaNhapHang)?.Count ?? 0;
    ViewBag.CountHuy = statusCounts.FirstOrDefault(x => x.Status == DocumentStatus.DaHuy)?.Count ?? 0;

    // dropdown kho
    ViewBag.WarehouseOptions = new SelectList(
        await _db.Warehouses.AsNoTracking().OrderBy(w => w.Name).ToListAsync(),
        "Id", "Name", warehouseId);
    ViewBag.Materials = await _db.Materials.AsNoTracking()
    .Select(m => new { m.Id, m.Code, m.Name, m.Unit, m.Specification, m.PurchasePrice })
    .ToListAsync();
    // tổng cộng phần chân trang (trên dataset đã lọc)
    decimal sumQty = 0, sumAmt = 0;
    foreach (var r in list)
    {
        var qtty = r.Details.Sum(d => Convert.ToDecimal(d.Quantity));
        var amt  = r.Details.Sum(d => Convert.ToDecimal(d.Quantity) * d.UnitPrice);
        sumQty += qtty; sumAmt += amt;
    }
    ViewBag.SumQty = sumQty;
    ViewBag.SumAmt = sumAmt;

    // dữ liệu cho modal tạo nhanh
    ViewBag.Materials = await _db.Materials.AsNoTracking().OrderBy(m => m.Name).ToListAsync();

    return View(list);
}

[HttpPost]
[Authorize(Roles = "Admin")]
[ValidateAntiForgeryToken]
public async Task<IActionResult> BulkDelete([FromForm] int[] ids)
{
    if (ids == null || ids.Length == 0)
    {
        TempData["Error"] = "Chưa chọn bản ghi.";
        return RedirectToAction(nameof(Index));
    }
    var recs = await _db.StockReceipts.Where(x => ids.Contains(x.Id)).ToListAsync();
    _db.StockReceipts.RemoveRange(recs);
    await _db.SaveChangesAsync();
    await _audit.LogAsync(int.TryParse(User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier), out var uid) ? uid : 0,
        action: "BulkDelete", objectType: "StockReceipt", objectId: string.Join(',', ids), module: "Nhập kho", content: $"Xoá {recs.Count} phiếu");
    TempData["Msg"] = $"Đã xoá {recs.Count} phiếu.";
    return RedirectToAction(nameof(Index));
}

[HttpGet]
[Authorize]
[RequirePermission("Receipts","Read")]
public async Task<IActionResult> ExportCsv(string? q, DocumentStatus? status, int? warehouseId)
{
    var query = _db.StockReceipts.AsNoTracking();
    if (!string.IsNullOrWhiteSpace(q))
        query = query.Where(r => r.ReceiptNumber!.Contains(q) || (r.Note ?? "").Contains(q));
    if (status.HasValue) query = query.Where(r => r.Status == status);
    if (warehouseId.HasValue) query = query.Where(r => r.WarehouseId == warehouseId);
    query = query.Include(r => r.Warehouse).Include(r => r.CreatedBy).Include(r => r.Details);

    var rows = await query.OrderByDescending(x => x.CreatedAt).ToListAsync();
    var sb = new System.Text.StringBuilder();
    sb.AppendLine("Mã phiếu,Kho,Người tạo,Ngày tạo,Trạng thái,Số lượng,Thành tiền,Ghi chú");
    foreach (var r in rows)
    {
        var qty = r.Details.Sum(d => (decimal)d.Quantity);
        var amt = r.Details.Sum(d => (decimal)d.Quantity * d.UnitPrice);
        var line = string.Join(',',
            Escape(r.ReceiptNumber),
            Escape(r.Warehouse?.Name),
            Escape(r.CreatedBy?.FullName),
            r.CreatedAt.ToString("dd/MM/yyyy HH:mm"),
            Escape(GetStatusVietnamese(r.Status)),
            qty.ToString(System.Globalization.CultureInfo.InvariantCulture),
            amt.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Escape(r.Note));
        sb.AppendLine(line);
    }
    
    // Thêm BOM UTF-8 để Excel nhận diện đúng encoding
    var utf8WithBom = new System.Text.UTF8Encoding(true);
    var csvContent = sb.ToString();
    var bytes = utf8WithBom.GetPreamble().Concat(utf8WithBom.GetBytes(csvContent)).ToArray();
    
    return File(bytes, "text/csv; charset=utf-8", $"StockReceipts_{DateTime.Now:yyyyMMddHHmm}.csv");

    static string Escape(string? v)
        => "\"" + (v ?? "").Replace("\"", "\"\"") + "\"";
}

[HttpGet]
[Authorize]
[RequirePermission("Receipts","Read")]
public async Task<IActionResult> PrintAll(string? q, DocumentStatus? status, int? warehouseId, string? mode)
{
    var query = _db.StockReceipts.AsNoTracking();
    if (!string.IsNullOrWhiteSpace(q))
        query = query.Where(r => r.ReceiptNumber!.Contains(q) || (r.Note ?? "").Contains(q));
    if (status.HasValue) query = query.Where(r => r.Status == status);
    if (warehouseId.HasValue) query = query.Where(r => r.WarehouseId == warehouseId);
    query = query.Include(r => r.Warehouse).Include(r => r.CreatedBy).Include(r => r.Details);

    var list = await query.OrderByDescending(r => r.CreatedAt).ToListAsync();
    ViewBag.Mode = mode;
    return View("Print", list);
}

[HttpGet]
[Authorize]
[RequirePermission("Receipts","Read")]
public async Task<IActionResult> PrintOne(int id)
{
    var list = await _db.StockReceipts.AsNoTracking()
        .Include(r => r.Warehouse).Include(r => r.CreatedBy).Include(r => r.Details)
        .Where(r => r.Id == id)
        .ToListAsync();
    return View("Print", list);
}

[HttpGet]
[Authorize]
[RequirePermission("Receipts","Read")]
public async Task<IActionResult> PrintSelected([FromQuery] int[] ids, string? mode)
{
    if (ids == null || ids.Length == 0)
        return RedirectToAction(nameof(PrintAll), new { mode });

    var list = await _db.StockReceipts.AsNoTracking()
        .Include(r => r.Warehouse).Include(r => r.CreatedBy).Include(r => r.Details)
        .Where(r => ids.Contains(r.Id))
        .OrderByDescending(r => r.CreatedAt)
        .ToListAsync();
    ViewBag.Mode = mode;
    return View("Print", list);
}

[HttpGet]
[Authorize]
[RequirePermission("Receipts","Create")]
public IActionResult DownloadTemplate()
{
    var csv = "WarehouseId,Note,Details\n" +
              "1,Nhap hang mau,10:5:12000|11:2:15000";
    var bytes = System.Text.Encoding.UTF8.GetBytes(csv);
    return File(bytes, "text/csv; charset=utf-8", "receipt_import_template.csv");
}

[HttpPost]
[Authorize(Roles = "Admin")]
[ValidateAntiForgeryToken]
[RequirePermission("Receipts","Create")]
public async Task<IActionResult> ImportCsv(IFormFile file)
{
    if (file == null || file.Length == 0)
    {
        TempData["Error"] = "Vui lòng chọn tệp CSV.";
        return RedirectToAction(nameof(Index));
    }
    using var s = file.OpenReadStream();
    using var reader = new System.IO.StreamReader(s);
    string? line = await reader.ReadLineAsync(); // skip header

    var uidStr = User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier);
    int.TryParse(uidStr, out var uid);
    int created = 0;
    while ((line = await reader.ReadLineAsync()) != null)
    {
        var cols = line.Split(',');
        if (cols.Length < 3) continue;
        if (!int.TryParse(cols[0].Trim(), out var wh)) continue;
        var note = cols[1].Trim();
        var detailsStr = cols[2].Trim();

        var r = new StockReceipt
        {
            WarehouseId = wh,
            CreatedById = uid,
            CreatedAt = DateTime.Now,
            Status = DocumentStatus.Moi,
            ReceiptNumber = await _num.NextAsync("StockReceipt", wh),
            Note = note,
            Details = new List<StockReceiptDetail>()
        };

        foreach (var part in detailsStr.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = part.Split(':');
            if (p.Length < 3) continue;
            if (!int.TryParse(p[0], out var mat)) continue;
            if (!decimal.TryParse(p[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var q2)) continue;
            if (!decimal.TryParse(p[2], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var price)) continue;
r.Details.Add(new StockReceiptDetail { MaterialId = mat, Quantity = (double)q2, UnitPrice = Math.Round(price, 2) });
        }
        if (r.Details.Any())
        {
            _db.StockReceipts.Add(r);
            created++;
        }
    }
    await _db.SaveChangesAsync();
    TempData["Msg"] = $"Đã nhập {created} phiếu.";
    return RedirectToAction(nameof(Index));
}

[HttpGet]
[Authorize]
[RequirePermission("Receipts","Read")]
public async Task<IActionResult> Suggest()
{
    const decimal LowLevel = 5m;
    var low = await _db.Stocks.AsNoTracking()
        .Include(s => s.Material).Include(s => s.Warehouse)
        .Where(s => s.Quantity <= LowLevel)
        .OrderBy(s => s.Warehouse.Name).ThenBy(s => s.Material.Code)
        .ToListAsync();
    return View("Suggest", low);
}


    

    [HttpGet]
    [Authorize]
[RequirePermission("Receipts","Create")]
    public IActionResult Create()
    {
        // Chặn Admin tự tạo phiếu
        if (string.Equals(User.FindFirstValue(ClaimTypes.Role), "Admin", StringComparison.OrdinalIgnoreCase))
            return Forbid();
        // nếu chưa đăng nhập thì redirect về Login
        if (User?.Identity?.IsAuthenticated != true)
            return RedirectToAction("Login", "Account");

        var uidStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(uidStr, out var uid))
            return Forbid();

        var model = new StockReceipt { CreatedById = uid };

        ViewBag.WarehouseId = new SelectList(_db.Warehouses, "Id", "Name");
        ViewBag.Materials = _db.Materials.OrderBy(m => m.Name).ToList();
        ViewBag.CurrentUserName = User.Identity?.Name;
        return View(model); // model trống
    }

    [HttpPost]
[ValidateAntiForgeryToken]
[RequirePermission("Receipts","Create")]
    public async Task<IActionResult> CreateReceipt(StockReceipt receipt, IFormFile EvidenceFile)
    {
        // Chặn Admin tự tạo phiếu
        if (string.Equals(User.FindFirstValue(ClaimTypes.Role), "Admin", StringComparison.OrdinalIgnoreCase))
            return Forbid();
        // server-set
        receipt.CreatedAt = DateTime.Now;
        receipt.Status = DocumentStatus.Moi;
        // cấp số chứng từ chuẩn
        receipt.ReceiptNumber = await _num.NextAsync("StockReceipt", receipt.WarehouseId);
        // Tự động tạo số chứng từ gốc
        receipt.ReferenceDocumentNumber = receipt.ReceiptNumber;
        receipt.AttachedDocuments ??= string.Empty;

        // Override người lập từ claim (không tin form)
        var uidStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (int.TryParse(uidStr, out var uid)) receipt.CreatedById = uid;
        else ModelState.AddModelError("", "Không xác định được người lập. Vui lòng đăng nhập lại.");

        // Validation các trường bắt buộc
        if (receipt.WarehouseId <= 0)
            ModelState.AddModelError(nameof(StockReceipt.WarehouseId), "Vui lòng chọn kho chứa hàng.");

        if (string.IsNullOrWhiteSpace(receipt.DeliveredByName))
            ModelState.AddModelError(nameof(StockReceipt.DeliveredByName), "Vui lòng nhập người giao.");

        if (!receipt.ReferenceDate.HasValue)
            ModelState.AddModelError(nameof(StockReceipt.ReferenceDate), "Vui lòng chọn ngày chứng từ.");

        // làm sạch ModelState cho các field không post
        ModelState.Remove(nameof(StockReceipt.ReceiptNumber));
        ModelState.Remove(nameof(StockReceipt.Status));
        ModelState.Remove(nameof(StockReceipt.CreatedAt));
        ModelState.Remove(nameof(StockReceipt.ApprovedById));
        ModelState.Remove(nameof(StockReceipt.ApprovedAt));
        ModelState.Remove(nameof(StockReceipt.Note));
        ModelState.Remove(nameof(StockReceipt.Warehouse));
        ModelState.Remove(nameof(StockReceipt.CreatedBy));
        // AttachedDocuments KHÔNG required → không cần remove, nhưng remove cũng không sao
        ModelState.Remove(nameof(StockReceipt.AttachedDocuments));

        // BẮT BUỘC ảnh minh chứng + lưu file
        if (EvidenceFile == null || EvidenceFile.Length == 0 || !(EvidenceFile.ContentType?.StartsWith("image/") ?? false))
        {
            ModelState.AddModelError("", "Vui lòng tải lên hình ảnh minh chứng (PNG/JPG).");
        }
        else
        {
            var uploadsRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "receipts");
            Directory.CreateDirectory(uploadsRoot);
            var ext = Path.GetExtension(EvidenceFile.FileName);
            var safeName = $"{DateTime.Now:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}{ext}";
            var filePath = Path.Combine(uploadsRoot, safeName);
            using (var fs = new FileStream(filePath, FileMode.Create))
                await EvidenceFile.CopyToAsync(fs);
            receipt.AttachedDocuments = $"/uploads/receipts/{safeName}";
        }

        // chuẩn hóa chi tiết và validation
        var detailsList = (receipt.Details ?? new List<StockReceiptDetail>())
            .Where(d => d.MaterialId > 0 && d.Quantity > 0)
            .Select(d => 
            { 
                d.UnitPrice = Math.Round(d.UnitPrice, 2);
                // Tự động tạo mã lô nếu trống
                if (string.IsNullOrWhiteSpace(d.LotNumber))
                {
                    var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
                    var random = new Random().Next(100, 999);
                    d.LotNumber = $"LOT-{timestamp}-{random}";
                }
                return d; 
            })
            .ToList();

        receipt.Details = detailsList;

        // gỡ lỗi nav trong details (nếu có)
        for (int i = 0; i < detailsList.Count; i++)
        {
            ModelState.Remove($"Details[{i}].Material");
            ModelState.Remove($"Details[{i}].StockReceipt");
            
            // Validation các trường bắt buộc trong detail
            var detail = detailsList[i];
            if (detail.MaterialId <= 0)
                ModelState.AddModelError($"Details[{i}].MaterialId", $"Dòng {i + 1}: Vui lòng chọn sản phẩm.");
            if (detail.Quantity <= 0)
                ModelState.AddModelError($"Details[{i}].Quantity", $"Dòng {i + 1}: Số lượng phải lớn hơn 0.");
            if (detail.UnitPrice < 0)
                ModelState.AddModelError($"Details[{i}].UnitPrice", $"Dòng {i + 1}: Giá nhập phải lớn hơn hoặc bằng 0.");
            if (string.IsNullOrWhiteSpace(detail.Unit))
                ModelState.AddModelError($"Details[{i}].Unit", $"Dòng {i + 1}: Vui lòng nhập đơn vị tính.");
            if (!detail.ManufactureDate.HasValue)
                ModelState.AddModelError($"Details[{i}].ManufactureDate", $"Dòng {i + 1}: Vui lòng chọn ngày sản xuất.");
            if (!detail.ExpiryDate.HasValue)
                ModelState.AddModelError($"Details[{i}].ExpiryDate", $"Dòng {i + 1}: Vui lòng chọn hạn sử dụng.");
        }

        if (!detailsList.Any())
            ModelState.AddModelError("", "Vui lòng nhập ít nhất 1 dòng chi tiết hợp lệ với đầy đủ thông tin (Sản phẩm, SL, Giá nhập, ĐVT, Ngày sản xuất, Hạn sử dụng).");

        if (!ModelState.IsValid)
        {
            ViewBag.WarehouseId = new SelectList(_db.Warehouses, "Id", "Name", receipt.WarehouseId);
            ViewBag.Materials = _db.Materials.OrderBy(m => m.Name).ToList();
            ViewBag.CurrentUserName = User.Identity?.Name;
            return View("Create", receipt);
        }

        _db.StockReceipts.Add(receipt);
        await _db.SaveChangesAsync();
        
        // Tạo thông báo cho Admin khi user tạo phiếu
        try
        {
            var adminUserIds = await GetAdminUserIdsAsync();
            var warehouse = await _db.Warehouses.FindAsync(receipt.WarehouseId);
            // Load CreatedBy để lấy tên người tạo
            await _db.Entry(receipt).Reference(r => r.CreatedBy).LoadAsync();
            var createdByName = receipt.CreatedBy?.FullName ?? User.Identity?.Name ?? "System";
            
            if (adminUserIds.Any())
            {
                await _notificationService.CreateNotificationForUsersAsync(
                    NotificationType.Receipt,
                    receipt.Id,
                    $"Phiếu nhập mới: {receipt.ReceiptNumber}",
                    $"Người tạo: {createdByName} | Kho: {warehouse?.Name ?? "N/A"}",
                    adminUserIds,
                    NotificationPriority.Normal
                );
            }
        }
        catch
        {
            // Log error but don't fail the create operation
        }
        
        return RedirectToAction(nameof(Index));
    }



    public async Task<IActionResult> Details(int id)
    {
        var receipt = await _db.StockReceipts
            .Include(r => r.Warehouse)
            .Include(r => r.CreatedBy)
            .Include(r => r.ApprovedBy)
            .Include(r => r.Details).ThenInclude(d => d.Material)
            .FirstOrDefaultAsync(r => r.Id == id);
        if (receipt == null) return NotFound();
        return View(receipt);
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ValidateAntiForgeryToken]
[ActionName("ApprovePost")]   // để tránh trùng tên với GET Details
[RequirePermission("Receipts","Update")]
    public async Task<IActionResult> Approve(int id)
    {
        var r = await _db.StockReceipts.Include(x => x.Details).FirstOrDefaultAsync(x => x.Id == id);
        if (r == null) return NotFound();
        if (r.Status != DocumentStatus.Moi)
            return BadRequest("Chỉ xác nhận phiếu ở trạng thái Mới.");

        // KHÔNG đụng tồn kho ở bước xác nhận
        r.Status = DocumentStatus.DaXacNhan;
        r.ApprovedAt = DateTime.Now;
        var uidStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (int.TryParse(uidStr, out var uid)) r.ApprovedById = uid;

        await _db.SaveChangesAsync();
        
        // Thông báo cho user tạo phiếu
        if (r.CreatedById > 0)
        {
            await _notificationService.CreateNotificationAsync(
                NotificationType.Receipt,
                r.Id,
                $"Phiếu nhập đã được xác nhận: {r.ReceiptNumber}",
                $"Phiếu nhập của bạn đã được xác nhận bởi admin",
                r.CreatedById,
                NotificationPriority.Normal
            );
        }
        
        // return RedirectToAction(nameof(Details), new { id });
        TempData["OpenDetailId"] = id;                 // <— gửi id để mở modal
        return RedirectToAction(nameof(Index));        // <— quay về Index
    }

    // POST: StockReceipts/QuickConfirm/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequirePermission("Receipts", "Update")]
    public async Task<IActionResult> QuickConfirm(int id, int? notificationId = null)
    {
        var userId = GetCurrentUserId();
        var success = await _quickActionService.QuickConfirmReceiptAsync(id, userId);
        
        if (notificationId.HasValue && success)
        {
            await _notificationService.MarkAsReadAsync(notificationId.Value, userId);
        }

        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
        {
            return Json(new { success, message = success ? "Đã xác nhận thành công" : "Xác nhận thất bại" });
        }

        TempData["Msg"] = success ? "Đã xác nhận thành công" : "Xác nhận thất bại";
        return RedirectToAction(nameof(Index));
    }

    // POST: StockReceipts/QuickCancel/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequirePermission("Receipts", "Update")]
    public async Task<IActionResult> QuickCancel(int id, int? notificationId = null)
    {
        var userId = GetCurrentUserId();
        var success = await _quickActionService.QuickCancelReceiptAsync(id, userId);
        
        if (notificationId.HasValue && success)
        {
            await _notificationService.MarkAsReadAsync(notificationId.Value, userId);
        }

        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
        {
            return Json(new { success, message = success ? "Đã hủy thành công" : "Hủy thất bại" });
        }

        TempData["Msg"] = success ? "Đã hủy thành công" : "Hủy thất bại";
        return RedirectToAction(nameof(Index));
    }

    private int GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(userIdClaim, out int userId) ? userId : 1;
    }

    // Helper method để lấy danh sách Admin user IDs
    private async Task<List<int>> GetAdminUserIdsAsync()
    {
        // Get Admin users (role string = "Admin")
        var adminUserIds = await _db.Users
            .Where(u => u.IsActive && u.Role != null && u.Role.ToLower() == "admin")
            .Select(u => u.Id)
            .ToListAsync();

        // Get users with Admin role from UserRoles table
        try
        {
            var adminRoleIds = await (from r in _db.Roles
                                    where r.Code.ToLower() == "admin" || r.Name.ToLower() == "admin"
                                    select r.Id)
                                    .ToListAsync();

            if (adminRoleIds.Any())
            {
                var adminUserIdsFromRoles = await (from ur in _db.UserRoles
                                                  where adminRoleIds.Contains(ur.RoleId)
                                                  select ur.UserId)
                                                  .Distinct()
                                                  .ToListAsync();
                adminUserIds = adminUserIds.Union(adminUserIdsFromRoles).Distinct().ToList();
            }
        }
        catch
        {
            // UserRoles table may not exist, ignore
        }

        return adminUserIds;
    }


    

    //     [HttpPost]
    // [Authorize(Roles = "admin")]
    // [ValidateAntiForgeryToken]
    //  [ActionName("ReceivePost")]  // để tránh trùng tên với GET Details
    // public async Task<IActionResult> Receive(int id)
    // {
    //     using var tx = await _db.Database.BeginTransactionAsync();

    //     var r = await _db.StockReceipts
    //         .Include(x => x.Details)
    //         .FirstOrDefaultAsync(x => x.Id == id);
    //     if (r == null) return NotFound();
    //     if (r.Status != DocumentStatus.DaXacNhan)
    //         return BadRequest("Chỉ nhập kho phiếu ở trạng thái Đã xác nhận.");

    //     // Cộng tồn theo từng (Kho, Nguyên liệu)
    //     foreach (var d in r.Details)
    //     {
    //         var sb = await _db.Set<StockBalance>()
    //             .FirstOrDefaultAsync(x => x.WarehouseId == r.WarehouseId && x.MaterialId == d.MaterialId);

    //         if (sb == null)
    //         {
    //             sb = new StockBalance {
    //                 WarehouseId = r.WarehouseId,
    //                 MaterialId = d.MaterialId,
    //                 Quantity = 0
    //             };
    //             _db.Add(sb);
    //         }

    //         sb.Quantity += (decimal)d.Quantity;
    //     }

    //     // Trạng thái phiếu
    //     r.Status = DocumentStatus.DaNhapHang;

    //     await _db.SaveChangesAsync();
    //     await tx.CommitAsync();

    //     // return RedirectToAction(nameof(Details), new { id });
    //     TempData["OpenDetailId"] = id;
    //     return RedirectToAction(nameof(Index));
    // }


        [HttpPost]
[Authorize(Roles = "Admin")]
[ValidateAntiForgeryToken]
// [ActionName("Receive")] // View dùng asp-action="Receive"
[RequirePermission("Receipts","Update")]
public async Task<IActionResult> ReceivePost(int id)
{
    using var tx = await _db.Database.BeginTransactionAsync();

    var r = await _db.StockReceipts
        .Include(x => x.Details)
        .FirstOrDefaultAsync(x => x.Id == id);
    if (r == null) return NotFound();
    if (r.Status != DocumentStatus.DaXacNhan)
        return BadRequest("Chỉ nhập kho phiếu ở trạng thái Đã xác nhận.");

    // Chốt ngày ghi sổ (gợi ý dùng local date)
    var today = DateTime.Today;

    foreach (var d in r.Details)
    {
        var qty   = (decimal)d.Quantity;
        var amt   = Math.Round(qty * d.UnitPrice, 2);

        // Tìm dòng sổ kỳ theo (Kho, NL, Ngày)
        var sb = await _db.Set<StockBalance>()
            .FirstOrDefaultAsync(x =>
                x.WarehouseId == r.WarehouseId &&
                x.MaterialId  == d.MaterialId  &&
                x.Date        == today);

        if (sb == null)
        {
            sb = new StockBalance {
                WarehouseId = r.WarehouseId,
                MaterialId  = d.MaterialId,
                Date        = today,
                InQty       = 0,
                InValue     = 0,
                OutQty      = 0,
                OutValue    = 0,
                UpdatedAt   = DateTime.Now
            };
            _db.Add(sb);
        }

        // Cộng phát sinh nhập của NGÀY
        sb.InQty   += qty;
        sb.InValue += amt;
        sb.UpdatedAt = DateTime.Now;

        // (Tuỳ chọn) nếu bạn vẫn duy trì bảng tồn tức thời Stock => cập nhật luôn:
        var stock = await _db.Stocks
            .FirstOrDefaultAsync(s => s.WarehouseId == r.WarehouseId && s.MaterialId == d.MaterialId);
        if (stock == null)
        {
            stock = new Stock {
                WarehouseId = r.WarehouseId,
                MaterialId  = d.MaterialId,
                Quantity    = 0
            };
            _db.Stocks.Add(stock);
        }
        stock.Quantity += qty;

        // Tăng theo lô (nếu có khai báo) - lưu giá nhập vào lô
        var unitPrice = d.UnitPrice > 0 ? d.UnitPrice : (await _db.Materials.FindAsync(d.MaterialId))?.PurchasePrice;
        await _stock.IncreaseLotAsync(r.WarehouseId, d.MaterialId, qty, d.LotNumber, d.ManufactureDate, d.ExpiryDate, unitPrice);
    }

    // Cập nhật trạng thái phiếu
    r.Status = DocumentStatus.DaNhapHang;

    await _db.SaveChangesAsync();
    await tx.CommitAsync();

    TempData["OpenDetailId"] = id;
    return RedirectToAction(nameof(Index));
}





    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ValidateAntiForgeryToken]
    [ActionName("RejectPost")]    // để tránh trùng tên với GET Details
[RequirePermission("Receipts","Delete")]
    public async Task<IActionResult> Reject(int id, string? note)
    {
        using var tx = await _db.Database.BeginTransactionAsync();
        var r = await _db.StockReceipts.Include(x => x.Details).FirstOrDefaultAsync(x => x.Id == id);
        if (r == null) return NotFound();
        if (r.Status == DocumentStatus.DaHuy) return BadRequest("Phiếu đã huỷ.");

        // Nếu đã nhập kho rồi -> TRỪ tồn
        if (r.Status == DocumentStatus.DaNhapHang)
        {
            // Nếu service bạn CHƯA có DecreaseAsync, có thể gọi IncreaseAsync với số âm.
            foreach (var d in r.Details)
            {
                await _stock.DecreaseAsync(r.WarehouseId, d.MaterialId, (decimal)d.Quantity);
                await _stock.DecreaseLotAsync(r.WarehouseId, d.MaterialId, (decimal)d.Quantity, d.LotNumber, d.ManufactureDate, d.ExpiryDate);
            }
            await _stock.SaveAsync();
        }

        r.Status = DocumentStatus.DaHuy;
        if (!string.IsNullOrWhiteSpace(note))
            r.Note = string.IsNullOrWhiteSpace(r.Note) ? $"[HUỶ] {note}" : $"{r.Note}\n[HUỶ] {note}";

        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        // return RedirectToAction(nameof(Details), new { id });
        TempData["OpenDetailId"] = id;
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [Authorize]
    [RequirePermission("Receipts", "Read")]
    public async Task<IActionResult> ExportPdf(string? q, DocumentStatus? status, int? warehouseId)
    {
        var query = _db.StockReceipts.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(r => r.ReceiptNumber!.Contains(q) || (r.Note ?? "").Contains(q));
        if (status.HasValue) query = query.Where(r => r.Status == status);
        if (warehouseId.HasValue) query = query.Where(r => r.WarehouseId == warehouseId);
        query = query.Include(r => r.Warehouse).Include(r => r.CreatedBy).Include(r => r.Details).ThenInclude(d => d.Material);

        var list = await query.OrderByDescending(r => r.CreatedAt).ToListAsync();

        if (list.Count == 0)
        {
            return NotFound("Không có phiếu nhập kho nào để xuất.");
        }

        // Tạo PDF - mỗi phiếu một trang
        QuestPDF.Settings.License = LicenseType.Community;
            var document = QuestPDF.Fluent.Document.Create(container =>
        {
            foreach (var receipt in list)
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                    page.Margin(2f, Unit.Centimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Times New Roman"));

                    page.Content()
                    .Column(column =>
                    {
                            // Header - Form number và thông tin đơn vị
                            column.Item().Row(row =>
                            {
                                // Bên trái: Đơn vị, Mã QHNS
                                row.RelativeItem().Column(leftCol =>
                                {
                                    leftCol.Item().Text("Đơn vị: ").FontSize(10).FontColor(Colors.Black).FontFamily("Times New Roman");
                                    leftCol.Item().PaddingTop(4).Text("Mã QHNS: ").FontSize(10).FontColor(Colors.Black).FontFamily("Times New Roman");
                                });

                                // Bên phải: Mẫu số C30-HD
                                row.RelativeItem().Column(rightCol =>
                                {
                                    rightCol.Item().AlignRight().Text("Mẫu số C30 - HD")
                            .FontSize(10)
                                        .Bold()
                                        .FontColor(Colors.Black)
                                        .FontFamily("Times New Roman");
                                    rightCol.Item().PaddingTop(2).AlignRight().Text("(Ban hành kèm theo thông tư 107/2017/TT-BTC")
                                        .FontSize(8)
                                        .Italic()
                                        .FontColor(Colors.Grey.Darken1)
                                        .FontFamily("Times New Roman");
                                    rightCol.Item().AlignRight().Text("ngày 24/11/2017)")
                                        .FontSize(8)
                                        .Italic()
                                        .FontColor(Colors.Grey.Darken1)
                                        .FontFamily("Times New Roman");
                                });
                            });

                            column.Item().PaddingTop(20);

                            // Tiêu đề chính
                            column.Item().AlignCenter().Text("PHIẾU NHẬP KHO")
                                .FontSize(18)
                                        .Bold()
                                .FontColor(Colors.Black)
                                .FontFamily("Times New Roman");

                            column.Item().PaddingTop(10);

                            // Thông tin phiếu
                            column.Item().Column(infoCol =>
                            {
                                infoCol.Item().Row(row =>
                                {
                                    row.AutoItem().Text("Ngày").FontSize(10).FontFamily("Times New Roman");
                                    row.RelativeItem().Text($": {receipt.CreatedAt:dd} tháng {receipt.CreatedAt:MM} năm {receipt.CreatedAt:yyyy}").FontSize(10).FontFamily("Times New Roman");
                                    row.AutoItem().PaddingLeft(20).Text("Số").FontSize(10).FontFamily("Times New Roman");
                                    row.RelativeItem().Text($": {receipt.ReceiptNumber ?? ""}").FontSize(10).Bold().FontFamily("Times New Roman");
                                });

                                infoCol.Item().PaddingTop(8).Row(row =>
                                {
                                    row.AutoItem().Text("- Họ tên người giao").FontSize(10).FontFamily("Times New Roman");
                                    row.RelativeItem().Text($": {receipt.DeliveredByName ?? ""}").FontSize(10).FontFamily("Times New Roman");
                                });

                                infoCol.Item().PaddingTop(4).Row(row =>
                                {
                                    row.AutoItem().Text("Nhập tại kho").FontSize(10).FontFamily("Times New Roman");
                                    row.RelativeItem().Text($": {receipt.Warehouse?.Name ?? ""}").FontSize(10).Bold().FontFamily("Times New Roman");
                                });
                        });

                            column.Item().PaddingTop(15);

                            // Tính tổng tiền trước
                            decimal totalAmount = receipt.Details?.Sum(d => (decimal)d.Quantity * d.UnitPrice) ?? 0m;

                            // Bảng chi tiết
                        column.Item().Table(table =>
                        {
                                // Định nghĩa cột
                            table.ColumnsDefinition(columns =>
                            {
                                    columns.ConstantColumn(40); // Số TT
                                    columns.RelativeColumn(3f); // Tên, nhãn hiệu, quy cách, phẩm chất
                                    columns.RelativeColumn(1.5f); // Mã số
                                    columns.RelativeColumn(1.2f); // Đơn vị tính
                                    columns.RelativeColumn(1.5f); // Số lượng (Theo chứng từ)
                                    columns.RelativeColumn(1.5f); // Số lượng (Thực nhập)
                                    columns.RelativeColumn(1.5f); // Đơn giá
                                    columns.RelativeColumn(1.8f); // Thành tiền
                                });

                                // Header row 1
                            table.Header(header =>
                            {
                                    header.Cell().Element(ReceiptHeaderCellStyle).Column(col =>
                                    {
                                        col.Item().Text("Số").Bold().FontSize(10).FontFamily("Times New Roman");
                                        col.Item().Text("TT").Bold().FontSize(10).FontFamily("Times New Roman");
                                    });
                                    header.Cell().Element(ReceiptHeaderCellStyle).Column(col =>
                                    {
                                        col.Item().Text("Tên, nhãn hiệu,").Bold().FontSize(10).FontFamily("Times New Roman");
                                        col.Item().Text("quy cách, phẩm chất").Bold().FontSize(10).FontFamily("Times New Roman");
                                    });
                                    header.Cell().Element(ReceiptHeaderCellStyle).Text("Mã số").Bold().FontSize(10).FontFamily("Times New Roman");
                                    header.Cell().Element(ReceiptHeaderCellStyle).Text("Đơn vị").Bold().FontSize(10).FontFamily("Times New Roman");
                                    header.Cell().Element(ReceiptHeaderCellStyle).Column(col =>
                                    {
                                        col.Item().Text("Số lượng").Bold().FontSize(10).FontFamily("Times New Roman");
                                        col.Item().PaddingTop(2).Row(subRow =>
                                        {
                                            subRow.RelativeItem().Text("Theo").FontSize(9).FontFamily("Times New Roman");
                                            subRow.RelativeItem().Text("chứng từ").FontSize(9).FontFamily("Times New Roman");
                                        });
                                    });
                                    header.Cell().Element(ReceiptHeaderCellStyle).Column(col =>
                                    {
                                        col.Item().PaddingTop(20).Text("Thực").FontSize(9).FontFamily("Times New Roman");
                                        col.Item().Text("nhập").FontSize(9).FontFamily("Times New Roman");
                                    });
                                    header.Cell().Element(ReceiptHeaderCellStyle).Text("Đơn giá").Bold().FontSize(10).FontFamily("Times New Roman");
                                    header.Cell().Element(ReceiptHeaderCellStyle).Text("Thành tiền").Bold().FontSize(10).FontFamily("Times New Roman");
                            });

                            // Data rows
                                int stt = 1;
                                foreach (var detail in receipt.Details ?? Enumerable.Empty<StockReceiptDetail>())
                                {
                                    var quantity = (decimal)detail.Quantity;
                                    var amount = quantity * detail.UnitPrice;

                                    table.Cell().Element(ReceiptDataCellStyle).Text(stt.ToString()).FontSize(10).FontFamily("Times New Roman");
                                    table.Cell().Element(ReceiptDataCellStyle).Column(col =>
                                    {
                                        col.Item().Text(detail.Material?.Name ?? "").FontSize(10).FontFamily("Times New Roman");
                                        if (!string.IsNullOrEmpty(detail.Specification))
                                        {
                                            col.Item().Text($"Quy cách: {detail.Specification}").FontSize(9).FontColor(Colors.Grey.Darken1).FontFamily("Times New Roman");
                                        }
                                    });
                                    table.Cell().Element(ReceiptDataCellStyle).Text(detail.Material?.Code ?? "").FontSize(10).FontFamily("Times New Roman");
                                    table.Cell().Element(ReceiptDataCellStyle).Text(detail.Unit ?? detail.Material?.Unit ?? "").FontSize(10).FontFamily("Times New Roman");
                                    table.Cell().Element(ReceiptDataCellStyle).AlignRight().Text(FormatQty(quantity)).FontSize(10).FontFamily("Times New Roman");
                                    table.Cell().Element(ReceiptDataCellStyle).AlignRight().Text(FormatQty(quantity)).FontSize(10).FontFamily("Times New Roman");
                                    table.Cell().Element(ReceiptDataCellStyle).AlignRight().Text(FormatMoneyNoUnit(detail.UnitPrice)).FontSize(10).FontFamily("Times New Roman");
                                    table.Cell().Element(ReceiptDataCellStyle).AlignRight().Text(FormatMoneyNoUnit(amount)).FontSize(10).Bold().FontFamily("Times New Roman");

                                    stt++;
                                }

                                // Dòng tổng "Cộng"
                                table.Cell().Element(ReceiptDataCellStyle).Text("Cộng").FontSize(10).Bold().FontFamily("Times New Roman");
                                table.Cell().Element(ReceiptDataCellStyle).Text("x").FontSize(10).AlignCenter().FontFamily("Times New Roman");
                                table.Cell().Element(ReceiptDataCellStyle).Text("x").FontSize(10).AlignCenter().FontFamily("Times New Roman");
                                table.Cell().Element(ReceiptDataCellStyle).Text("x").FontSize(10).AlignCenter().FontFamily("Times New Roman");
                                table.Cell().Element(ReceiptDataCellStyle).AlignRight().Text(FormatQty(receipt.Details?.Sum(d => (decimal)d.Quantity) ?? 0m)).FontSize(10).Bold().FontFamily("Times New Roman");
                                table.Cell().Element(ReceiptDataCellStyle).AlignRight().Text(FormatQty(receipt.Details?.Sum(d => (decimal)d.Quantity) ?? 0m)).FontSize(10).Bold().FontFamily("Times New Roman");
                                table.Cell().Element(ReceiptDataCellStyle).Text("x").FontSize(10).AlignCenter().FontFamily("Times New Roman");
                                table.Cell().Element(ReceiptDataCellStyle).AlignRight().Text(FormatMoneyNoUnit(totalAmount)).FontSize(10).Bold().FontFamily("Times New Roman");
                            });

                            column.Item().PaddingTop(15);

                            // Tổng số tiền (viết bằng chữ)
                            column.Item().Row(row =>
                            {
                                row.AutoItem().Text("Tổng số tiền (viết bằng chữ): ").FontSize(10).FontFamily("Times New Roman");
                                row.RelativeItem().Text(NumberToVietnameseWords(totalAmount)).FontSize(10).Bold().FontFamily("Times New Roman");
                            });

                            column.Item().PaddingTop(30);

                            // Phần chữ ký
                            column.Item().Row(row =>
                            {
                                row.RelativeItem(); // Spacer
                                row.AutoItem().Column(signCol =>
                                {
                                    signCol.Item().Text($"Ngày {DateTime.Now:dd} tháng {DateTime.Now:MM} năm {DateTime.Now:yyyy}").FontSize(10).AlignRight().FontFamily("Times New Roman");
                                });
                            });

                            column.Item().PaddingTop(20).Row(row =>
                            {
                                // Người lập
                                row.RelativeItem().Column(col =>
                                {
                                    col.Item().AlignCenter().Text("Người lập").FontSize(10).Bold().FontFamily("Times New Roman");
                                    col.Item().PaddingTop(30).AlignCenter().Text("(Ký, họ tên)").FontSize(9).Italic().FontColor(Colors.Grey.Darken1).FontFamily("Times New Roman");
                                    col.Item().PaddingTop(5).AlignCenter().Text(receipt.CreatedBy?.FullName ?? "").FontSize(10).FontFamily("Times New Roman");
                                });

                                // Người giao hàng
                                row.RelativeItem().Column(col =>
                                {
                                    col.Item().AlignCenter().Text("Người giao hàng").FontSize(10).Bold().FontFamily("Times New Roman");
                                    col.Item().PaddingTop(30).AlignCenter().Text("(Ký, họ tên)").FontSize(9).Italic().FontColor(Colors.Grey.Darken1).FontFamily("Times New Roman");
                                    col.Item().PaddingTop(5).AlignCenter().Text(receipt.DeliveredByName ?? "").FontSize(10).FontFamily("Times New Roman");
                                });

                                // Thủ kho
                                row.RelativeItem().Column(col =>
                                {
                                    col.Item().AlignCenter().Text("Thủ kho").FontSize(10).Bold().FontFamily("Times New Roman");
                                    col.Item().PaddingTop(30).AlignCenter().Text("(Ký, họ tên)").FontSize(9).Italic().FontColor(Colors.Grey.Darken1).FontFamily("Times New Roman");
                                });

                                // Kế toán trưởng
                                row.RelativeItem().Column(col =>
                                {
                                    col.Item().AlignCenter().Text("Kế toán trưởng").FontSize(10).Bold().FontFamily("Times New Roman");
                                    col.Item().PaddingTop(2).AlignCenter().Text("(Hoặc phụ trách bộ phận").FontSize(8).Italic().FontColor(Colors.Grey.Darken1).FontFamily("Times New Roman");
                                    col.Item().AlignCenter().Text("có nhu cầu nhập)").FontSize(8).Italic().FontColor(Colors.Grey.Darken1).FontFamily("Times New Roman");
                                    col.Item().PaddingTop(20).AlignCenter().Text("(Ký, họ tên)").FontSize(9).Italic().FontColor(Colors.Grey.Darken1).FontFamily("Times New Roman");
                    });
            });
                        });
                });
            }
        });

        var pdfBytes = document.GeneratePdf();
        var fileName = $"PhieuNhap_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
        return File(pdfBytes, "application/pdf", fileName);
    }

    private static IContainer HeaderCellStyle(IContainer container)
    {
        return container
            .Border(1)
            .BorderColor(Colors.Blue.Darken2)
            .PaddingVertical(12)
            .PaddingHorizontal(10)
            .Background(Colors.Blue.Darken3)
            .AlignMiddle()
            .AlignCenter();
    }

    private static IContainer DataCellStyle(IContainer container, bool isEvenRow)
    {
        return container
            .Border(1)
            .BorderColor(Colors.Grey.Lighten1)
            .PaddingVertical(10)
            .PaddingHorizontal(8)
            .Background(isEvenRow ? Colors.White : Colors.Grey.Lighten4)
            .AlignMiddle();
    }

    private static IContainer ReceiptHeaderCellStyle(IContainer container)
    {
        return container
            .Border(1)
            .BorderColor(Colors.Black)
            .PaddingVertical(8)
            .PaddingHorizontal(6)
            .Background(Colors.White)
            .AlignMiddle()
            .AlignCenter();
    }

    private static IContainer ReceiptDataCellStyle(IContainer container)
    {
        return container
            .Border(1)
            .BorderColor(Colors.Black)
            .PaddingVertical(6)
            .PaddingHorizontal(4)
            .Background(Colors.White)
            .AlignMiddle();
    }

    private static string FormatQty(decimal v)
    {
        return (v % 1m == 0m) ? ((long)v).ToString() : v.ToString("0.##");
    }

    private static string FormatMoney(decimal v)
    {
        return v.ToString("N0") + " đ";
    }

    private static string FormatMoneyNoUnit(decimal v)
    {
        return v.ToString("N0");
    }

    private static string GetStatusVietnamese(DocumentStatus status)
    {
        return status switch
        {
            DocumentStatus.Moi => "Mới",
            DocumentStatus.DaXacNhan => "Đã xác nhận",
            DocumentStatus.DaNhapHang => "Đã nhập hàng",
            DocumentStatus.DaHuy => "Từ chối",
            _ => status.ToString()
        };
    }

    private static string NumberToVietnameseWords(decimal number)
    {
        if (number == 0) return "không đồng";

        string[] ones = { "", "một", "hai", "ba", "bốn", "năm", "sáu", "bảy", "tám", "chín", "mười", "mười một", "mười hai", "mười ba", "mười bốn", "mười lăm", "mười sáu", "mười bảy", "mười tám", "mười chín" };
        string[] tens = { "", "", "hai mươi", "ba mươi", "bốn mươi", "năm mươi", "sáu mươi", "bảy mươi", "tám mươi", "chín mươi" };
        string[] hundreds = { "", "một trăm", "hai trăm", "ba trăm", "bốn trăm", "năm trăm", "sáu trăm", "bảy trăm", "tám trăm", "chín trăm" };

        long integerPart = (long)number;
        long fractionalPart = (long)((number - integerPart) * 100);

        if (integerPart == 0) return "không đồng";

        string result = "";

        // Xử lý phần tỷ
        if (integerPart >= 1000000000)
        {
            long billions = integerPart / 1000000000;
            result += ConvertThreeDigits(billions, ones, tens, hundreds) + " tỷ ";
            integerPart %= 1000000000;
        }

        // Xử lý phần triệu
        if (integerPart >= 1000000)
        {
            long millions = integerPart / 1000000;
            result += ConvertThreeDigits(millions, ones, tens, hundreds) + " triệu ";
            integerPart %= 1000000;
        }

        // Xử lý phần nghìn
        if (integerPart >= 1000)
        {
            long thousands = integerPart / 1000;
            result += ConvertThreeDigits(thousands, ones, tens, hundreds) + " nghìn ";
            integerPart %= 1000;
        }

        // Xử lý phần còn lại
        if (integerPart > 0)
        {
            result += ConvertThreeDigits(integerPart, ones, tens, hundreds);
        }

        result = result.Trim() + " đồng";

        // Xử lý phần xu (nếu có)
        if (fractionalPart > 0)
        {
            result += " " + ConvertTwoDigits(fractionalPart, ones, tens) + " xu";
        }

        return char.ToUpper(result[0]) + result.Substring(1);
    }

    private static string ConvertThreeDigits(long number, string[] ones, string[] tens, string[] hundreds)
    {
        if (number == 0) return "";

        string result = "";
        long hundred = number / 100;
        long remainder = number % 100;

        if (hundred > 0)
        {
            result += hundreds[hundred] + " ";
        }

        if (remainder > 0)
        {
            result += ConvertTwoDigits(remainder, ones, tens);
        }

        return result.Trim();
    }

    private static string ConvertTwoDigits(long number, string[] ones, string[] tens)
    {
        if (number < 20)
        {
            return ones[number];
        }
        else
        {
            long ten = number / 10;
            long one = number % 10;
            string result = tens[ten];
            if (one > 0)
            {
                if (one == 1 && ten > 1)
                    result += " mốt";
                else if (one == 5 && ten > 1)
                    result += " lăm";
                else
                    result += " " + ones[one];
            }
            return result;
        }
    }

}

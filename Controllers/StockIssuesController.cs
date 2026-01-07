// using Microsoft.AspNetCore.Mvc;
// using Microsoft.EntityFrameworkCore;
// using Microsoft.AspNetCore.Mvc.Rendering;
// using MNBEMART.Data;
// using MNBEMART.Models;
// using MNBEMART.Services;

// public class StockIssuesController : Controller
// {
//     private readonly AppDbContext _db;
//     private readonly IStockService _stock;

//     public StockIssuesController(AppDbContext db, IStockService stock)
//     {
//         _db = db;
//         _stock = stock;
//     }

//     public async Task<IActionResult> Index()
//     {
//         var data = await _db.StockIssues
//             .Include(x => x.Warehouse)
//             .Include(x => x.CreatedBy)
//             .OrderByDescending(x => x.CreatedAt)
//             .ToListAsync();
//         return View(data);
//     }

//     public IActionResult Create()
//     {
//         ViewBag.WarehouseId = new SelectList(_db.Warehouses, "Id", "Name");
//         ViewBag.CreatedById = new SelectList(_db.Users, "Id", "FullName");
//         ViewBag.Materials = _db.Materials.OrderBy(m => m.Name).ToList();
//         return View();
//     }

//     // [HttpPost]
//     // [ValidateAntiForgeryToken]
//     // public async Task<IActionResult> Create(StockIssue issue, List<StockIssueDetail> details)
//     // {
//     //     if (!ModelState.IsValid)
//     //     {
//     //         ViewBag.WarehouseId = new SelectList(_db.Warehouses, "Id", "Name", issue.WarehouseId);
//     //         ViewBag.CreatedById = new SelectList(_db.Users, "Id", "FullName", issue.CreatedById);
//     //         ViewBag.Materials = _db.Materials.ToList();
//     //         return View(issue);
//     //     }

//     //     issue.CreatedAt = DateTime.Now;
//     //     issue.Status = DocumentStatus.ChoDuyet;
//     //     issue.Details = details
//     //         .Where(d => d.MaterialId > 0 && d.Quantity > 0)
//     //         .Select(d => { d.UnitPrice = Math.Round(d.UnitPrice, 2); return d; })
//     //         .ToList();

//     //     _db.StockIssues.Add(issue);
//     //     await _db.SaveChangesAsync();
//     //     return RedirectToAction(nameof(Index));
//     // }

//     [HttpPost]
//     [ValidateAntiForgeryToken]
//     public async Task<IActionResult> Create(StockIssue issue)
//     {
//         if (!ModelState.IsValid || issue?.Details == null || !issue.Details.Any(d => d.MaterialId > 0 && d.Quantity > 0))
//         {
//             ViewBag.WarehouseId = new SelectList(_db.Warehouses, "Id", "Name", issue?.WarehouseId);
//             ViewBag.CreatedById = new SelectList(_db.Users, "Id", "FullName", issue?.CreatedById);
//             ViewBag.Materials = _db.Materials.ToList();
//             ModelState.AddModelError("", "Vui lòng nhập ít nhất 1 dòng chi tiết hợp lệ.");
//             return View(issue);
//         }

//         issue.CreatedAt = DateTime.Now;
//         issue.Status = DocumentStatus.ChoDuyet;
//         issue.Details = issue.Details
//             .Where(d => d.MaterialId > 0 && d.Quantity > 0)
//             .Select(d => { d.UnitPrice = Math.Round(d.UnitPrice, 2); return d; })
//             .ToList();

//         _db.StockIssues.Add(issue);
//         await _db.SaveChangesAsync();
//         return RedirectToAction(nameof(Index));
//     }


//     // DUYỆT PHIẾU XUẤT
//     // [HttpPost]
//     // [ValidateAntiForgeryToken]
//     // public async Task<IActionResult> Approve(int id, int approvedById)
//     // {
//     //     using var tx = await _db.Database.BeginTransactionAsync();
//     //     try
//     //     {
//     //         var issue = await _db.StockIssues
//     //             .Include(r => r.Details)
//     //             .FirstOrDefaultAsync(r => r.Id == id);

//     //         if (issue == null) return NotFound();
//     //         if (issue.Status != DocumentStatus.ChoDuyet)
//     //             return BadRequest("Phiếu không ở trạng thái chờ duyệt.");

//     //         // giảm tồn kho
//     //         foreach (var d in issue.Details)
//     //         {
//     //             await _stock.DecreaseAsync(issue.WarehouseId, d.MaterialId, (decimal)d.Quantity);
//     //         }

//     //         issue.Status = DocumentStatus.DaXacNhan;
//     //         issue.ApprovedById = approvedById;
//     //         issue.ApprovedAt = DateTime.Now;

//     //         await _db.SaveChangesAsync();
//     //         await tx.CommitAsync();
//     //         return RedirectToAction(nameof(Index));
//     //     }
//     //     catch (Exception ex)
//     //     {
//     //         await tx.RollbackAsync();
//     //         return BadRequest($"Lỗi duyệt phiếu: {ex.Message}");
//     //     }
//     // }

//     // [HttpPost]
//     // [ValidateAntiForgeryToken]
//     // public async Task<IActionResult> Reject(int id, string note, int approvedById)
//     // {
//     //     var issue = await _db.StockIssues.FindAsync(id);
//     //     if (issue == null) return NotFound();
//     //     if (issue.Status != DocumentStatus.ChoDuyet)
//     //         return BadRequest("Phiếu không ở trạng thái chờ duyệt.");

//     //     issue.Status = DocumentStatus.TuChoi;
//     //     issue.ApprovedById = approvedById;
//     //     issue.ApprovedAt = DateTime.Now;
//     //     issue.Note = note;
//     //     await _db.SaveChangesAsync();
//     //     return RedirectToAction(nameof(Index));
//     //    }
//     public async Task<IActionResult> Details(int id)
//     {
//         var issue = await _db.StockIssues
//             .Include(r => r.Warehouse)
//             .Include(r => r.CreatedBy)
//             .Include(r => r.Details).ThenInclude(d => d.Material)
//             .FirstOrDefaultAsync(r => r.Id == id);
//         if (issue == null) return NotFound();
//         return View(issue);
//     }

//     [HttpPost]
// [ValidateAntiForgeryToken]
// public async Task<IActionResult> Approve(int id, int approvedById)
// {
//     using var tx = await _db.Database.BeginTransactionAsync();
//     try
//     {
//         var issue = await _db.StockIssues
//             .Include(r => r.Details)
//             .FirstOrDefaultAsync(r => r.Id == id);

//         if (issue == null) return NotFound();
//         if (issue.Status != DocumentStatus.ChoDuyet)
//             return BadRequest("Phiếu không ở trạng thái chờ duyệt.");

//         foreach (var d in issue.Details)
//             await _stock.DecreaseAsync(issue.WarehouseId, d.MaterialId, (decimal)d.Quantity);

//         await _stock.SaveAsync(); // <- lưu tồn kho
//         issue.Status = DocumentStatus.DaXacNhan;
//         issue.ApprovedById = approvedById;
//         issue.ApprovedAt = DateTime.Now;

//         await _db.SaveChangesAsync(); // <- lưu trạng thái phiếu
//         await tx.CommitAsync();

//         return RedirectToAction(nameof(Details), new { id });
//     }
//     catch (Exception ex)
//     {
//         await tx.RollbackAsync();
//         ModelState.AddModelError("", $"Lỗi duyệt phiếu: {ex.Message}");
//         return RedirectToAction(nameof(Details), new { id });
//     }
// }

// [HttpPost]
// [ValidateAntiForgeryToken]
// public async Task<IActionResult> Reject(int id, int approvedById, string note)
// {
//     var issue = await _db.StockIssues.FindAsync(id);
//     if (issue == null) return NotFound();
//     if (issue.Status != DocumentStatus.ChoDuyet)
//         return BadRequest("Phiếu không ở trạng thái chờ duyệt.");

//     issue.Status = DocumentStatus.TuChoi;
//     issue.ApprovedById = approvedById;
//     issue.ApprovedAt = DateTime.Now;
//     issue.Note = note;

//     await _db.SaveChangesAsync();
//     return RedirectToAction(nameof(Details), new { id });
// }

// }

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using MNBEMART.Data;
using MNBEMART.Models;
using MNBEMART.Services;
using System.Security.Claims;
using MNBEMART.Filters;
using MNBEMART.Extensions;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.IO;

public class StockIssuesController : Controller
{
    private readonly AppDbContext _db;
    private readonly IStockService _stock;
    private readonly IDocumentNumberingService _num;
    private readonly INotificationService _notificationService;
    private readonly ICostingService _costing;

    public StockIssuesController(AppDbContext db, IStockService stock, IDocumentNumberingService num, INotificationService notificationService, ICostingService costing)
    {
        _db = db; _stock = stock; _num = num; _notificationService = notificationService; _costing = costing;
    }

    [Authorize]
    [RequirePermission("Issues","Read")]
    public async Task<IActionResult> Index(string? q, DocumentStatus? status, int? warehouseId, int page = 1, int pageSize = 10)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 5, 100);
        var query = _db.StockIssues.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(r => r.IssueNumber!.Contains(q) || (r.Note ?? "").Contains(q));
        if (status.HasValue)
            query = query.Where(r => r.Status == status);
        if (warehouseId.HasValue)
            query = query.Where(r => r.WarehouseId == warehouseId);

        query = query
            .Include(r => r.Warehouse)
            .Include(r => r.CreatedBy)
            .Include(r => r.Details).ThenInclude(d => d.Material);

        var pagedResult = await query
            .OrderByDescending(r => r.CreatedAt)
            .ToPagedResultAsync(page, pageSize);

        var list = pagedResult.Items.ToList();
        var totalItems = pagedResult.TotalItems;

        // Load thông tin phân bổ lô cho các phiếu đã xuất hàng
        var issuedReceipts = list.Where(r => r.Status == DocumentStatus.DaXuatHang).ToList();
        if (issuedReceipts.Any())
        {
            var allDetailIds = issuedReceipts.SelectMany(r => r.Details.Select(d => d.Id)).ToList();
            var allocations = await _db.StockIssueAllocations
                .Include(a => a.StockLot)
                .Where(a => allDetailIds.Contains(a.StockIssueDetailId))
                .ToListAsync();
            
            // Xử lý duplicate: chỉ lấy record đầu tiên, bỏ qua các record trùng
            var seen = new HashSet<string>();
            var result = new List<StockIssueAllocation>();
            
            foreach (var alloc in allocations)
            {
                var key = $"{alloc.StockIssueDetailId}_{alloc.StockLotId}";
                if (!seen.Contains(key))
                {
                    seen.Add(key);
                    result.Add(alloc);
                }
                // Nếu đã có rồi thì bỏ qua (không làm gì)
            }
            
            ViewBag.LotAllocations = result;
        }

        ViewBag.Page = pagedResult.Page;
        ViewBag.PageSize = pagedResult.PageSize;
        ViewBag.TotalItems = pagedResult.TotalItems;
        ViewBag.TotalPages = pagedResult.TotalPages;
        ViewBag.PagedResult = pagedResult;

        // chips - Optimized: Single query with grouping instead of 5 separate queries
        var statusCounts = await _db.StockIssues.AsNoTracking()
            .GroupBy(x => x.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();
        
        ViewBag.CountAll = statusCounts.Sum(x => x.Count);
        ViewBag.CountMoi = statusCounts.FirstOrDefault(x => x.Status == DocumentStatus.Moi)?.Count ?? 0;
        ViewBag.CountXacNhan = statusCounts.FirstOrDefault(x => x.Status == DocumentStatus.DaXacNhan)?.Count ?? 0;
        ViewBag.CountXuat = statusCounts.FirstOrDefault(x => x.Status == DocumentStatus.DaXuatHang)?.Count ?? 0;
        ViewBag.CountHuy = statusCounts.FirstOrDefault(x => x.Status == DocumentStatus.DaHuy)?.Count ?? 0;

        ViewBag.WarehouseOptions = new SelectList(
            await _db.Warehouses.AsNoTracking().OrderBy(w => w.Name).ToListAsync(),
            "Id", "Name", warehouseId);

        // cho modal tạo nhanh
        ViewBag.Materials = await _db.Materials.AsNoTracking().OrderBy(m => m.Name).ToListAsync();

        // tổng (theo dataset đang lọc)
        decimal sumQty = 0, sumAmt = 0;
        foreach (var r in list)
        {
            var qtty = r.Details.Sum(d => (decimal)d.Quantity);
            var amt = r.Details.Sum(d => (decimal)d.Quantity * d.UnitPrice);
            sumQty += qtty; sumAmt += amt;
        }
        ViewBag.SumQty = sumQty; ViewBag.SumAmt = sumAmt;

        return View(list);
    }

    [HttpGet]
    [Authorize]
    [RequirePermission("Issues","Read")]
    public async Task<IActionResult> ExportCsv(string? q, DocumentStatus? status, int? warehouseId)
    {
        var query = _db.StockIssues.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(r => r.IssueNumber!.Contains(q) || (r.Note ?? "").Contains(q));
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
            Escape(r.IssueNumber),
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
    
    return File(bytes, "text/csv; charset=utf-8", $"StockIssues_{DateTime.Now:yyyyMMddHHmm}.csv");

        static string Escape(string? v) => "\"" + (v ?? "").Replace("\"", "\"\"") + "\"";
    }

    [HttpGet]
    [Authorize]
    [RequirePermission("Issues","Read")]
public async Task<IActionResult> Print(string? q, DocumentStatus? status, int? warehouseId)
    {
        var query = _db.StockIssues.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(r => r.IssueNumber!.Contains(q) || (r.Note ?? "").Contains(q));
        if (status.HasValue) query = query.Where(r => r.Status == status);
        if (warehouseId.HasValue) query = query.Where(r => r.WarehouseId == warehouseId);
        query = query.Include(r => r.Warehouse).Include(r => r.CreatedBy).Include(r => r.Details);

        var list = await query.OrderByDescending(r => r.CreatedAt).ToListAsync();
        return View("Print", list);
    }

    [HttpGet]
    [Authorize]
    [RequirePermission("Issues","Read")]
    public async Task<IActionResult> PrintOne(int id)
    {
        var list = await _db.StockIssues.AsNoTracking()
            .Include(r => r.Warehouse).Include(r => r.CreatedBy).Include(r => r.Details)
            .Where(r => r.Id == id)
            .ToListAsync();
        return View("Print", list);
    }

    [HttpGet]
    [Authorize]
    [RequirePermission("Issues","Read")]
    public async Task<IActionResult> PrintSelected([FromQuery] int[] ids)
    {
        if (ids == null || ids.Length == 0)
            return RedirectToAction(nameof(Print));

        var list = await _db.StockIssues.AsNoTracking()
            .Include(r => r.Warehouse).Include(r => r.CreatedBy).Include(r => r.Details)
            .Where(r => ids.Contains(r.Id))
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

        return View("Print", list);
    }

    [HttpGet]
    [Authorize]
[RequirePermission("Issues","Create")]
public IActionResult DownloadTemplate()
    {
        var csv = "WarehouseId,Note,Details\n" +
                  "1,Xuat hang mau,10:3:12000|11:1:15000";
        var bytes = System.Text.Encoding.UTF8.GetBytes(csv);
        return File(bytes, "text/csv; charset=utf-8", "issue_import_template.csv");
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ValidateAntiForgeryToken]
[RequirePermission("Issues","Create")]
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

        var uidStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        int.TryParse(uidStr, out var uid);
        int created = 0;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            var cols = line.Split(',');
            if (cols.Length < 3) continue;
            if (!int.TryParse(cols[0].Trim(), out var wh)) continue;
            var note = cols[1].Trim();
            var detailsStr = cols[2].Trim();

            var m = new StockIssue
            {
                WarehouseId = wh,
                CreatedById = uid,
                CreatedAt = DateTime.Now,
                Status = DocumentStatus.Moi,
                IssueNumber = await _num.NextAsync("StockIssue", wh),
                Note = note,
                Details = new List<StockIssueDetail>()
            };

            foreach (var part in detailsStr.Split('|', StringSplitOptions.RemoveEmptyEntries))
            {
                var p = part.Split(':');
                if (p.Length < 3) continue;
                if (!int.TryParse(p[0], out var mat)) continue;
                if (!decimal.TryParse(p[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var q2)) continue;
                if (!decimal.TryParse(p[2], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var price)) continue;
                m.Details.Add(new StockIssueDetail { MaterialId = mat, Quantity = (double)q2, UnitPrice = Math.Round(price, 2) });
            }
            if (m.Details.Any())
            {
                _db.StockIssues.Add(m);
                created++;
            }
        }
        await _db.SaveChangesAsync();
        TempData["Msg"] = $"Đã nhập {created} phiếu.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SeedDemo(int count = 20)
    {
        var mats = await _db.Materials.AsNoTracking().ToListAsync();
        var whs  = await _db.Warehouses.AsNoTracking().ToListAsync();
        if (!mats.Any() || !whs.Any())
        {
            TempData["Error"] = "Cần có danh mục Kho và Nguyên liệu trước.";
            return RedirectToAction(nameof(Index));
        }

        var uidStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        int.TryParse(uidStr, out var uid);
        var rnd = new Random();
        var today = DateTime.Today;
        int created = 0, issued = 0;

        using var tx = await _db.Database.BeginTransactionAsync();
        for (int i = 0; i < count; i++)
        {
            var wh = whs[rnd.Next(whs.Count)];
            var lines = rnd.Next(1, 4);
            var chosen = mats.OrderBy(_ => rnd.Next()).Take(lines).ToList();

            var issue = new StockIssue
            {
                WarehouseId = wh.Id,
                CreatedById = uid,
                CreatedAt   = DateTime.Now,
                Status      = DocumentStatus.Moi,
                IssueNumber = await _num.NextAsync("StockIssue", wh.Id),
                Note        = "Dữ liệu mẫu",
                Details     = new List<StockIssueDetail>()
            };

            foreach (var m in chosen)
            {
                var qty = rnd.Next(1, 6);
                var price = m.SellingPrice ?? m.PurchasePrice ?? 0m;
                issue.Details.Add(new StockIssueDetail
                {
                    MaterialId = m.Id,
                    Quantity   = qty,
                    Unit       = m.Unit,
                    UnitPrice  = Math.Round(price, 2),
                    Specification = m.Specification
                });
            }

            _db.StockIssues.Add(issue);
            await _db.SaveChangesAsync();
            created++;

            // Ngẫu nhiên chuyển trạng thái
            var roll = rnd.NextDouble();
            if (roll < 0.33)
            {
                // Giữ Mới
            }
            else if (roll < 0.66)
            {
                issue.Status = DocumentStatus.DaXacNhan;
                await _db.SaveChangesAsync();
            }
            else
            {
                // Cố gắng xuất hàng nếu đủ tồn
                bool ok = true;
                foreach (var d in issue.Details)
                {
                    var stock = await _db.Stocks.AsNoTracking()
                        .FirstOrDefaultAsync(s => s.WarehouseId == issue.WarehouseId && s.MaterialId == d.MaterialId);
                    var onHand = stock?.Quantity ?? 0m;
                    if (onHand < (decimal)d.Quantity) { ok = false; break; }
                }

                if (ok)
                {
                    foreach (var d in issue.Details)
                    {
                        await _stock.DecreaseAsync(issue.WarehouseId, d.MaterialId, (decimal)d.Quantity);
                        var sb = await _db.Set<StockBalance>()
                            .FirstOrDefaultAsync(x => x.WarehouseId == issue.WarehouseId && x.MaterialId == d.MaterialId && x.Date == today);
                        if (sb == null)
                        {
                            sb = new StockBalance
                            {
                                WarehouseId = issue.WarehouseId,
                                MaterialId  = d.MaterialId,
                                Date        = today,
                                InQty = 0, InValue = 0, OutQty = 0, OutValue = 0,
                                UpdatedAt   = DateTime.Now
                            };
                            _db.Add(sb);
                        }
                        sb.OutQty   += (decimal)d.Quantity;
                        sb.OutValue += (decimal)d.Quantity * d.UnitPrice;
                        sb.UpdatedAt = DateTime.Now;
                    }
                    await _stock.SaveAsync();
                    issue.Status = DocumentStatus.DaXuatHang;
                    await _db.SaveChangesAsync();
                    issued++;
                }
                else
                {
                    issue.Status = DocumentStatus.DaXacNhan;
                    await _db.SaveChangesAsync();
                }
            }
        }
        await tx.CommitAsync();

        TempData["Msg"] = $"Đã tạo {created} phiếu, đã xuất {issued}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [Authorize]
[RequirePermission("Issues","Create")]
    public async Task<IActionResult> Create()
    {
        // Chặn Admin tự tạo phiếu xuất
        if (string.Equals(User.FindFirstValue(ClaimTypes.Role), "Admin", StringComparison.OrdinalIgnoreCase))
            return Forbid();
        ViewBag.WarehouseId = new SelectList(
            await _db.Warehouses.AsNoTracking().OrderBy(w => w.Name).ToListAsync(),
            "Id", "Name"
        );
        ViewBag.Materials = await _db.Materials.AsNoTracking().OrderBy(m => m.Name).ToListAsync();

        // Gợi ý set sẵn người lập
        var uidStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        int.TryParse(uidStr, out var uid);
        var model = new StockIssue { CreatedById = uid, CreatedAt = DateTime.Now };
return View(model);
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateIssue(StockIssue issue, IFormFile EvidenceImage)
    {
        // Chặn Admin tự tạo phiếu xuất
        if (string.Equals(User.FindFirstValue(ClaimTypes.Role), "Admin", StringComparison.OrdinalIgnoreCase))
            return Forbid();
        
        // == code giống CreateIssue hiện tại ==
        issue.CreatedAt = DateTime.Now;
        issue.Status = DocumentStatus.Moi;
        issue.IssueNumber = await _num.NextAsync("StockIssue", issue.WarehouseId);
        issue.Note ??= string.Empty;                // tránh NULL (nếu DB chưa migrate)
        
        // Auto-generate ReferenceDocumentNumber
        issue.ReferenceDocumentNumber = await _num.NextAsync("StockIssueReference", issue.WarehouseId);
        
        // BẮT BUỘC ảnh minh chứng + lưu file
        if (EvidenceImage == null || EvidenceImage.Length == 0 || !(EvidenceImage.ContentType?.StartsWith("image/") ?? false))
        {
            ModelState.AddModelError("EvidenceImage", "Vui lòng tải lên hình ảnh minh chứng (PNG/JPG/GIF/WEBP).");
        }
        else
        {
            // Validate file size (max 10MB)
            if (EvidenceImage.Length > 10 * 1024 * 1024)
            {
                ModelState.AddModelError("EvidenceImage", "Kích thước file không được vượt quá 10MB.");
            }
            else
            {
                var uploadsRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "issues");
                Directory.CreateDirectory(uploadsRoot);
                var ext = Path.GetExtension(EvidenceImage.FileName);
                var safeName = $"{DateTime.Now:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}{ext}";
                var filePath = Path.Combine(uploadsRoot, safeName);
                using (var fs = new FileStream(filePath, FileMode.Create))
                    await EvidenceImage.CopyToAsync(fs);
                issue.AttachedDocuments = $"/uploads/issues/{safeName}";
            }
        }

        var uidStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (int.TryParse(uidStr, out var uid)) issue.CreatedById = uid;
        else ModelState.AddModelError("", "Không xác định được người lập.");

        // Validation các trường bắt buộc ở form chính
        if (issue.WarehouseId <= 0)
            ModelState.AddModelError(nameof(StockIssue.WarehouseId), "Vui lòng chọn kho xuất.");

        if (string.IsNullOrWhiteSpace(issue.ReceivedByName))
            ModelState.AddModelError(nameof(StockIssue.ReceivedByName), "Vui lòng nhập người nhận.");

        ModelState.Remove(nameof(StockIssue.IssueNumber));
        ModelState.Remove(nameof(StockIssue.Status));
        ModelState.Remove(nameof(StockIssue.CreatedAt));
        ModelState.Remove(nameof(StockIssue.ApprovedById));
        ModelState.Remove(nameof(StockIssue.ApprovedAt));
        ModelState.Remove(nameof(StockIssue.Note));
        ModelState.Remove(nameof(StockIssue.Warehouse));
        ModelState.Remove(nameof(StockIssue.CreatedBy));
        ModelState.Remove(nameof(StockIssue.AttachedDocuments));
        ModelState.Remove(nameof(StockIssue.ReferenceDocumentNumber));

        var detailsList = (issue.Details ?? new List<StockIssueDetail>())
            .Where(d => d.MaterialId > 0 && d.Quantity > 0)
            .GroupBy(d => d.MaterialId)
            .Select(g =>
            {
                var first = g.First();
                return new StockIssueDetail
                {
                    MaterialId = g.Key,
                    Quantity = g.Sum(x => x.Quantity),
                    Unit = first.Unit,
                    UnitPrice = Math.Round(first.UnitPrice, 2),
                    Specification = first.Specification
                };
            }).ToList();

        issue.Details = detailsList;

        // Validation các trường bắt buộc trong detail
        for (int i = 0; i < detailsList.Count; i++)
        {
            ModelState.Remove($"Details[{i}].Material");
            ModelState.Remove($"Details[{i}].StockIssue");
            
            var detail = detailsList[i];
            if (detail.MaterialId <= 0)
                ModelState.AddModelError($"Details[{i}].MaterialId", $"Dòng {i + 1}: Vui lòng chọn sản phẩm.");
            if (detail.Quantity <= 0)
                ModelState.AddModelError($"Details[{i}].Quantity", $"Dòng {i + 1}: Số lượng phải lớn hơn 0.");
            if (detail.UnitPrice < 0)
                ModelState.AddModelError($"Details[{i}].UnitPrice", $"Dòng {i + 1}: Giá xuất phải lớn hơn hoặc bằng 0.");
            if (string.IsNullOrWhiteSpace(detail.Unit))
                ModelState.AddModelError($"Details[{i}].Unit", $"Dòng {i + 1}: Vui lòng nhập đơn vị tính.");
        }

        if (!detailsList.Any())
            ModelState.AddModelError("", "Vui lòng nhập ít nhất 1 dòng chi tiết hợp lệ với đầy đủ thông tin (Sản phẩm, SL, Giá xuất, ĐVT).");
            
        // Sau khi đã gom merge details & Validate ModelState…
        if (!ModelState.IsValid)
        {
            ViewBag.WarehouseId = new SelectList(_db.Warehouses, "Id", "Name", issue.WarehouseId);
            ViewBag.Materials = _db.Materials.OrderBy(m => m.Name).ToList();
            return View("Create", issue);
        }

        // ==== CHẶN VƯỢT TỒN (server-side) ====
        var wid = issue.WarehouseId;
        foreach (var d in issue.Details)
        {
            var stock = await _db.Stocks.AsNoTracking()
                .FirstOrDefaultAsync(s => s.WarehouseId == wid && s.MaterialId == d.MaterialId);

            var onHand = stock?.Quantity ?? 0m;
            if (onHand < (decimal)d.Quantity)
            {
                var mat = await _db.Materials.AsNoTracking().FirstOrDefaultAsync(x => x.Id == d.MaterialId);
                ModelState.AddModelError("",
                    $"Vật tư {(mat?.Code + " " + mat?.Name) ?? ("ID=" + d.MaterialId)} ở kho hiện còn {onHand}, không đủ để xuất {d.Quantity}.");
            }
        }

        if (!ModelState.IsValid)
        {
            ViewBag.WarehouseId = new SelectList(_db.Warehouses, "Id", "Name", issue.WarehouseId);
            ViewBag.Materials = _db.Materials.OrderBy(m => m.Name).ToList();
            return View("Create", issue);
        }

        _db.StockIssues.Add(issue);
        await _db.SaveChangesAsync();
        
        // Tạo thông báo cho Admin khi user tạo phiếu
        try
        {
            var adminUserIds = await GetAdminUserIdsAsync();
            var warehouse = await _db.Warehouses.FindAsync(issue.WarehouseId);
            // Load CreatedBy để lấy tên người tạo
            await _db.Entry(issue).Reference(i => i.CreatedBy).LoadAsync();
            var createdByName = issue.CreatedBy?.FullName ?? User.Identity?.Name ?? "System";
            
            if (adminUserIds.Any())
            {
                await _notificationService.CreateNotificationForUsersAsync(
                    NotificationType.Issue,
                    issue.Id,
                    $"Phiếu xuất mới: {issue.IssueNumber}",
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
        var issue = await _db.StockIssues
            .Include(r => r.Warehouse)
            .Include(r => r.CreatedBy)
            .Include(r => r.ApprovedBy)
            .Include(r => r.Details).ThenInclude(d => d.Material)
            .FirstOrDefaultAsync(r => r.Id == id);
        if (issue == null) return NotFound();

        // Nếu phiếu đã xuất hàng, load thêm thông tin phân bổ lô
        if (issue.Status == DocumentStatus.DaXuatHang)
        {
            var detailIds = issue.Details.Select(d => d.Id).ToList();
            var allocations = await _db.StockIssueAllocations
                .Include(a => a.StockLot)
                .Where(a => detailIds.Contains(a.StockIssueDetailId))
                .ToListAsync();

            // Xử lý duplicate: chỉ lấy record đầu tiên, bỏ qua các record trùng
            var seen = new HashSet<string>();
            var result = new List<StockIssueAllocation>();
            
            foreach (var alloc in allocations)
            {
                var key = $"{alloc.StockIssueDetailId}_{alloc.StockLotId}";
                if (!seen.Contains(key))
                {
                    seen.Add(key);
                    result.Add(alloc);
                }
                // Nếu đã có rồi thì bỏ qua (không làm gì)
            }

            // Gắn vào ViewBag để hiển thị
            ViewBag.LotAllocations = result;
        }

        return View(issue);
    }



    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ValidateAntiForgeryToken]
[RequirePermission("Issues","Update")]
    public async Task<IActionResult> Approve(int id)
    {
        var r = await _db.StockIssues.Include(x => x.Details).FirstOrDefaultAsync(x => x.Id == id);
        if (r == null) return NotFound();
        if (r.Status != DocumentStatus.Moi)
            return BadRequest("Chỉ xác nhận phiếu ở trạng thái Mới.");

        r.Status = DocumentStatus.DaXacNhan;
        r.ApprovedAt = DateTime.Now;
        var uidStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (int.TryParse(uidStr, out var uid)) r.ApprovedById = uid;

        await _db.SaveChangesAsync();
        
        // Thông báo cho user tạo phiếu
        if (r.CreatedById > 0)
        {
            await _notificationService.CreateNotificationAsync(
                NotificationType.Issue,
                r.Id,
                $"Phiếu xuất đã được xác nhận: {r.IssueNumber}",
                $"Phiếu xuất của bạn đã được xác nhận bởi admin",
                r.CreatedById,
                NotificationPriority.Normal
            );
        }
        
        return RedirectToAction(nameof(Index)); // hoặc Details
    }

    // Xuất kho: trừ tồn + ghi sổ StockBalance
    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ValidateAntiForgeryToken]
[ActionName("IssuePost")]  // tránh trùng tên
[RequirePermission("Issues","Update")]
    public async Task<IActionResult> Issue(int id, string? allocationsJson)
    {
        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var r = await _db.StockIssues.Include(x => x.Details).FirstOrDefaultAsync(x => x.Id == id);
            if (r == null) return NotFound();
            if (r.Status != DocumentStatus.DaXacNhan)
                return BadRequest("Chỉ xuất kho phiếu ở trạng thái Đã xác nhận.");

            // Parse phân bổ lô từ client
            List<LotAllocationDto>? userAllocations = null;
            if (!string.IsNullOrWhiteSpace(allocationsJson))
            {
                try
                {
                    userAllocations = System.Text.Json.JsonSerializer.Deserialize<List<LotAllocationDto>>(allocationsJson);
                }
                catch { /* ignore invalid JSON */ }
            }

            // Kiểm tra tồn từng dòng ở kho đó
            foreach (var d in r.Details)
            {
                var stock = await _db.Stocks.AsNoTracking()
                    .FirstOrDefaultAsync(s => s.WarehouseId == r.WarehouseId && s.MaterialId == d.MaterialId);

                var onHand = stock?.Quantity ?? 0m;
                if (onHand < (decimal)d.Quantity)
                {
                    TempData["Error"] = $"Vật tư ID={d.MaterialId} không đủ tồn (có {onHand}, cần {d.Quantity}).";
                    return RedirectToAction(nameof(Index));
                }
            }

            // Trừ tồn + ghi sổ từng dòng
            var today = DateTime.Today;
            foreach (var d in r.Details)
            {
                List<(int lotId, decimal qty)> allocations;

                // Nếu client gửi allocations cho detail này -> dùng; không thì FEFO tự động
                var userAlloc = userAllocations?.Where(a => a.DetailId == d.Id).ToList();
                if (userAlloc?.Any() == true)
                {
                    allocations = userAlloc.Select(a => (a.LotId, (decimal)a.Qty)).ToList();
                }
                else
                {
                    // Fallback: tự động FEFO
                    allocations = await _stock.AllocateFromLotsFefoAsync(r.WarehouseId, d.MaterialId, (decimal)d.Quantity);
                }

                // Lưu phân bổ
                foreach (var (lotId, q) in allocations)
                {
                    _db.StockIssueAllocations.Add(new StockIssueAllocation
                    {
                        StockIssueDetailId = d.Id,
                        StockLotId = lotId,
                        Quantity = q
                    });
                }

                // Giảm tổng tồn bảng Stocks
                await _stock.DecreaseAsync(r.WarehouseId, d.MaterialId, (decimal)d.Quantity);

                // Ghi sổ phát sinh ngày
                var sb = await _db.Set<StockBalance>()
                    .FirstOrDefaultAsync(x => x.WarehouseId == r.WarehouseId
                                           && x.MaterialId == d.MaterialId
                                           && x.Date == today);
                if (sb == null)
                {
                    sb = new StockBalance
                    {
                        WarehouseId = r.WarehouseId,
                        MaterialId = d.MaterialId,
                        Date = today,
                        InQty = 0,
                        InValue = 0,
                        OutQty = 0,
                        OutValue = 0,
                        UpdatedAt = DateTime.Now
                    };
                    _db.Add(sb);
                }
                // Tính giá vốn (COGS) tự động
                var costPrice = await _costing.CalculateIssueCostAsync(r.WarehouseId, d.MaterialId, (decimal)d.Quantity, today);
                d.CostPrice = costPrice;  // Lưu giá vốn vào detail

                sb.OutQty += (decimal)d.Quantity;
                sb.OutValue += (decimal)d.Quantity * costPrice;  // Dùng giá vốn thay vì UnitPrice
                sb.UpdatedAt = DateTime.Now;
            }
            await _stock.SaveAsync();

            // Cập nhật trạng thái
            r.Status = DocumentStatus.DaXuatHang;
            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            TempData["OpenDetailId"] = id;
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            TempData["Error"] = $"Lỗi xuất hàng: {ex.Message}";
            return RedirectToAction(nameof(Index));
        }
    }

    // DTO cho phân bổ lô từ client
    public class LotAllocationDto
    {
        public int DetailId { get; set; }
        public int LotId { get; set; }
        public double Qty { get; set; }
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ValidateAntiForgeryToken]
[RequirePermission("Issues","Delete")]
    public async Task<IActionResult> Reject(int id, string? note)
    {
        using var tx = await _db.Database.BeginTransactionAsync();
        var r = await _db.StockIssues.Include(x => x.Details).FirstOrDefaultAsync(x => x.Id == id);
        if (r == null) return NotFound();
        if (r.Status == DocumentStatus.DaHuy) return BadRequest("Phiếu đã huỷ.");

        // Nếu đã xuất rồi -> CỘNG bù
        if (r.Status == DocumentStatus.DaXuatHang)
        {
            foreach (var d in r.Details)
                await _stock.IncreaseAsync(r.WarehouseId, d.MaterialId, (decimal)d.Quantity);
            await _stock.SaveAsync();

            // Ghi số đảo ở StockBalance (tuỳ nghiệp vụ có thể thêm bản ghi âm)
            var today = DateTime.Today;
            foreach (var d in r.Details)
            {
                var sb = await _db.Set<StockBalance>()
                    .FirstOrDefaultAsync(x => x.WarehouseId == r.WarehouseId
                                           && x.MaterialId == d.MaterialId
                                           && x.Date == today);
                if (sb == null)
                {
                    sb = new StockBalance
                    {
                        WarehouseId = r.WarehouseId,
                        MaterialId = d.MaterialId,
                        Date = today,
                        UpdatedAt = DateTime.Now
                    };
                    _db.Add(sb);
                }
                sb.OutQty -= (decimal)d.Quantity;
                sb.OutValue -= (decimal)d.Quantity * d.UnitPrice;
                sb.UpdatedAt = DateTime.Now;
            }
            await _db.SaveChangesAsync();
        }

        r.Status = DocumentStatus.DaHuy;
        if (!string.IsNullOrWhiteSpace(note))
            r.Note = string.IsNullOrWhiteSpace(r.Note) ? $"[HUỶ] {note}" : $"{r.Note}\n[HUỶ] {note}";

        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        return RedirectToAction(nameof(Index));
    }
    
    [HttpGet]
    public async Task<IActionResult> OnHand(int warehouseId, string ids)
    {
        if (warehouseId <= 0 || string.IsNullOrWhiteSpace(ids))
            return Json(Array.Empty<object>());

        var matIds = ids.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => int.TryParse(s, out var i) ? i : 0)
                        .Where(i => i > 0)
                        .ToList();

        var rows = await _db.Stocks.AsNoTracking()
            .Where(s => s.WarehouseId == warehouseId && matIds.Contains(s.MaterialId))
            .Select(s => new { materialId = s.MaterialId, qty = s.Quantity })
            .ToListAsync();

        // Nếu vật tư chưa có record tồn → coi như 0
        // (client sẽ tự hiểu không thấy khóa => 0)
        return Json(rows);
    }

    // API: lấy danh sách lô có thể xuất cho 1 vật tư tại 1 kho (sắp xếp FEFO)
    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetAvailableLots(int warehouseId, int materialId)
    {
        if (warehouseId <= 0 || materialId <= 0)
            return Json(new { success = false, message = "Tham số không hợp lệ" });

        var lots = await _db.StockLots.AsNoTracking()
            .Where(x => x.WarehouseId == warehouseId && x.MaterialId == materialId && x.Quantity > 0)
            .OrderBy(x => x.ExpiryDate.HasValue ? 0 : 1)  // có HSD trước
            .ThenBy(x => x.ExpiryDate)
            .ThenBy(x => x.ManufactureDate.HasValue ? 0 : 1)
            .ThenBy(x => x.ManufactureDate)
            .ThenBy(x => x.CreatedAt)
            .Select(x => new
            {
                lotId = x.Id,
                lotNumber = x.LotNumber ?? "(Không có số lô)",
                mfgDate = x.ManufactureDate,
                expDate = x.ExpiryDate,
                qty = x.Quantity
            })
            .ToListAsync();

        return Json(new { success = true, lots });
    }

    [HttpGet]
    [Authorize]
    [RequirePermission("Issues", "Read")]
    public async Task<IActionResult> ExportPdf(string? q, DocumentStatus? status, int? warehouseId)
    {
        var query = _db.StockIssues.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(r => r.IssueNumber!.Contains(q) || (r.Note ?? "").Contains(q));
        if (status.HasValue) query = query.Where(r => r.Status == status);
        if (warehouseId.HasValue) query = query.Where(r => r.WarehouseId == warehouseId);
        query = query.Include(r => r.Warehouse).Include(r => r.CreatedBy).Include(r => r.Details).ThenInclude(d => d.Material);

        var list = await query.OrderByDescending(r => r.CreatedAt).ToListAsync();

        if (list.Count == 0)
        {
            return NotFound("Không có phiếu xuất kho nào để xuất.");
        }

        // Tạo PDF - mỗi phiếu một trang
        QuestPDF.Settings.License = LicenseType.Community;
        var document = QuestPDF.Fluent.Document.Create(container =>
        {
            foreach (var issue in list)
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
                            column.Item().AlignCenter().Text("PHIẾU XUẤT KHO")
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
                                    row.RelativeItem().Text($": {issue.CreatedAt:dd} tháng {issue.CreatedAt:MM} năm {issue.CreatedAt:yyyy}").FontSize(10).FontFamily("Times New Roman");
                                    row.AutoItem().PaddingLeft(20).Text("Số").FontSize(10).FontFamily("Times New Roman");
                                    row.RelativeItem().Text($": {issue.IssueNumber ?? ""}").FontSize(10).Bold().FontFamily("Times New Roman");
                                });

                                infoCol.Item().PaddingTop(8).Row(row =>
                                {
                                    row.AutoItem().Text("- Họ tên người nhận").FontSize(10).FontFamily("Times New Roman");
                                    row.RelativeItem().Text($": {issue.ReceivedByName ?? ""}").FontSize(10).FontFamily("Times New Roman");
                                });

                                infoCol.Item().PaddingTop(4).Row(row =>
                                {
                                    row.AutoItem().Text("Xuất tại kho").FontSize(10).FontFamily("Times New Roman");
                                    row.RelativeItem().Text($": {issue.Warehouse?.Name ?? ""}").FontSize(10).Bold().FontFamily("Times New Roman");
                                });
                            });

                            column.Item().PaddingTop(15);

                            // Tính tổng tiền trước
                            decimal totalAmount = issue.Details?.Sum(d => (decimal)d.Quantity * d.UnitPrice) ?? 0m;

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
                                    columns.RelativeColumn(1.5f); // Số lượng (Thực xuất)
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
                                        col.Item().Text("xuất").FontSize(9).FontFamily("Times New Roman");
                                    });
                                    header.Cell().Element(ReceiptHeaderCellStyle).Text("Đơn giá").Bold().FontSize(10).FontFamily("Times New Roman");
                                    header.Cell().Element(ReceiptHeaderCellStyle).Text("Thành tiền").Bold().FontSize(10).FontFamily("Times New Roman");
                                });

                                // Data rows
                                int stt = 1;
                                foreach (var detail in issue.Details ?? Enumerable.Empty<StockIssueDetail>())
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
                                table.Cell().Element(ReceiptDataCellStyle).AlignRight().Text(FormatQty(issue.Details?.Sum(d => (decimal)d.Quantity) ?? 0m)).FontSize(10).Bold().FontFamily("Times New Roman");
                                table.Cell().Element(ReceiptDataCellStyle).AlignRight().Text(FormatQty(issue.Details?.Sum(d => (decimal)d.Quantity) ?? 0m)).FontSize(10).Bold().FontFamily("Times New Roman");
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
                                    col.Item().PaddingTop(5).AlignCenter().Text(issue.CreatedBy?.FullName ?? "").FontSize(10).FontFamily("Times New Roman");
                                });

                                // Người nhận
                                row.RelativeItem().Column(col =>
                                {
                                    col.Item().AlignCenter().Text("Người nhận").FontSize(10).Bold().FontFamily("Times New Roman");
                                    col.Item().PaddingTop(30).AlignCenter().Text("(Ký, họ tên)").FontSize(9).Italic().FontColor(Colors.Grey.Darken1).FontFamily("Times New Roman");
                                    col.Item().PaddingTop(5).AlignCenter().Text(issue.ReceivedByName ?? "").FontSize(10).FontFamily("Times New Roman");
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
                                    col.Item().AlignCenter().Text("có nhu cầu xuất)").FontSize(8).Italic().FontColor(Colors.Grey.Darken1).FontFamily("Times New Roman");
                                    col.Item().PaddingTop(20).AlignCenter().Text("(Ký, họ tên)").FontSize(9).Italic().FontColor(Colors.Grey.Darken1).FontFamily("Times New Roman");
                                });
                            });
                        });
                });
            }
        });

        var pdfBytes = document.GeneratePdf();
        var fileName = $"PhieuXuat_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
        return File(pdfBytes, "application/pdf", fileName);
    }

    private static IContainer HeaderCellStyle(IContainer container)
    {
        return container
            .Border(1)
            .BorderColor(Colors.Blue.Darken2)
            .PaddingVertical(10)
            .PaddingHorizontal(8)
            .Background(Colors.Blue.Darken3)
            .AlignMiddle();
    }

    private static IContainer DataCellStyle(IContainer container, bool isEvenRow)
    {
        return container
            .Border(1)
            .BorderColor(Colors.Grey.Lighten2)
            .PaddingVertical(8)
            .PaddingHorizontal(8)
            .Background(isEvenRow ? Colors.White : Colors.Grey.Lighten4)
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
            DocumentStatus.DaXuatHang => "Đã xuất hàng",
            DocumentStatus.DaHuy => "Từ chối",
            _ => status.ToString()
        };
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
}

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

public class StockIssuesController : Controller
{
    private readonly AppDbContext _db;
    private readonly IStockService _stock;

    public StockIssuesController(AppDbContext db, IStockService stock)
    {
        _db = db; _stock = stock;
    }

    [Authorize]
    public async Task<IActionResult> Index(string? q, DocumentStatus? status, int? warehouseId)
    {
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

        var list = await query.OrderByDescending(r => r.CreatedAt).ToListAsync();

        // chips
        var baseQ = _db.StockIssues.AsNoTracking();
        ViewBag.CountAll = await baseQ.CountAsync();
        ViewBag.CountMoi = await baseQ.Where(x => x.Status == DocumentStatus.Moi).CountAsync();
        ViewBag.CountXacNhan = await baseQ.Where(x => x.Status == DocumentStatus.DaXacNhan).CountAsync();
        ViewBag.CountXuat = await baseQ.Where(x => x.Status == DocumentStatus.DaXuatHang).CountAsync();
        ViewBag.CountHuy = await baseQ.Where(x => x.Status == DocumentStatus.DaHuy).CountAsync();

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
    public async Task<IActionResult> Create()
    {
        ViewBag.WarehouseId = new SelectList(
            await _db.Warehouses.AsNoTracking().OrderBy(w => w.Name).ToListAsync(),
            "Id", "Name"
        );
        ViewBag.Materials = await _db.Materials.AsNoTracking().OrderBy(m => m.Name).ToListAsync();

        // Gợi ý set sẵn người lập
        var uidStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        int.TryParse(uidStr, out var uid);
        var model = new StockIssue { CreatedById = uid, CreatedAt = DateTime.Now };
        return View(model); // View: Views/StockIssues/Create.cshtml
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateIssue(StockIssue issue)
    {
        // == code giống CreateIssue hiện tại ==
        issue.CreatedAt = DateTime.Now;
        issue.Status = DocumentStatus.Moi;
        issue.IssueNumber = $"PX{DateTime.Now:yyMMdd}-{Guid.NewGuid().ToString("N")[..4].ToUpper()}";
        issue.AttachedDocuments ??= string.Empty;
        issue.Note ??= string.Empty;                // tránh NULL (nếu DB chưa migrate)

        var uidStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (int.TryParse(uidStr, out var uid)) issue.CreatedById = uid;
        else ModelState.AddModelError("", "Không xác định được người lập.");

        ModelState.Remove(nameof(StockIssue.IssueNumber));
        ModelState.Remove(nameof(StockIssue.Status));
        ModelState.Remove(nameof(StockIssue.CreatedAt));
        ModelState.Remove(nameof(StockIssue.ApprovedById));
        ModelState.Remove(nameof(StockIssue.ApprovedAt));
        ModelState.Remove(nameof(StockIssue.Note));
        ModelState.Remove(nameof(StockIssue.Warehouse));
        ModelState.Remove(nameof(StockIssue.CreatedBy));
        ModelState.Remove(nameof(StockIssue.AttachedDocuments));

        issue.Details = (issue.Details ?? new List<StockIssueDetail>())
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

        for (int i = 0; i < issue.Details.Count; i++)
        {
            ModelState.Remove($"Details[{i}].Material");
            ModelState.Remove($"Details[{i}].StockIssue");
        }

        if (!issue.Details.Any())
            ModelState.AddModelError("", "Vui lòng nhập ít nhất 1 dòng chi tiết.");
            
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
        return View(issue);
    }



    [HttpPost]
    [Authorize(Roles = "admin")]
    [ValidateAntiForgeryToken]
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
        return RedirectToAction(nameof(Index)); // hoặc Details
    }

    // Xuất kho: trừ tồn + ghi sổ StockBalance
    [HttpPost]
    [Authorize(Roles = "admin")]
    [ValidateAntiForgeryToken]
    [ActionName("IssuePost")]  // tránh trùng tên
    public async Task<IActionResult> Issue(int id)
    {
        using var tx = await _db.Database.BeginTransactionAsync();
        var r = await _db.StockIssues.Include(x => x.Details).FirstOrDefaultAsync(x => x.Id == id);
        if (r == null) return NotFound();
        if (r.Status != DocumentStatus.DaXacNhan)
            return BadRequest("Chỉ xuất kho phiếu ở trạng thái Đã xác nhận.");

        // Kiểm tra tồn từng dòng ở kho đó
        foreach (var d in r.Details)
        {
            // tồn hiện tại theo bảng Stocks (warehouse, material)
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
            await _stock.DecreaseAsync(r.WarehouseId, d.MaterialId, (decimal)d.Quantity);

            // StockBalance: OutQty/OutValue
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
            sb.OutQty += (decimal)d.Quantity;
            sb.OutValue += (decimal)d.Quantity * d.UnitPrice;
            sb.UpdatedAt = DateTime.Now;
        }
        await _stock.SaveAsync();

        // Cập nhật trạng thái
        r.Status = DocumentStatus.DaXuatHang; // Nếu enum bạn CHƯA có, tạm dùng DaNhapHang và sửa label “Đã xuất hàng”
        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        TempData["OpenDetailId"] = id;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Roles = "admin")]
    [ValidateAntiForgeryToken]
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

}

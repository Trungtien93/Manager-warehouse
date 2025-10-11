using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using MNBEMART.Data;
using MNBEMART.Models;
using MNBEMART.Services;
using System.Security.Claims;

[Authorize]
public class StockAdjustmentsController : Controller
{
    private readonly AppDbContext _db;
    private readonly IDocumentNumberingService _num;
    private readonly IStockService _stock;

    public StockAdjustmentsController(AppDbContext db, IDocumentNumberingService num, IStockService stock)
    {
        _db = db; _num = num; _stock = stock;
    }

    public async Task<IActionResult> Index()
    {
        var data = await _db.StockAdjustments
            .Include(x => x.Warehouse)
            .Include(x => x.CreatedBy)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync();
        return View(data);
    }

    [HttpGet]
    public IActionResult Create()
    {
        ViewBag.WarehouseId = new SelectList(_db.Warehouses, "Id", "Name");
        ViewBag.Materials   = _db.Materials.OrderBy(m => m.Name).ToList();
        return View(new StockAdjustment { Details = new List<StockAdjustmentDetail>(), CreatedAt = DateTime.Now });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(StockAdjustment m)
    {
        // số phiếu + người lập
        m.AdjustNumber = await _num.NextAsync("StockAdjustment", m.WarehouseId);
        var uidStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (int.TryParse(uidStr, out var uid)) m.CreatedById = uid;

        // clean details
        // m.Details = (m.Details ?? new List<StockAdjustmentDetail>())
        //     .Where(d => d.MaterialId > 0 && d.QuantityDiff != 0)
        //     .ToList();

        // if (!m.Details.Any())
        //     ModelState.AddModelError("", "Vui lòng nhập ít nhất 1 dòng chênh lệch (+/-).");

        // if (!ModelState.IsValid)
        // {
        //     ViewBag.WarehouseId = new SelectList(_db.Warehouses, "Id", "Name", m.WarehouseId);
        //     ViewBag.Materials   = _db.Materials.OrderBy(a => a.Name).ToList();
        //     return View(m);
        // }

        // Gom dòng trùng vật tư (cộng chênh lệch)
        m.Details = (m.Details ?? new List<StockAdjustmentDetail>())
            .Where(d => d.MaterialId > 0 && d.QuantityDiff != 0)
            .GroupBy(d => new { d.MaterialId, d.Note }) // gộp theo vật tư + ghi chú (tuỳ bạn, có thể chỉ theo MaterialId)
            .Select(g => new StockAdjustmentDetail {
                MaterialId   = g.Key.MaterialId,
                QuantityDiff = g.Sum(x => x.QuantityDiff),
                Note         = g.Key.Note
            })
            .ToList();

        if (!m.Details.Any())
            ModelState.AddModelError("", "Vui lòng nhập ít nhất 1 dòng chênh lệch (+/-).");

        // Server-side: chặn tồn âm sau điều chỉnh
        var matIds = m.Details.Select(d => d.MaterialId).Distinct().ToList();
        var onhands = await _db.Stocks.AsNoTracking()
            .Where(s => s.WarehouseId == m.WarehouseId && matIds.Contains(s.MaterialId))
            .GroupBy(s => s.MaterialId)
            .Select(g => new { MaterialId = g.Key, Qty = g.Sum(x => x.Quantity) })
            .ToDictionaryAsync(x => x.MaterialId, x => x.Qty);

        var matNames = await _db.Materials.AsNoTracking()
            .Where(mm => matIds.Contains(mm.Id))
            .ToDictionaryAsync(mm => mm.Id, mm => $"{mm.Code} - {mm.Name}");

        foreach (var d in m.Details)
        {
            var onhand = onhands.TryGetValue(d.MaterialId, out var q) ? q : 0m;
            var final  = onhand + d.QuantityDiff;
            if (final < 0)
            {
                var name = matNames.TryGetValue(d.MaterialId, out var n) ? n : $"#{d.MaterialId}";
                ModelState.AddModelError("", $"Vật tư '{name}': tồn {onhand}, chênh {d.QuantityDiff} → sẽ âm ({final}).");
            }
        }

        if (!ModelState.IsValid)
        {
            ViewBag.WarehouseId = new SelectList(_db.Warehouses, "Id", "Name", m.WarehouseId);
            ViewBag.Materials   = _db.Materials.OrderBy(a => a.Name).ToList();
            return View(m);
        }


        _db.StockAdjustments.Add(m);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Roles = "admin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int id)
    {
        using var tx = await _db.Database.BeginTransactionAsync();
        var a = await _db.StockAdjustments.Include(x => x.Details).FirstOrDefaultAsync(x => x.Id == id);
        if (a == null) return NotFound();
        if (a.Status != DocumentStatus.Moi) return BadRequest("Phiếu không ở trạng thái chờ duyệt.");

        await _stock.ApplyAdjustmentAsync(a);
        await _stock.SaveAsync();

        a.Status = DocumentStatus.DaXacNhan;
        var uidStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (int.TryParse(uidStr, out var uid)) a.ApprovedById = uid;
        a.ApprovedAt = DateTime.Now;

        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        return RedirectToAction(nameof(Index));
    }

    // Controllers/StockAdjustmentsController.cs (bổ sung cuối file controller)

    [HttpGet]
    public async Task<IActionResult> OnHand(int warehouseId, string ids)
    {
        var idList = (ids ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => int.TryParse(s.Trim(), out var x) ? x : 0)
            .Where(x => x > 0)
            .ToList();
        if (!idList.Any()) return Json(new { });

        var dict = await _db.Stocks.AsNoTracking()
            .Where(s => s.WarehouseId == warehouseId && idList.Contains(s.MaterialId))
            .GroupBy(s => s.MaterialId)
            .Select(g => new { g.Key, Qty = g.Sum(x => x.Quantity) })
            .ToDictionaryAsync(x => x.Key, x => x.Qty);

        return Json(dict);
    }

    [HttpGet]
    public async Task<IActionResult> MaterialUnits(string ids)
    {
        var idList = (ids ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => int.TryParse(s.Trim(), out var x) ? x : 0)
            .Where(x => x > 0)
            .ToList();
        if (!idList.Any()) return Json(new { });

        var dict = await _db.Materials.AsNoTracking()
            .Where(m => idList.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, m => m.Unit);

        return Json(dict);
    }

}

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Rendering;
using MNBEMART.Data;
using MNBEMART.Models;
using MNBEMART.Services;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;


public class StockReceiptsController : Controller
{
    private readonly AppDbContext _db;
    private readonly IStockService _stock;

    public StockReceiptsController(AppDbContext db, IStockService stock)
    {
        _db = db;
        _stock = stock;
    }

    [Authorize]
public async Task<IActionResult> Index(string? q, DocumentStatus? status, int? warehouseId)
{
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

    var list = await query
        .OrderByDescending(r => r.CreatedAt)
        .ToListAsync();

    // var list = await query.OrderByDescending(r => r.CreatedAt).ToListAsync();

    // chips đếm theo trạng thái (toàn hệ thống, không phụ thuộc filter — muốn phụ thuộc thì dùng 'query')
    var baseQ = _db.StockReceipts.AsNoTracking();
    ViewBag.CountAll      = await baseQ.CountAsync();
    ViewBag.CountMoi      = await baseQ.Where(x => x.Status == DocumentStatus.Moi).CountAsync();
    ViewBag.CountXacNhan  = await baseQ.Where(x => x.Status == DocumentStatus.DaXacNhan).CountAsync();
    ViewBag.CountNhap     = await baseQ.Where(x => x.Status == DocumentStatus.DaNhapHang).CountAsync();
    ViewBag.CountHuy      = await baseQ.Where(x => x.Status == DocumentStatus.DaHuy).CountAsync();

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


    

    [HttpGet]
    [Authorize]
    public IActionResult Create()
    {
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
    public async Task<IActionResult> CreateReceipt(StockReceipt receipt)
    {
        // server-set
        receipt.CreatedAt = DateTime.Now;
        receipt.Status = DocumentStatus.Moi;
        receipt.ReceiptNumber = $"PN{DateTime.Now:yyMMdd}-{Guid.NewGuid().ToString("N")[..4].ToUpper()}";
        receipt.AttachedDocuments ??= string.Empty;

        // Override người lập từ claim (không tin form)
        var uidStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (int.TryParse(uidStr, out var uid)) receipt.CreatedById = uid;
        else ModelState.AddModelError("", "Không xác định được người lập. Vui lòng đăng nhập lại.");

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

        // chuẩn hóa chi tiết
        receipt.Details = (receipt.Details ?? new List<StockReceiptDetail>())
            .Where(d => d.MaterialId > 0 && d.Quantity > 0)
            .Select(d => { d.UnitPrice = Math.Round(d.UnitPrice, 2); return d; })
            .ToList();

        // gỡ lỗi nav trong details (nếu có)
        for (int i = 0; i < receipt.Details.Count; i++)
        {
            ModelState.Remove($"Details[{i}].Material");
            ModelState.Remove($"Details[{i}].StockReceipt");
        }

        if (!receipt.Details.Any())
            ModelState.AddModelError("", "Vui lòng nhập ít nhất 1 dòng chi tiết hợp lệ.");

        if (!ModelState.IsValid)
        {
            ViewBag.WarehouseId = new SelectList(_db.Warehouses, "Id", "Name", receipt.WarehouseId);
            ViewBag.Materials = _db.Materials.OrderBy(m => m.Name).ToList();
            ViewBag.CurrentUserName = User.Identity?.Name;
            return View("Create", receipt);
        }

        _db.StockReceipts.Add(receipt);
        await _db.SaveChangesAsync();
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
    [Authorize(Roles = "admin")]
    [ValidateAntiForgeryToken]
    [ActionName("ApprovePost")]   // để tránh trùng tên với GET Details
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
        // return RedirectToAction(nameof(Details), new { id });
        TempData["OpenDetailId"] = id;                 // <— gửi id để mở modal
        return RedirectToAction(nameof(Index));        // <— quay về Index
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
[Authorize(Roles = "admin")]
[ValidateAntiForgeryToken]
// [ActionName("Receive")] // View dùng asp-action="Receive"
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
    }

    // Cập nhật trạng thái phiếu
    r.Status = DocumentStatus.DaNhapHang;

    await _db.SaveChangesAsync();
    await tx.CommitAsync();

    TempData["OpenDetailId"] = id;
    return RedirectToAction(nameof(Index));
}





    [HttpPost]
    [Authorize(Roles = "admin")]
    [ValidateAntiForgeryToken]
    [ActionName("RejectPost")]    // để tránh trùng tên với GET Details
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
                await _stock.DecreaseAsync(r.WarehouseId, d.MaterialId, (decimal)d.Quantity);
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


}

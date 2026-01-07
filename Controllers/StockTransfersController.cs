using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using MNBEMART.Data;
using MNBEMART.Models;
using MNBEMART.Services;
using MNBEMART.ViewModels; 
using MNBEMART.Filters;
using MNBEMART.Extensions;

namespace MNBEMART.Controllers
{
    [Authorize]
    public class StockTransfersController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IDocumentNumberingService _num;
        private readonly IStockService _stock;
        private readonly IAuditService _audit;
        private readonly ITransferOptimizationService _transferService;
        private readonly INotificationService _notificationService;

        public StockTransfersController(AppDbContext db, IDocumentNumberingService num, IStockService stock, IAuditService audit, ITransferOptimizationService transferService, INotificationService notificationService)
        {
            _db = db; _num = num; _stock = stock; _audit = audit; _transferService = transferService; _notificationService = notificationService;
        }

[RequirePermission("Transfers","Read")]
        public async Task<IActionResult> Index(int page = 1, int pageSize = 30)
        {
            var query = _db.StockTransfers
                .AsNoTracking()
                .Include(x => x.FromWarehouse)
                .Include(x => x.ToWarehouse)
                .Include(x => x.CreatedBy)
                .Include(x => x.ApprovedBy)
                .Include(x => x.Details).ThenInclude(d => d.Material);

            var pagedResult = await query
                .OrderByDescending(x => x.CreatedAt)
                .ToPagedResultAsync(page, pageSize);

            ViewBag.Page = pagedResult.Page;
            ViewBag.PageSize = pagedResult.PageSize;
            ViewBag.TotalItems = pagedResult.TotalItems;
            ViewBag.TotalPages = pagedResult.TotalPages;
            ViewBag.PagedResult = pagedResult;

            return View(pagedResult.Items.ToList());
        }

        [HttpGet]
        [RequirePermission("Transfers","Create")]
        public async Task<IActionResult> Create()
        {
            // Cả Admin và User đều được tạo phiếu

            var model = new StockTransfer { CreatedAt = DateTime.UtcNow };
            var uidStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (int.TryParse(uidStr, out var uid)) model.CreatedById = uid;

            ViewBag.WarehouseOptions = new SelectList(
                await _db.Warehouses.AsNoTracking().OrderBy(w => w.Name).ToListAsync(),
                "Id", "Name"
            );
            ViewBag.Materials = await _db.Materials.AsNoTracking().OrderBy(m => m.Name).ToListAsync();

            // Nếu là AJAX request, trả về partial view modal
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return PartialView("_CreateModal", model);
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
[RequirePermission("Transfers","Create")]
        public async Task<IActionResult> Create(StockTransfer transfer)
        {
            // Cả Admin và User đều được tạo phiếu
            bool isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest";
            
            // server-set
            transfer.CreatedAt = DateTime.Now;
            transfer.Status = DocumentStatus.Moi;
            transfer.TransferNumber = await _num.NextAsync("StockTransfer", null);

            // Override người lập từ claim (không tin form)
            var uidStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (int.TryParse(uidStr, out var uid)) transfer.CreatedById = uid;
            else ModelState.AddModelError("", "Không xác định được người lập. Vui lòng đăng nhập lại.");

            // Validation các trường bắt buộc
            if (transfer.FromWarehouseId <= 0)
                ModelState.AddModelError(nameof(StockTransfer.FromWarehouseId), "Vui lòng chọn kho đi.");

            if (transfer.ToWarehouseId <= 0)
                ModelState.AddModelError(nameof(StockTransfer.ToWarehouseId), "Vui lòng chọn kho đến.");

            if (transfer.FromWarehouseId == transfer.ToWarehouseId)
                ModelState.AddModelError(nameof(StockTransfer.ToWarehouseId), "Kho đi và kho đến không được trùng.");

            // làm sạch ModelState cho các field không post
            ModelState.Remove(nameof(StockTransfer.TransferNumber));
            ModelState.Remove(nameof(StockTransfer.Status));
            ModelState.Remove(nameof(StockTransfer.CreatedAt));
            ModelState.Remove(nameof(StockTransfer.ApprovedById));
            ModelState.Remove(nameof(StockTransfer.ApprovedAt));
            ModelState.Remove(nameof(StockTransfer.Note));
            ModelState.Remove(nameof(StockTransfer.FromWarehouse));
            ModelState.Remove(nameof(StockTransfer.ToWarehouse));
            ModelState.Remove(nameof(StockTransfer.CreatedBy));
            ModelState.Remove(nameof(StockTransfer.ApprovedBy));

            // chuẩn hóa chi tiết và validation
            var detailsList = (transfer.Details ?? new List<StockTransferDetail>())
                .Where(d => d.MaterialId > 0 && d.Quantity > 0)
                .GroupBy(d => new { d.MaterialId, d.Unit, d.Note, d.LotId, d.ManufactureDate, d.ExpiryDate })
                .Select(g => new StockTransferDetail
                {
                    MaterialId = g.Key.MaterialId,
                    Unit = g.Key.Unit,
                    Note = g.Key.Note,
                    Quantity = g.Sum(x => x.Quantity),
                    LotId = g.Key.LotId,
                    ManufactureDate = g.Key.ManufactureDate,
                    ExpiryDate = g.Key.ExpiryDate
                })
                .ToList();

            transfer.Details = detailsList;

            // gỡ lỗi nav trong details (nếu có)
            for (int i = 0; i < detailsList.Count; i++)
            {
                ModelState.Remove($"Details[{i}].Material");
                ModelState.Remove($"Details[{i}].StockTransfer");
                ModelState.Remove($"Details[{i}].Lot");
                
                // Validation các trường bắt buộc trong detail
                var detail = detailsList[i];
                if (detail.MaterialId <= 0)
                    ModelState.AddModelError($"Details[{i}].MaterialId", $"Dòng {i + 1}: Vui lòng chọn vật tư.");
                if (detail.Quantity <= 0)
                    ModelState.AddModelError($"Details[{i}].Quantity", $"Dòng {i + 1}: Số lượng phải lớn hơn 0.");
                if (string.IsNullOrWhiteSpace(detail.Unit))
                    ModelState.AddModelError($"Details[{i}].Unit", $"Dòng {i + 1}: Vui lòng nhập đơn vị tính.");
                if (!detail.LotId.HasValue || detail.LotId <= 0)
                    ModelState.AddModelError($"Details[{i}].LotId", $"Dòng {i + 1}: Vui lòng chọn lô.");
                if (!detail.ManufactureDate.HasValue)
                    ModelState.AddModelError($"Details[{i}].ManufactureDate", $"Dòng {i + 1}: Vui lòng chọn ngày sản xuất.");
                if (!detail.ExpiryDate.HasValue)
                    ModelState.AddModelError($"Details[{i}].ExpiryDate", $"Dòng {i + 1}: Vui lòng chọn hạn sử dụng.");
            }

            if (!detailsList.Any())
                ModelState.AddModelError("", "Vui lòng nhập ít nhất 1 dòng chi tiết hợp lệ với đầy đủ thông tin (Vật tư, SL, ĐVT, Lô, Ngày sản xuất, Hạn sử dụng).");

            // ==== CHẶN VƯỢT TỒN (server-side) ====
            var fromWid = transfer.FromWarehouseId;
            foreach (var d in detailsList)
            {
                var stock = await _db.Stocks.AsNoTracking()
                    .FirstOrDefaultAsync(s => s.WarehouseId == fromWid && s.MaterialId == d.MaterialId);

                var onHand = stock?.Quantity ?? 0m;
                if (onHand < d.Quantity)
                {
                    var mat = await _db.Materials.AsNoTracking().FirstOrDefaultAsync(x => x.Id == d.MaterialId);
                    ModelState.AddModelError("",
                        $"Vật tư {(mat?.Code + " " + mat?.Name) ?? ("ID=" + d.MaterialId)} ở kho đi hiện còn {onHand}, không đủ để chuyển {d.Quantity}.");
                }
            }

            if (!ModelState.IsValid)
            {
                ViewBag.WarehouseOptions = new SelectList(_db.Warehouses, "Id", "Name", transfer.FromWarehouseId);
                ViewBag.Materials = _db.Materials.OrderBy(m => m.Name).ToList();
                
                if (isAjax)
                {
                    Response.ContentType = "application/json";
                    Response.StatusCode = 400;
                    var errors = ModelState
                        .Where(x => x.Value?.Errors != null && x.Value.Errors.Count > 0)
                        .SelectMany(x => x.Value!.Errors.Select(e => new { Field = x.Key, Message = e.ErrorMessage }))
                        .ToList();
                    if (!errors.Any())
                        errors.Add(new { Field = "", Message = "Có lỗi validation xảy ra." });
                    return Json(new { success = false, errors });
                }
                
                return View(transfer);
            }

            _db.StockTransfers.Add(transfer);
            await _db.SaveChangesAsync();
            
            // Tạo thông báo cho Admin khi user tạo phiếu
            try
            {
                var adminUserIds = await GetAdminUserIdsAsync();
                var fromWarehouse = await _db.Warehouses.FindAsync(transfer.FromWarehouseId);
                var toWarehouse = await _db.Warehouses.FindAsync(transfer.ToWarehouseId);
                await _db.Entry(transfer).Reference(e => e.CreatedBy).LoadAsync();
                var createdByName = transfer.CreatedBy?.FullName ?? User.Identity?.Name ?? "System";
                
                if (adminUserIds != null && adminUserIds.Any())
                {
                    await _notificationService.CreateNotificationForUsersAsync(
                        NotificationType.Transfer,
                        transfer.Id,
                        $"Phiếu chuyển kho mới: {transfer.TransferNumber}",
                        $"Người tạo: {createdByName} | Từ {fromWarehouse?.Name ?? "N/A"} → {toWarehouse?.Name ?? "N/A"}",
                        adminUserIds,
                        NotificationPriority.Normal
                    );
                }
            }
            catch (Exception notifEx)
            {
                System.Diagnostics.Debug.WriteLine($"[StockTransfer] Error creating notification: {notifEx.Message}");
            }
            
            await _audit.LogAsync(GetUid(), "Create", "StockTransfer", transfer.Id.ToString(), module: "Chuyển kho", warehouseId: transfer.FromWarehouseId, content: $"Chuyển từ kho #{transfer.FromWarehouseId} sang #{transfer.ToWarehouseId}, {transfer.Details.Count} dòng");
            
            TempData["Msg"] = $"Tạo phiếu chuyển kho {transfer.TransferNumber} thành công!";
            
            if (isAjax)
            {
                Response.ContentType = "application/json";
                return Json(new { success = true, message = $"Tạo phiếu chuyển kho {transfer.TransferNumber} thành công!", transferId = transfer.Id });
            }
            
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
[RequirePermission("Transfers","Read")]
        public async Task<IActionResult> Details(int id)
        {
            var m = await _db.StockTransfers
                .AsNoTracking()
                .Include(x => x.FromWarehouse)
                .Include(x => x.ToWarehouse)
                .Include(x => x.CreatedBy)
                .Include(x => x.ApprovedBy)
                .Include(x => x.Details).ThenInclude(d => d.Material)
                .FirstOrDefaultAsync(x => x.Id == id);
            if (m == null) return NotFound();
            return View(m);
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
[RequirePermission("Transfers","Update")]
        public async Task<IActionResult> Approve(int id)
        {
            using var tx = await _db.Database.BeginTransactionAsync();

            var t = await _db.StockTransfers
                .Include(x => x.Details)
                .FirstOrDefaultAsync(x => x.Id == id);
            if (t == null) return NotFound();
            if (t.Status != DocumentStatus.Moi) return BadRequest("Phiếu không ở trạng thái chờ duyệt.");

            // Không chuyển tồn ở bước duyệt – chỉ đánh dấu đã xác nhận
            t.Status = DocumentStatus.DaXacNhan;
            t.ApprovedAt = DateTime.UtcNow;
            var uidStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (int.TryParse(uidStr, out var uid)) t.ApprovedById = uid;

            await _db.SaveChangesAsync();
            
            // Thông báo cho user tạo phiếu
            if (t.CreatedById > 0)
            {
                await _notificationService.CreateNotificationAsync(
                    NotificationType.Transfer,
                    t.Id,
                    $"Phiếu chuyển kho đã được xác nhận: {t.TransferNumber}",
                    $"Phiếu chuyển kho của bạn đã được xác nhận bởi admin",
                    t.CreatedById,
                    NotificationPriority.Normal
                );
            }
            
            await _audit.LogAsync(GetUid(), "Approve", "StockTransfer", id.ToString(), module: "Chuyển kho", warehouseId: t.FromWarehouseId, content: "Duyệt phiếu chuyển kho");
            await tx.CommitAsync();
            TempData["Msg"] = $"Duyệt phiếu {t.TransferNumber} thành công!";
            return RedirectToAction(nameof(Index));
        }

        private int GetUid() => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

        // Helper method để lấy danh sách Admin user IDs
        private async Task<List<int>> GetAdminUserIdsAsync()
        {
            var adminUserIds = new List<int>();
            
            try
            {
                // Get Admin users (role string = "Admin")
                adminUserIds = await _db.Users
                    .Where(u => u.IsActive && u.Role != null && u.Role.ToLower() == "admin")
                    .Select(u => u.Id)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[StockTransfer] Error getting admin users from Users table: {ex.Message}");
                // Continue with empty list
            }

            // Get users with Admin role from UserRoles table
            try
            {
                var adminRoleIds = await (from r in _db.Roles
                                        where r.Code.ToLower() == "admin" || r.Name.ToLower() == "admin"
                                        select r.Id)
                                        .ToListAsync();

                if (adminRoleIds != null && adminRoleIds.Any())
                {
                    var adminUserIdsFromRoles = await (from ur in _db.UserRoles
                                                      where adminRoleIds.Contains(ur.RoleId)
                                                      select ur.UserId)
                                                      .Distinct()
                                                      .ToListAsync();
                    if (adminUserIdsFromRoles != null && adminUserIdsFromRoles.Any())
                    {
                        adminUserIds = adminUserIds.Union(adminUserIdsFromRoles).Distinct().ToList();
                    }
                }
            }
            catch (Exception ex)
            {
                // UserRoles table may not exist or query may fail, ignore
                System.Diagnostics.Debug.WriteLine($"[StockTransfer] Error getting admin users from UserRoles table: {ex.Message}");
            }

            return adminUserIds ?? new List<int>();
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        [RequirePermission("Transfers","Update")]
        public async Task<IActionResult> Complete(int id)
        {
            using var tx = await _db.Database.BeginTransactionAsync();

            var t = await _db.StockTransfers
                .Include(x => x.Details)
                .Include(x => x.CreatedBy)
                .FirstOrDefaultAsync(x => x.Id == id);
            if (t == null) return NotFound();
            if (t.Status != DocumentStatus.DaXacNhan) return BadRequest("Chỉ hoàn thành phiếu đã xác nhận.");

            // Chuyển tồn kho thực tế
            await _stock.ApplyTransferAsync(t);
            await _stock.SaveAsync();

            var today = DateTime.Today;
            foreach (var d in t.Details)
            {
                // Out ở kho đi
                var sbOut = await _db.Set<StockBalance>()
                    .FirstOrDefaultAsync(x => x.WarehouseId == t.FromWarehouseId && x.MaterialId == d.MaterialId && x.Date == today);
                if (sbOut == null)
                {
                    sbOut = new StockBalance { WarehouseId = t.FromWarehouseId, MaterialId = d.MaterialId, Date = today, UpdatedAt = DateTime.Now };
                    _db.Add(sbOut);
                }
                sbOut.OutQty += d.Quantity;
                sbOut.OutValue += d.Quantity * (d.UnitPrice ?? 0m);
                sbOut.UpdatedAt = DateTime.Now;

                // In ở kho đến
                var sbIn = await _db.Set<StockBalance>()
                    .FirstOrDefaultAsync(x => x.WarehouseId == t.ToWarehouseId && x.MaterialId == d.MaterialId && x.Date == today);
                if (sbIn == null)
                {
                    sbIn = new StockBalance { WarehouseId = t.ToWarehouseId, MaterialId = d.MaterialId, Date = today, UpdatedAt = DateTime.Now };
                    _db.Add(sbIn);
                }
                sbIn.InQty += d.Quantity;
                sbIn.InValue += d.Quantity * (d.UnitPrice ?? 0m);
                sbIn.UpdatedAt = DateTime.Now;
            }

            t.Status = DocumentStatus.HoanThanh;
            await _db.SaveChangesAsync();
            
            // Thông báo cho user tạo phiếu
            if (t.CreatedById > 0)
            {
                await _notificationService.CreateNotificationAsync(
                    NotificationType.Transfer,
                    t.Id,
                    $"Phiếu chuyển kho đã hoàn thành: {t.TransferNumber}",
                    $"Phiếu chuyển kho của bạn đã được hoàn thành",
                    t.CreatedById,
                    NotificationPriority.Normal
                );
            }
            
            await _audit.LogAsync(GetUid(), "Complete", "StockTransfer", id.ToString(), module: "Chuyển kho", warehouseId: t.FromWarehouseId, content: "Hoàn thành chuyển kho");
            await tx.CommitAsync();
            TempData["Msg"] = $"Hoàn thành phiếu {t.TransferNumber} thành công!";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        [RequirePermission("Transfers","Update")]
        public async Task<IActionResult> Cancel(int id)
        {
            var t = await _db.StockTransfers
                .Include(x => x.CreatedBy)
                .FirstOrDefaultAsync(x => x.Id == id);
            if (t == null) return NotFound();
            if (t.Status == DocumentStatus.HoanThanh) 
            {
                TempData["Error"] = "Không thể huỷ phiếu đã hoàn thành.";
                return RedirectToAction(nameof(Index));
            }
            t.Status = DocumentStatus.DaHuy;
            await _db.SaveChangesAsync();
            
            // Thông báo cho user tạo phiếu
            if (t.CreatedById > 0)
            {
                await _notificationService.CreateNotificationAsync(
                    NotificationType.Transfer,
                    t.Id,
                    $"Phiếu chuyển kho đã bị hủy: {t.TransferNumber}",
                    $"Phiếu chuyển kho của bạn đã bị hủy bởi admin",
                    t.CreatedById,
                    NotificationPriority.High
                );
            }
            
            await _audit.LogAsync(GetUid(), "Cancel", "StockTransfer", id.ToString(), module: "Chuyển kho", warehouseId: t.FromWarehouseId, content: "Huỷ phiếu chuyển kho");
            TempData["Msg"] = $"Huỷ phiếu {t.TransferNumber} thành công!";
            return RedirectToAction(nameof(Index));
        }

        // API: tồn kho theo kho đi cho list vật tư
        // GET /StockTransfers/OnHand?fromWarehouseId=1&ids=10,11,12
        [HttpGet]
[RequirePermission("Transfers","Read")]
public async Task<IActionResult> OnHand(int fromWarehouseId, string ids)
        {
            var idList = (ids ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.TryParse(s.Trim(), out var x) ? x : 0)
                .Where(x => x > 0)
                .ToList();
            if (!idList.Any()) return Json(new { });

            var dict = await _db.Stocks.AsNoTracking()
                .Where(s => s.WarehouseId == fromWarehouseId && idList.Contains(s.MaterialId))
                .GroupBy(s => s.MaterialId)
                .Select(g => new { MaterialId = g.Key, Qty = g.Sum(x => x.Quantity) })
                .ToDictionaryAsync(x => x.MaterialId, x => x.Qty);

            return Json(dict);
        }

        [HttpGet]
        [RequirePermission("Transfers","Read")]
        public async Task<IActionResult> PrintOne(int id)
        {
            var list = await _db.StockTransfers.AsNoTracking()
                .Include(r => r.FromWarehouse)
                .Include(r => r.ToWarehouse)
                .Include(r => r.CreatedBy)
                .Include(r => r.ApprovedBy)
                .Include(r => r.Details).ThenInclude(d => d.Material)
                .Where(r => r.Id == id)
                .ToListAsync();
            return View("Print", list);
        }

        // GET /StockTransfers/MaterialUnits?ids=10,11,12
        [HttpGet]
[RequirePermission("Transfers","Read")]
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

        // GET /StockTransfers/MaterialDates?ids=10,11,12
        [HttpGet]
        [RequirePermission("Transfers","Read")]
        public async Task<IActionResult> MaterialDates(string ids)
        {
            var idList = (ids ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.TryParse(s.Trim(), out var x) ? x : 0)
                .Where(x => x > 0)
                .ToList();
            if (!idList.Any()) return Json(new { });

            var dict = await _db.Materials.AsNoTracking()
                .Where(m => idList.Contains(m.Id))
                .Select(m => new {
                    m.Id,
                    m.ManufactureDate,
                    m.ExpiryDate
                })
                .ToDictionaryAsync(x => x.Id, x => new {
                    nsx = x.ManufactureDate?.ToString("yyyy-MM-dd"),
                    hsd = x.ExpiryDate?.ToString("yyyy-MM-dd")
                });

            return Json(dict);
        }

        // API: lấy danh sách lô có thể chuyển cho 1 vật tư tại 1 kho (sắp xếp FEFO)
        [HttpGet]
        [RequirePermission("Transfers", "Read")]
        public async Task<IActionResult> GetAvailableLots(int warehouseId, int materialId)
        {
            if (warehouseId <= 0 || materialId <= 0)
                return Json(new { success = false, message = "Tham số không hợp lệ" });

            try
            {
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
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

    }
}

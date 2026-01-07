using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using MNBEMART.Data;
using MNBEMART.Models;
using MNBEMART.Services;
using MNBEMART.Filters;
using System.Security.Claims;
using MNBEMART.Extensions;

namespace MNBEMART.Controllers
{
    [Authorize]
    public class LotsController : Controller
    {
        private readonly AppDbContext _context;
        private readonly ILotManagementService _lotService;

        public LotsController(AppDbContext context, ILotManagementService lotService)
        {
            _context = context;
            _lotService = lotService;
        }

        private int GetCurrentUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(userIdClaim, out int userId) ? userId : 1;
        }

        // GET: Lots/Index
        [RequirePermission("Lots", "Read")]
        public async Task<IActionResult> Index(int? materialId, int? warehouseId, string status = "all", int page = 1, int pageSize = 10)
        {
            var query = _context.StockLots
                .Include(l => l.Material)
                .Include(l => l.Warehouse)
                .Where(l => l.Quantity > 0)  // Only show active lots
                .AsQueryable();

            if (materialId.HasValue && materialId.Value > 0)
                query = query.Where(l => l.MaterialId == materialId.Value);

            if (warehouseId.HasValue && warehouseId.Value > 0)
                query = query.Where(l => l.WarehouseId == warehouseId.Value);

            if (status == "reserved")
                query = query.Where(l => l.IsReserved);
            else if (status == "available")
                query = query.Where(l => !l.IsReserved);
            else if (status == "expiring")
            {
                var soon = DateTime.Today.AddDays(30);
                query = query.Where(l => l.ExpiryDate != null && l.ExpiryDate <= soon);
            }

            // Apply pagination
            var pagedResult = await query
                .OrderBy(l => l.ExpiryDate)
                .ThenBy(l => l.Material!.Code)
                .ToPagedResultAsync(page, pageSize);

            ViewBag.Materials = new SelectList(
                await _context.Materials.OrderBy(m => m.Code).ToListAsync(),
                "Id", "Name", materialId);

            ViewBag.Warehouses = new SelectList(
                await _context.Warehouses.OrderBy(w => w.Name).ToListAsync(),
                "Id", "Name", warehouseId);

            ViewBag.StatusFilter = status;
            ViewBag.MaterialId = materialId;
            ViewBag.WarehouseId = warehouseId;
            ViewBag.Page = pagedResult.Page;
            ViewBag.PageSize = pagedResult.PageSize;
            ViewBag.TotalItems = pagedResult.TotalItems;
            ViewBag.TotalPages = pagedResult.TotalPages;
            ViewBag.PagedResult = pagedResult;

            var lots = pagedResult.Items.ToList();

            return View(lots);
        }

        // GET: Lots/Details/5
        [RequirePermission("Lots", "Read")]
        public async Task<IActionResult> Details(int id)
        {
            var lot = await _context.StockLots
                .Include(l => l.Material)
                    .ThenInclude(m => m!.Supplier)
                .Include(l => l.Warehouse)
                .FirstOrDefaultAsync(l => l.Id == id);

            if (lot == null)
                return NotFound();

            // Get history
            var history = await _lotService.GetLotHistory(id);
            ViewBag.History = history;

            // Get parent lot if exists
            if (!string.IsNullOrEmpty(lot.ParentLotId))
            {
                ViewBag.ParentLot = await _context.StockLots
                    .FirstOrDefaultAsync(l => l.LotNumber == lot.ParentLotId);
            }

            // Get child lots
            ViewBag.ChildLots = await _context.StockLots
                .Where(l => l.ParentLotId == lot.LotNumber)
                .ToListAsync();

            return View(lot);
        }

        // GET: Lots/Split/5
        [RequirePermission("Lots", "Update")]
        public async Task<IActionResult> Split(int id)
        {
            var lot = await _context.StockLots
                .Include(l => l.Material)
                .Include(l => l.Warehouse)
                .FirstOrDefaultAsync(l => l.Id == id);

            if (lot == null)
                return NotFound();

            if (!await _lotService.CanSplitLot(id))
            {
                TempData["Error"] = "Cannot split this lot (reserved or insufficient quantity)";
                return RedirectToAction(nameof(Details), new { id });
            }

            return View(lot);
        }

        // POST: Lots/Split/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Lots", "Update")]
        public async Task<IActionResult> Split(int id, [FromForm] List<decimal> quantities, [FromForm] string notes)
        {
            // Debug: Log received data
            var logger = HttpContext.RequestServices.GetRequiredService<ILogger<LotsController>>();
            logger.LogInformation($"Split request - LotId: {id}, Quantities: {(quantities != null ? string.Join(", ", quantities) : "null")}, Notes: {notes ?? "null"}");
            System.Diagnostics.Debug.WriteLine($"Split request - LotId: {id}, Quantities: {(quantities != null ? string.Join(", ", quantities) : "null")}, Notes: {notes ?? "null"}");
            
            // Log form data for debugging
            if (Request.HasFormContentType)
            {
                logger.LogInformation($"Form keys: {string.Join(", ", Request.Form.Keys)}");
                foreach (var key in Request.Form.Keys)
                {
                    logger.LogInformation($"Form[{key}] = {string.Join(", ", Request.Form[key].ToArray())}");
                }
            }
            
            // Validate input
            if (quantities == null || !quantities.Any())
            {
                logger.LogWarning($"Split failed: No quantities provided for lot {id}");
                TempData["Error"] = "Vui lòng nhập ít nhất một số lượng để phân tách lô";
                return RedirectToAction(nameof(Split), new { id });
            }

            // Filter out zero/negative values
            quantities = quantities.Where(q => q > 0).ToList();
            
            if (!quantities.Any())
            {
                logger.LogWarning($"Split failed: All quantities are zero or negative for lot {id}");
                TempData["Error"] = "Tất cả số lượng phải lớn hơn 0";
                return RedirectToAction(nameof(Split), new { id });
            }

            // Check if lot exists and can be split
            var lot = await _context.StockLots
                .Include(l => l.Material)
                .FirstOrDefaultAsync(l => l.Id == id);

            if (lot == null)
            {
                logger.LogWarning($"Split failed: Lot {id} not found");
                TempData["Error"] = "Không tìm thấy lô hàng";
                return RedirectToAction(nameof(Index));
            }

            if (lot.IsReserved)
            {
                logger.LogWarning($"Split failed: Lot {id} ({lot.LotNumber}) is reserved");
                TempData["Error"] = "Không thể phân tách lô đã được đặt chỗ";
                return RedirectToAction(nameof(Details), new { id });
            }

            var totalSplit = quantities.Sum();
            if (totalSplit > lot.Quantity)
            {
                var formatQty = (decimal q) => q % 1 == 0 ? q.ToString("N0") : q.ToString("N2");
                logger.LogWarning($"Split failed: Total split quantity {totalSplit} exceeds lot quantity {lot.Quantity} for lot {id}");
                TempData["Error"] = $"Tổng số lượng phân tách ({formatQty(totalSplit)}) vượt quá số lượng lô gốc ({formatQty(lot.Quantity)})";
                return RedirectToAction(nameof(Split), new { id });
            }

            try
            {
                logger.LogInformation($"Attempting to split lot {id} ({lot.LotNumber}) into {quantities.Count} lots with quantities: {string.Join(", ", quantities)}");
                var newLots = await _lotService.SplitLot(id, quantities, GetCurrentUserId(), notes);
                logger.LogInformation($"Successfully split lot {id} into {newLots.Count} new lots: {string.Join(", ", newLots.Select(l => l.LotNumber))}");
                TempData["Msg"] = $"Đã phân tách lô thành công thành {newLots.Count} lô mới";
                return RedirectToAction(nameof(Details), new { id });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error splitting lot {id}");
                TempData["Error"] = $"Lỗi khi phân tách lô: {ex.Message}";
                return RedirectToAction(nameof(Split), new { id });
            }
        }

        // GET: Lots/Merge
        [RequirePermission("Lots", "Update")]
        public async Task<IActionResult> Merge(string lotIds)
        {
            if (string.IsNullOrWhiteSpace(lotIds))
                return RedirectToAction(nameof(Index));

            var ids = lotIds.Split(',').Select(int.Parse).ToList();
            var lots = await _context.StockLots
                .Include(l => l.Material)
                .Include(l => l.Warehouse)
                .Where(l => ids.Contains(l.Id))
                .ToListAsync();

            if (lots.Count < 2)
            {
                TempData["Error"] = "Need at least 2 lots to merge";
                return RedirectToAction(nameof(Index));
            }

            if (!await _lotService.CanMergeLots(ids))
            {
                TempData["Error"] = "Cannot merge these lots (reserved, incompatible material/warehouse, or empty)";
                return RedirectToAction(nameof(Index));
            }

            return View(lots);
        }

        // POST: Lots/Merge
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Lots", "Update")]
        public async Task<IActionResult> MergeConfirmed(string lotIds, string notes)
        {
            if (string.IsNullOrWhiteSpace(lotIds))
                return RedirectToAction(nameof(Index));

            try
            {
                var ids = lotIds.Split(',').Select(int.Parse).ToList();
                var mergedLot = await _lotService.MergeLots(ids, GetCurrentUserId(), notes);
                TempData["Msg"] = $"Lots merged successfully into {mergedLot.LotNumber}";
                return RedirectToAction(nameof(Details), new { id = mergedLot.Id });
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error: {ex.Message}";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: Lots/Reserve/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Lots", "Update")]
        public async Task<IActionResult> Reserve(int id, decimal quantity, int issueId)
        {
            try
            {
                await _lotService.ReserveLot(id, quantity, issueId, GetCurrentUserId());
                TempData["Msg"] = "Lot reserved successfully";
                return RedirectToAction(nameof(Details), new { id });
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error: {ex.Message}";
                return RedirectToAction(nameof(Details), new { id });
            }
        }

        // POST: Lots/Release/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Lots", "Update")]
        public async Task<IActionResult> Release(int id)
        {
            try
            {
                await _lotService.ReleaseLot(id, GetCurrentUserId());
                TempData["Msg"] = "Lot reservation released";
                return RedirectToAction(nameof(Details), new { id });
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error: {ex.Message}";
                return RedirectToAction(nameof(Details), new { id });
            }
        }

        // GET: Lots/Expiring
        [RequirePermission("Lots", "Read")]
        public async Task<IActionResult> Expiring()
        {
            var today = DateTime.Today;
            var warningDate = today.AddDays(30);

            // Get all expiring/expired lots
            var expiringData = await _context.StockLots
                .Include(l => l.Material)
                .ThenInclude(m => m!.Supplier)
                .Include(l => l.Warehouse)
                .Where(l => l.Quantity > 0 && l.ExpiryDate != null && l.ExpiryDate <= warningDate)
                .OrderBy(l => l.ExpiryDate)
                .Select(l => new
                {
                    l.Id,
                    l.LotNumber,
                    l.MaterialId,
                    MaterialCode = l.Material!.Code,
                    MaterialName = l.Material.Name,
                    SupplierName = l.Material.Supplier != null ? l.Material.Supplier.Name : "",
                    l.Quantity,
                    Unit = l.Material.Unit,
                    l.ManufactureDate,
                    l.ExpiryDate,
                    WarehouseName = l.Warehouse!.Name,
                    DaysRemaining = EF.Functions.DateDiffDay(today, l.ExpiryDate!.Value)
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

        // GET: Lots/History/5
        [RequirePermission("Lots", "Read")]
        public async Task<IActionResult> History(int id)
        {
            var lot = await _context.StockLots
                .Include(l => l.Material)
                .Include(l => l.Warehouse)
                .FirstOrDefaultAsync(l => l.Id == id);

            if (lot == null)
                return NotFound();

            var history = await _lotService.GetLotHistory(id);
            ViewBag.Lot = lot;

            return View(history);
        }
    }
}









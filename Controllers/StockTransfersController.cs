using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using MNBEMART.Data;
using MNBEMART.Models;
using MNBEMART.Services;
using MNBEMART.ViewModels; 

namespace MNBEMART.Controllers
{
    [Authorize]
    public class StockTransfersController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IDocumentNumberingService _num;
        private readonly IStockService _stock;

        public StockTransfersController(AppDbContext db, IDocumentNumberingService num, IStockService stock)
        {
            _db = db; _num = num; _stock = stock;
        }

        public async Task<IActionResult> Index()
        {
            var data = await _db.StockTransfers
                .AsNoTracking()
                .Include(x => x.FromWarehouse)
                .Include(x => x.ToWarehouse)
                .Include(x => x.CreatedBy)
                .OrderByDescending(x => x.CreatedAt)
                .ToListAsync();
            return View(data);
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            var vm = new StockTransferVM
            {
                WarehouseOptions = await _db.Warehouses.AsNoTracking()
                    .OrderBy(w => w.Name)
                    .Select(w => new SelectListItem { Value = w.Id.ToString(), Text = w.Name })
                    .ToListAsync(),
                MaterialOptions = await _db.Materials.AsNoTracking()
                    .OrderBy(m => m.Name)
                    .Select(m => new SelectListItem { Value = m.Id.ToString(), Text = $"{m.Code} - {m.Name}" })
                    .ToListAsync()
            };
            //  vm.Details.Add(new StockTransferDetailVM());
            vm.Details.Add(new StockTransferDetailVM { Quantity = 0 });

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(StockTransferVM vm)
        {
            async Task FillOptionsAsync()
            {
                vm.WarehouseOptions = await _db.Warehouses.AsNoTracking()
                    .OrderBy(w => w.Name)
                    .Select(w => new SelectListItem { Value = w.Id.ToString(), Text = w.Name })
                    .ToListAsync();
                vm.MaterialOptions = await _db.Materials.AsNoTracking()
                    .OrderBy(m => m.Name)
                    .Select(m => new SelectListItem { Value = m.Id.ToString(), Text = $"{m.Code} - {m.Name}" })
                    .ToListAsync();
            }

            if (vm.FromWarehouseId == vm.ToWarehouseId)
                ModelState.AddModelError(nameof(vm.ToWarehouseId), "Kho đi và kho đến không được trùng.");

            // Gộp dòng trùng vật tư (cộng Qty)
            vm.Details = (vm.Details ?? new())
                .Where(d => d != null && d.MaterialId > 0 && d.Quantity > 0)
                .GroupBy(d => new { d.MaterialId, d.Unit, d.Note })
                .Select(g => new StockTransferDetailVM
                {
                    MaterialId = g.Key.MaterialId,
                    Unit = g.Key.Unit,
                    Note = g.Key.Note,
                    Quantity = g.Sum(x => x.Quantity)
                }).ToList();

            if (!vm.Details.Any())
                ModelState.AddModelError("Details", "Vui lòng nhập ít nhất 1 dòng chi tiết.");

            // Kiểm tra tồn kho kho đi
            var matIds = vm.Details.Select(d => d.MaterialId).Distinct().ToList();
            var onhands = await _db.Stocks.AsNoTracking()
                .Where(s => s.WarehouseId == vm.FromWarehouseId && matIds.Contains(s.MaterialId))
                .GroupBy(s => s.MaterialId)
                .Select(g => new { MaterialId = g.Key, Qty = g.Sum(x => x.Quantity) })
                .ToDictionaryAsync(x => x.MaterialId, x => x.Qty);

            foreach (var d in vm.Details)
            {
                var onhand = onhands.TryGetValue(d.MaterialId, out var q) ? q : 0m;
                if (d.Quantity > onhand)
                {
                    var matName = await _db.Materials.AsNoTracking()
                        .Where(m => m.Id == d.MaterialId)
                        .Select(m => m.Name)
                        .FirstOrDefaultAsync() ?? $"#{d.MaterialId}";
                    ModelState.AddModelError("Details", $"Vật tư '{matName}' vượt tồn kho: Yêu cầu {d.Quantity}, tồn {onhand}.");
                }
                   
            }
            if (!ModelState.IsValid)
            {
                await FillOptionsAsync();
                // if (!vm.Details.Any()) vm.Details.Add(new StockTransferDetailVM());
                if (!vm.Details.Any()) vm.Details.Add(new StockTransferDetailVM { Quantity = 0 });

                return View(vm);
            }

            // Map VM -> Entity
            var entity = new StockTransfer
            {
                TransferNumber = await _num.NextAsync("StockTransfer", null),
                FromWarehouseId = vm.FromWarehouseId,
                ToWarehouseId = vm.ToWarehouseId,
                Note = vm.Note,
                CreatedAt = DateTime.UtcNow,
            };
            var uidStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (int.TryParse(uidStr, out var uid)) entity.CreatedById = uid;
             var matUnits = await _db.Materials.AsNoTracking()
                        .Where(m => matIds.Contains(m.Id))
                        .ToDictionaryAsync(m => m.Id, m => m.Unit);
            foreach (var d in vm.Details)
            {
                entity.Details.Add(new StockTransferDetail
                {
                    MaterialId = d.MaterialId,
                    Quantity   = d.Quantity, // int -> decimal: OK
                    Unit       = !string.IsNullOrWhiteSpace(d.Unit)
                                    ? d.Unit
                                    : (matUnits.TryGetValue(d.MaterialId, out var u) ? u : null),
                    Note       = d.Note
                });

            }

            using var tx = await _db.Database.BeginTransactionAsync();
            _db.StockTransfers.Add(entity);
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var m = await _db.StockTransfers
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
        [Authorize(Roles = "admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Approve(int id)
        {
            using var tx = await _db.Database.BeginTransactionAsync();

            var t = await _db.StockTransfers
                .Include(x => x.Details)
                .FirstOrDefaultAsync(x => x.Id == id);
            if (t == null) return NotFound();
            if (t.Status != DocumentStatus.Moi) return BadRequest("Phiếu không ở trạng thái chờ duyệt.");

            await _stock.ApplyTransferAsync(t);
            await _stock.SaveAsync();

            t.Status = DocumentStatus.DaXacNhan;
            t.ApprovedAt = DateTime.UtcNow;
            var uidStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (int.TryParse(uidStr, out var uid)) t.ApprovedById = uid;

            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            return RedirectToAction(nameof(Details), new { id });
        }

        // API: tồn kho theo kho đi cho list vật tư
        // GET /StockTransfers/OnHand?fromWarehouseId=1&ids=10,11,12
        [HttpGet]
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

        // GET /StockTransfers/MaterialUnits?ids=10,11,12
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
}

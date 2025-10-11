using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using MNBEMART.Data;
using MNBEMART.Models;
using MNBEMART.ViewModels;

namespace MNBEMART.Controllers
{
    [Authorize]
    public class ReportsController : Controller
    {
        private readonly AppDbContext _db;
        public ReportsController(AppDbContext db) => _db = db;

        // ===== Helper: chuẩn hoá khoảng ngày [from..to] =====
        private static (DateTime from, DateTime to) NormRange(DateTime? from, DateTime? to)
        {
            var now = DateTime.Now;
            var f = from?.Date ?? new DateTime(now.Year, now.Month, 1);
            var t = to?.Date   ?? f.AddMonths(1).AddDays(-1);
            return (f, t);
        }

        // ==========================================================================================
        // 1) DOANH THU — lấy theo OutValue trong StockBalance (phiếu đã xuất)
        //    GroupBy: day | month | year    + lọc kho (tuỳ chọn)
        // ==========================================================================================
        [HttpGet]
        public async Task<IActionResult> Revenue([FromQuery] ReportFilterVM f)
        {
            var (from, to) = NormRange(f.From, f.To);
            f.GroupBy = string.IsNullOrWhiteSpace(f.GroupBy) ? "day" : f.GroupBy.ToLowerInvariant();

            ViewBag.Warehouses = new SelectList(
                await _db.Warehouses.AsNoTracking().OrderBy(w => w.Name).ToListAsync(),
                "Id", "Name", f.WarehouseId);
            ViewBag.Filter = f;

            var q = _db.StockBalances.AsNoTracking()
                    .Where(x => x.Date >= from && x.Date <= to);

            if (f.WarehouseId.HasValue)
                q = q.Where(x => x.WarehouseId == f.WarehouseId.Value);

            switch (f.GroupBy)
            {
                case "year":
                {
                    var rows = await q.GroupBy(x => x.Date.Year)
                        .Select(g => new { Year = g.Key, Total = g.Sum(z => z.OutValue) })
                        .OrderBy(x => x.Year)
                        .ToListAsync();

                    var data = rows.Select(x => new RevenueRowVM {
                        Period = x.Year.ToString(),
                        TotalRevenue = x.Total
                    }).ToList();

                    // tổng cộng
                    ViewBag.Total = data.Sum(x => x.TotalRevenue);
                    return View(data);
                }
                case "month":
                {
                    var rows = await q.GroupBy(x => new { x.Date.Year, x.Date.Month })
                        .Select(g => new { g.Key.Year, g.Key.Month, Total = g.Sum(z => z.OutValue) })
                        .OrderBy(x => x.Year).ThenBy(x => x.Month)
                        .ToListAsync();

                    var data = rows.Select(x => new RevenueRowVM {
                        Period = $"{x.Year}-{x.Month:00}",
                        TotalRevenue = x.Total
                    }).ToList();

                    ViewBag.Total = data.Sum(x => x.TotalRevenue);
                    return View(data);
                }
                default: // day
                {
                    var rows = await q.GroupBy(x => x.Date)
                        .Select(g => new { Day = g.Key, Total = g.Sum(z => z.OutValue) })
                        .OrderBy(x => x.Day)
                        .ToListAsync();

                    var data = rows.Select(x => new RevenueRowVM {
                        Period = x.Day.ToString("yyyy-MM-dd"),
                        TotalRevenue = x.Total
                    }).ToList();

                    ViewBag.Total = data.Sum(x => x.TotalRevenue);
                    return View(data);
                }
            }
        }

        // ==========================================================================================
        // 2) TỔNG HỢP NHẬP / XUẤT theo vật tư trong kỳ — từ StockBalance
        //    Có Qty + Value, lọc theo kho (tuỳ chọn)
        // ==========================================================================================
        [HttpGet]
        public async Task<IActionResult> Movements([FromQuery] ReportFilterVM f)
        {
            var now = DateTime.Now;
            // ✅ KHÔNG dùng .Value
            var from = f.From?.Date ?? now.Date.AddDays(-7);
            var to   = f.To?.Date   ?? now.Date;

            // Cập nhật filter để hiển thị lại trên View
            f.From = from;
            f.To = to;

            ViewBag.Warehouses = new SelectList(
                await _db.Warehouses.AsNoTracking().OrderBy(w => w.Name).ToListAsync(),
                "Id", "Name", f.WarehouseId);
            ViewBag.Filter = f;

            // Nên .Date để tránh dính phần thời gian
            var q = _db.StockBalances.AsNoTracking()
                .Where(x => x.Date.Date >= from && x.Date.Date <= to);

            if (f.WarehouseId.HasValue)
                q = q.Where(x => x.WarehouseId == f.WarehouseId.Value);

            var agg = await q.GroupBy(x => x.MaterialId)
                .Select(g => new {
                    MaterialId = g.Key,
                    QtyIn      = g.Sum(z => z.InQty),
                    QtyOut     = g.Sum(z => z.OutQty),
                    InValue    = g.Sum(z => z.InValue),
                    OutValue   = g.Sum(z => z.OutValue)
                })
                .ToListAsync();

            var mats = await _db.Materials.AsNoTracking()
                .Select(m => new { m.Id, m.Code, m.Name, m.Unit })
                .ToListAsync();

            var data = (from a in agg
                        join m in mats on a.MaterialId equals m.Id
                        orderby (a.QtyIn + a.QtyOut) descending
                        select new MovementRowVM {
                            MaterialId   = m.Id,
                            MaterialName = $"{m.Code} - {m.Name}",
                            QtyIn        = a.QtyIn,
                            QtyOut       = a.QtyOut
                            // Nếu muốn hiện giá trị:
                            // InValue = a.InValue, OutValue = a.OutValue
                        }).ToList();

            return View(data);
        }


        // ==========================================================================================
        // 3) TỒN KHO CUỐI KỲ theo vật tư — tính từ sổ StockBalance
        //    Begin = sum (đến trước from); In/Out = trong kỳ; End = Begin + In - Out
        // ==========================================================================================
        [HttpGet]
        public async Task<IActionResult> Inventory([FromQuery] ReportFilterVM f)
        {
            var (from, to) = NormRange(f.From, f.To);
            // Cập nhật filter để hiển thị lại trên View
            f.From = from; 
            f.To = to;

            ViewBag.Warehouses = new SelectList(
                await _db.Warehouses.AsNoTracking().OrderBy(w => w.Name).ToListAsync(),
                "Id", "Name", f.WarehouseId);
            ViewBag.Filter = f;

            var qAllBefore = _db.StockBalances.AsNoTracking()
                .Where(x => x.Date.Date < from);
            var qInPeriod  = _db.StockBalances.AsNoTracking()
                .Where(x => x.Date.Date >= from && x.Date.Date <= to);

            if (f.WarehouseId.HasValue)
            {
                qAllBefore = qAllBefore.Where(x => x.WarehouseId == f.WarehouseId.Value);
                qInPeriod  = qInPeriod.Where(x => x.WarehouseId == f.WarehouseId.Value);
            }

            var openAgg = await qAllBefore.GroupBy(x => x.MaterialId)
                .Select(g => new { MaterialId = g.Key, Qty = g.Sum(z => z.InQty - z.OutQty) })
                .ToListAsync();

            var moveAgg = await qInPeriod.GroupBy(x => x.MaterialId)
                .Select(g => new {
                    MaterialId = g.Key,
                    QtyIn      = g.Sum(z => z.InQty),
                    QtyOut     = g.Sum(z => z.OutQty)
                })
                .ToListAsync();

            var mats = await _db.Materials.AsNoTracking()
                .Select(m => new { m.Id, m.Code, m.Name, m.Unit })
                .ToListAsync();

            var openDict = openAgg.ToDictionary(x => x.MaterialId, x => x.Qty);
            var movDict  = moveAgg.ToDictionary(x => x.MaterialId, x => (x.QtyIn, x.QtyOut));

            var data = new List<InventoryRowVM>();
            foreach (var m in mats)
            {
                var begin = openDict.TryGetValue(m.Id, out var b) ? b : 0m;
                var (inQty, outQty) = movDict.TryGetValue(m.Id, out var t) ? t : (0m, 0m);
                var end = begin + inQty - outQty;

                if (begin != 0 || inQty != 0 || outQty != 0 || end != 0)
                {
                    data.Add(new InventoryRowVM {
                        MaterialId   = m.Id,
                        MaterialName = $"{m.Code} - {m.Name}",
                        BeginQty     = begin,
                        InQty        = inQty,
                        OutQty       = outQty
                        // EndQty là property tính: BeginQty + InQty - OutQty
                    });
                }
            }

            data = data.OrderBy(x => x.MaterialName).ToList();
            ViewBag.SumBegin = data.Sum(x => x.BeginQty);
            ViewBag.SumIn    = data.Sum(x => x.InQty);
            ViewBag.SumOut   = data.Sum(x => x.OutQty);
            ViewBag.SumEnd   = data.Sum(x => x.EndQty);

            return View(data);
        }

    }
}

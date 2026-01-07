using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;
using MNBEMART.Data;
using MNBEMART.Models;
using MNBEMART.Services;

namespace MNBEMART.Controllers;

[Authorize]
public class HomeController : Controller
{
    private readonly AppDbContext _context;
    private readonly IStockoutPredictionService _stockoutService;
    private readonly ILogger<HomeController> _logger;
    private readonly IMemoryCache _cache;
    
    public HomeController(AppDbContext context, IStockoutPredictionService stockoutService, ILogger<HomeController> logger, IMemoryCache cache)
    {
        _context = context;
        _stockoutService = stockoutService;
        _logger = logger;
        _cache = cache;
    }

    [Authorize]
    public IActionResult Index()
    {
        var fullName = User.Identity?.Name ?? "Người dùng";
        ViewBag.FullName = fullName;
        ViewBag.Role = User.FindFirst(ClaimTypes.Role)?.Value;
        return RedirectToAction("Dashboard");
    }

    // ==== DASHBOARD (ASYNC) ====
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> Dashboard()
    {
        // Buộc trình duyệt luôn lấy mới (không dùng cache) để sidebar phản ánh đúng user hiện tại
        Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
        Response.Headers["Pragma"] = "no-cache";
        Response.Headers["Expires"] = "0";

        // KPIs cơ bản
        ViewBag.TotalMaterials = await _context.Materials.CountAsync();
        ViewBag.TotalWarehouses = await _context.Warehouses.CountAsync();
        ViewBag.TotalReceipts = await _context.StockReceipts.CountAsync();
        ViewBag.TotalIssues = await _context.StockIssues.CountAsync();
        ViewBag.TotalUsers = await _context.Users.CountAsync();

        // Tổng SL tồn (tất cả kho)
        ViewBag.TotalStocks = await _context.Stocks.AsNoTracking()
            .SumAsync(s => (decimal?)s.Quantity) ?? 0m;

        // Cảnh báo tồn thấp (tuỳ chỉnh ngưỡng)
        const decimal LowLevel = 5m;
        ViewBag.LowStock = await _context.Stocks.AsNoTracking()
            .Include(s => s.Material).Include(s => s.Warehouse)
            .Where(s => s.Quantity <= LowLevel)
            .OrderBy(s => s.Warehouse.Name).ThenBy(s => s.Material.Code)
            .Take(10)
            .Select(s => new
            {
                MaterialCode = s.Material.Code,
                MaterialName = s.Material.Name,
                Unit = s.Material.Unit,
                WarehouseName = s.Warehouse.Name,
                Qty = (decimal?)s.Quantity,
                ReorderLevel = (decimal?)LowLevel
            })
            .ToListAsync();

        // Phiếu nhập gần đây
        ViewBag.RecentReceipts = await _context.StockReceipts.AsNoTracking()
            .OrderByDescending(x => x.CreatedAt).Take(6)
            .Select(x => new
            {
                Number = x.ReceiptNumber,
                WarehouseName = x.Warehouse.Name,
                CreatedAt = x.CreatedAt,
                Status = x.Status,
                SumQty = x.Details.Sum(d => (decimal?)((decimal)d.Quantity)) ?? 0m,
                SumAmt = x.Details.Sum(d => (decimal?)(((decimal)d.Quantity) * d.UnitPrice)) ?? 0m

            })
            .ToListAsync();

        // Phiếu xuất gần đây
        ViewBag.RecentIssues = await _context.StockIssues.AsNoTracking()
            .OrderByDescending(x => x.CreatedAt).Take(6)
            .Select(x => new
            {
                Number = x.IssueNumber,
                WarehouseName = x.Warehouse.Name,
                CreatedAt = x.CreatedAt,
                Status = x.Status,
                // SumQty        = x.Details.Sum(d => (decimal?)d.Quantity) ?? 0m,
                // SumAmt        = x.Details.Sum(d => (decimal?)(d.Quantity * d.UnitPrice)) ?? 0m
                SumQty = x.Details.Sum(d => (decimal?)((decimal)d.Quantity)) ?? 0m,
                SumAmt = x.Details.Sum(d => (decimal?)(((decimal)d.Quantity) * d.UnitPrice)) ?? 0m
            })
            .ToListAsync();

        // Tổng tồn theo kho
        ViewBag.StockByWarehouse = await _context.Stocks.AsNoTracking()
            .GroupBy(s => s.Warehouse.Name)
            .Select(g => new
            {
                WarehouseName = g.Key,
                Qty = g.Sum(s => (decimal?)s.Quantity) ?? 0m
            })
            .OrderByDescending(x => x.Qty)
            .ToListAsync();

        // ===== DỮ LIỆU CHO 2 MODAL "Tạo nhanh" - Cached =====
        const string warehousesCacheKey = "home_warehouses_list";
        const string materialsCacheKey = "home_materials_list";
        
        if (!_cache.TryGetValue(warehousesCacheKey, out List<SelectListItem>? cachedWarehouses))
        {
            cachedWarehouses = (await _context.Warehouses.AsNoTracking()
                .OrderBy(w => w.Name)
                .Select(w => new SelectListItem { Value = w.Id.ToString(), Text = w.Name })
                .ToListAsync());
            _cache.Set(warehousesCacheKey, cachedWarehouses, TimeSpan.FromMinutes(30));
        }
        ViewBag.WarehouseOptions = new SelectList(cachedWarehouses ?? new List<SelectListItem>(), "Value", "Text");

        if (!_cache.TryGetValue(materialsCacheKey, out List<Material>? cachedMaterials))
        {
            cachedMaterials = await _context.Materials.AsNoTracking()
                .OrderBy(m => m.Name)
                .ToListAsync();
            _cache.Set(materialsCacheKey, cachedMaterials, TimeSpan.FromMinutes(30));
        }
        ViewBag.Materials = cachedMaterials ?? new List<Material>();

        // Stockout predictions (AI)
        try
        {
            var stockoutPredictions = await _stockoutService.PredictStockoutsAsync(daysAhead: 14);
            ViewBag.StockoutPredictions = stockoutPredictions.Take(10).ToList(); // Top 10 nguy cơ cao nhất
            ViewBag.StockoutPredictionsCount = stockoutPredictions.Count; // Debug info
        }
        catch (Exception ex)
        {
            // Log error để debug
            _logger.LogError(ex, "Error loading stockout predictions");
            ViewBag.StockoutPredictions = new List<object>();
            ViewBag.StockoutPredictionsCount = 0;
        }



        // Doanh thu & chi phí theo tháng (12 tháng gần nhất)
        var now = DateTime.Now;
        var last12 = Enumerable.Range(0, 12)
            .Select(i => new DateTime(now.Year, now.Month, 1).AddMonths(-i))
            .OrderBy(x => x)
            .ToList();

        ViewBag.MonthlyStats = last12.Select(m => new
        {
            Month = m.ToString("MM/yyyy"),
            Revenue = _context.StockIssues
                .Where(x => x.CreatedAt.Month == m.Month && x.CreatedAt.Year == m.Year)
                .SelectMany(x => x.Details)
                .Sum(d => (decimal?)((decimal)d.Quantity * d.UnitPrice)) ?? 0m,
            Expense = _context.StockReceipts
                .Where(x => x.CreatedAt.Month == m.Month && x.CreatedAt.Year == m.Year)
                .SelectMany(x => x.Details)
                .Sum(d => (decimal?)((decimal)d.Quantity * d.UnitPrice)) ?? 0m
        }).ToList();

        // Top 5 nguyên liệu tồn cao nhất
        ViewBag.TopStocksHigh = await _context.Stocks.AsNoTracking()
            .Include(s => s.Material).Include(s => s.Warehouse)
            .OrderByDescending(s => s.Quantity).Take(5)
            .Select(s => new
            {
                MaterialCode = s.Material.Code,
                MaterialName = s.Material.Name,
                WarehouseName = s.Warehouse.Name,
                Qty = s.Quantity,
                Unit = s.Material.Unit
            }).ToListAsync();

        // Top 5 nguyên liệu tồn thấp nhất
        ViewBag.TopStocksLow = await _context.Stocks.AsNoTracking()
            .Include(s => s.Material).Include(s => s.Warehouse)
            .OrderBy(s => s.Quantity).Take(5)
            .Select(s => new
            {
                MaterialCode = s.Material.Code,
                MaterialName = s.Material.Name,
                WarehouseName = s.Warehouse.Name,
                Qty = s.Quantity,
                Unit = s.Material.Unit
            }).ToListAsync();

        // Cảnh báo lô sắp hết hạn (HSD <= 30 ngày hoặc đã hết hạn)
        var today = DateTime.Now.Date;
        var warningDate = today.AddDays(30);
        
        ViewBag.ExpiringLots = await _context.StockLots.AsNoTracking()
            .Include(l => l.Material).Include(l => l.Warehouse)
            .Where(l => l.Quantity > 0 && l.ExpiryDate != null && l.ExpiryDate <= warningDate)
            .OrderBy(l => l.ExpiryDate)
            .Take(10)
            .Select(l => new
            {
                LotNumber = l.LotNumber,
                MaterialCode = l.Material.Code,
                MaterialName = l.Material.Name,
                WarehouseName = l.Warehouse.Name,
                Quantity = l.Quantity,
                Unit = l.Material.Unit,
                ManufactureDate = l.ManufactureDate,
                ExpiryDate = l.ExpiryDate,
                DaysLeft = l.ExpiryDate != null ? (int)(l.ExpiryDate.Value.Date - today).TotalDays : 0
            })
            .ToListAsync();

        // Tổng số lô hàng đang quản lý
        ViewBag.TotalLots = await _context.StockLots
            .Where(l => l.Quantity > 0)
            .CountAsync();

        // Tổng số lô đã hết hạn
        ViewBag.ExpiredLots = await _context.StockLots
            .Where(l => l.Quantity > 0 && l.ExpiryDate != null && l.ExpiryDate < today)
            .CountAsync();

        // Tổng số lô sắp hết hạn (7-30 ngày)
        var sevenDays = today.AddDays(7);
        ViewBag.ExpiringSoonLots = await _context.StockLots
            .Where(l => l.Quantity > 0 && l.ExpiryDate != null && 
                   l.ExpiryDate >= today && l.ExpiryDate <= warningDate)
            .CountAsync();

                
        return View();

    }

    public IActionResult Privacy() => View();

    public IActionResult Error() =>
        View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });

    // ===== CHART API ENDPOINTS =====
    
    [HttpGet]
    public async Task<IActionResult> GetTrendsChartData()
    {
        // Last 30 days receipt/issue trends
        var endDate = DateTime.Today;
        var startDate = endDate.AddDays(-29);
        
        var dates = Enumerable.Range(0, 30)
            .Select(i => startDate.AddDays(i))
            .ToList();

        var receipts = await _context.StockBalances
            .Where(sb => sb.Date >= startDate && sb.Date <= endDate)
            .GroupBy(sb => sb.Date)
            .Select(g => new { Date = g.Key, Total = g.Sum(x => x.InQty) })
            .ToDictionaryAsync(x => x.Date, x => x.Total);

        var issues = await _context.StockBalances
            .Where(sb => sb.Date >= startDate && sb.Date <= endDate)
            .GroupBy(sb => sb.Date)
            .Select(g => new { Date = g.Key, Total = g.Sum(x => x.OutQty) })
            .ToDictionaryAsync(x => x.Date, x => x.Total);

        return Json(new
        {
            labels = dates.Select(d => d.ToString("dd/MM")).ToArray(),
            datasets = new[]
            {
                new
                {
                    label = "Nhập kho",
                    data = dates.Select(d => receipts.GetValueOrDefault(d, 0)).ToArray(),
                    borderColor = "rgb(16, 185, 129)",
                    backgroundColor = "rgba(16, 185, 129, 0.1)",
                    tension = 0.4
                },
                new
                {
                    label = "Xuất kho",
                    data = dates.Select(d => issues.GetValueOrDefault(d, 0)).ToArray(),
                    borderColor = "rgb(239, 68, 68)",
                    backgroundColor = "rgba(239, 68, 68, 0.1)",
                    tension = 0.4
                }
            }
        });
    }

    [HttpGet]
    public async Task<IActionResult> GetDistributionChartData()
    {
        // Stock distribution by warehouse
        var distribution = await _context.Stocks
            .GroupBy(s => s.Warehouse.Name)
            .Select(g => new
            {
                Warehouse = g.Key,
                TotalQty = g.Sum(s => s.Quantity)
            })
            .OrderByDescending(x => x.TotalQty)
            .ToListAsync();

        return Json(new
        {
            labels = distribution.Select(x => x.Warehouse).ToArray(),
            datasets = new[]
            {
                new
                {
                    label = "Tồn kho",
                    data = distribution.Select(x => x.TotalQty).ToArray(),
                    backgroundColor = new[]
                    {
                        "rgba(59, 130, 246, 0.8)",
                        "rgba(16, 185, 129, 0.8)",
                        "rgba(251, 146, 60, 0.8)",
                        "rgba(139, 92, 246, 0.8)",
                        "rgba(236, 72, 153, 0.8)",
                        "rgba(14, 165, 233, 0.8)"
                    },
                    borderWidth = 2,
                    borderColor = "#fff"
                }
            }
        });
    }
}

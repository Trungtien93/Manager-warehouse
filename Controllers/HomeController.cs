using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;           // <— THÊM DÒNG NÀY
using Microsoft.AspNetCore.Mvc.Rendering;
using MNBEMART.Data;
using MNBEMART.Models;

namespace MNBEMART.Controllers;

[Authorize]
public class HomeController : Controller
{
    private readonly AppDbContext _context;
    public HomeController(AppDbContext context) => _context = context;

    [Authorize]
    public IActionResult Index()
    {
        var fullName = User.Identity?.Name ?? "Người dùng";
        ViewBag.FullName = fullName;
        ViewBag.Role = User.FindFirst(ClaimTypes.Role)?.Value;
        return RedirectToAction("Dashboard");
    }

    // ==== DASHBOARD (ASYNC) ====
    public async Task<IActionResult> Dashboard()
    {
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

        // ===== DỮ LIỆU CHO 2 MODAL “Tạo nhanh” =====
        ViewBag.WarehouseOptions = new SelectList(
            await _context.Warehouses.AsNoTracking()
                .OrderBy(w => w.Name).ToListAsync(),
            "Id", "Name");

        ViewBag.Materials = await _context.Materials.AsNoTracking()
            .OrderBy(m => m.Name).ToListAsync();



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

                
        return View();        

    }

    public IActionResult Privacy() => View();

    public IActionResult Error() =>
        View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using MNBEMART.Data;

public class StocksController : Controller
{
    // private readonly AppDbContext _context;

    // public InventoryController(AppDbContext context)
    // {
    //     _context = context;
    // }

    // public async Task<IActionResult> Index()
    // {
    //     var stocks = await _context.Stocks
    //         .Include(s => s.Warehouse)
    //         .Include(s => s.Material)
    //         .OrderBy(s => s.Warehouse.Name)
    //         .ThenBy(s => s.Material.Name)
    //         .ToListAsync();

    //     return View(stocks);
    // }
    private readonly AppDbContext _db;
    public StocksController(AppDbContext db) { _db = db; }

    public async Task<IActionResult> Index(string? q, int? warehouseId, int page = 1, int pageSize = 50)
    {
        var query = _db.Stocks
            .AsNoTracking()
            .Include(s => s.Warehouse)
            .Include(s => s.Material)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
        {
            q = q.Trim();
            query = query.Where(s =>
                EF.Functions.Like(s.Material.Code, $"%{q}%") ||
                EF.Functions.Like(s.Material.Name, $"%{q}%") ||
                EF.Functions.Like(s.Warehouse.Name, $"%{q}%"));
        }

        if (warehouseId.HasValue)
            query = query.Where(s => s.WarehouseId == warehouseId.Value);

        var totalItems = await query.CountAsync();
        var totalQty   = await query.SumAsync(s => (decimal?)s.Quantity) ?? 0m;

        var items = await query
            .OrderBy(s => s.Warehouse.Name)
            .ThenBy(s => s.Material.Code)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        ViewBag.WarehouseOptions = new SelectList(
            await _db.Warehouses.AsNoTracking().OrderBy(w => w.Name).ToListAsync(),
            "Id", "Name", warehouseId);

        ViewBag.Q           = q;
        ViewBag.WarehouseId = warehouseId;
        ViewBag.Page        = page;
        ViewBag.PageSize    = pageSize;
        ViewBag.TotalItems  = totalItems;
        ViewBag.TotalQty    = totalQty;
        ViewBag.TotalPages  = (int)Math.Ceiling(totalItems / (double)pageSize);

        return View(items);
    }
}

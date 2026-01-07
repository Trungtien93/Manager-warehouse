using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using MNBEMART.Data;
using Microsoft.AspNetCore.Authorization;
using MNBEMART.Filters;
using MNBEMART.Extensions;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

[Authorize]
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

    [RequirePermission("Stocks", "Read")]
    public async Task<IActionResult> Index(string? q, int? warehouseId, int page = 1, int pageSize = 50, string costingMethod = "periodic")
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

        var totalQty = await query.SumAsync(s => (decimal?)s.Quantity) ?? 0m;

        // Lấy tất cả stocks (không phân trang) để tính tổng giá trị
        var allStocks = await query
            .OrderBy(s => s.Warehouse.Name)
            .ThenBy(s => s.Material.Code)
            .ToListAsync();

        var pagedResult = await query
            .OrderBy(s => s.Warehouse.Name)
            .ThenBy(s => s.Material.Code)
            .ToPagedResultAsync(page, pageSize);

        var items = pagedResult.Items.ToList();

        // Tính giá trị tồn kho theo phương pháp được chọn
        decimal totalValue = 0m;
        Dictionary<(int WarehouseId, int MaterialId), (decimal UnitPrice, decimal Value)> stockValues = new();

        if (costingMethod == "fifo" || costingMethod == "weighted")
        {
            // Lấy danh sách (WarehouseId, MaterialId) từ tất cả stocks (cho tổng giá trị)
            var allStockKeys = allStocks.Select(s => (s.WarehouseId, s.MaterialId)).Distinct().ToHashSet();
            
            // Lấy danh sách từ items (cho hiển thị trong bảng)
            var pageStockKeys = items.Select(s => (s.WarehouseId, s.MaterialId)).Distinct().ToHashSet();

            // Lấy tất cả warehouse IDs và material IDs để filter
            var warehouseIds = allStockKeys.Select(k => k.WarehouseId).Distinct().ToList();
            var materialIds = allStockKeys.Select(k => k.MaterialId).Distinct().ToList();

            // Query StockLots cho tất cả các kho và vật tư
            var lotsQuery = _db.StockLots
                .AsNoTracking()
                .Where(l => l.Quantity > 0 
                    && warehouseIds.Contains(l.WarehouseId) 
                    && materialIds.Contains(l.MaterialId));

            if (warehouseId.HasValue)
                lotsQuery = lotsQuery.Where(l => l.WarehouseId == warehouseId.Value);

            // Materialize query, sau đó filter trong memory để match exact combinations
            var allLots = await lotsQuery.ToListAsync();
            var lots = allLots.Where(l => allStockKeys.Contains((l.WarehouseId, l.MaterialId))).ToList();

            // Nhóm theo (WarehouseId, MaterialId)
            var lotsByStock = lots.GroupBy(l => (l.WarehouseId, l.MaterialId)).ToList();

            // Tính giá trị cho tất cả stocks (để có tổng giá trị)
            foreach (var group in lotsByStock)
            {
                var (whId, matId) = group.Key;
                var stockLots = group.ToList();

                // Lấy số lượng thực tế từ bảng Stocks (từ allStocks)
                var stock = allStocks.FirstOrDefault(s => s.WarehouseId == whId && s.MaterialId == matId);
                if (stock == null) continue;

                decimal unitPrice = 0m;
                decimal quantity = stock.Quantity;

                if (costingMethod == "fifo")
                {
                    // FIFO: Lấy giá của lô cũ nhất
                    var oldestLot = stockLots
                        .Where(l => l.UnitPrice.HasValue)
                        .OrderBy(l => l.CreatedAt)
                        .FirstOrDefault();
                    
                    if (oldestLot?.UnitPrice.HasValue == true)
                    {
                        unitPrice = oldestLot.UnitPrice.Value;
                    }
                }
                else if (costingMethod == "weighted")
                {
                    // Bình quân gia quyền: Tổng giá trị / Tổng số lượng từ lots
                    var totalValueForGroup = stockLots
                        .Where(l => l.UnitPrice.HasValue)
                        .Sum(l => l.Quantity * l.UnitPrice.Value);
                    
                    var totalLotQty = stockLots.Sum(l => l.Quantity);
                    
                    if (totalLotQty > 0)
                    {
                        unitPrice = totalValueForGroup / totalLotQty;
                    }
                }

                if (unitPrice > 0 && quantity > 0)
                {
                    var value = quantity * unitPrice;
                    
                    // Chỉ lưu vào stockValues nếu nằm trong trang hiện tại
                    if (pageStockKeys.Contains((whId, matId)))
                    {
                        stockValues[(whId, matId)] = (unitPrice, value);
                    }
                    
                    totalValue += value;
                }
            }
        }

        ViewBag.WarehouseOptions = new SelectList(
            await _db.Warehouses.AsNoTracking().OrderBy(w => w.Name).ToListAsync(),
            "Id", "Name", warehouseId);

        ViewBag.Q             = q;
        ViewBag.WarehouseId   = warehouseId;
        ViewBag.Page          = pagedResult.Page;
        ViewBag.PageSize      = pagedResult.PageSize;
        ViewBag.TotalItems    = pagedResult.TotalItems;
        ViewBag.TotalQty      = totalQty;
        ViewBag.TotalPages    = pagedResult.TotalPages;
        ViewBag.PagedResult   = pagedResult;
        ViewBag.CostingMethod = costingMethod;
        ViewBag.StockValues   = stockValues;
        ViewBag.TotalValue    = totalValue;

        return View(items);
    }

    [RequirePermission("Stocks", "Read")]
    public async Task<IActionResult> ExportPdf(string? q, int? warehouseId, string costingMethod = "periodic")
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

        var allStocks = await query
            .OrderBy(s => s.Warehouse.Name)
            .ThenBy(s => s.Material.Code)
            .ToListAsync();

        var totalQty = allStocks.Sum(s => s.Quantity);
        decimal totalValue = 0m;
        Dictionary<(int WarehouseId, int MaterialId), (decimal UnitPrice, decimal Value)> stockValues = new();
        bool showValue = costingMethod == "fifo" || costingMethod == "weighted";

        if (showValue)
        {
            var allStockKeys = allStocks.Select(s => (s.WarehouseId, s.MaterialId)).Distinct().ToHashSet();
            var warehouseIds = allStockKeys.Select(k => k.WarehouseId).Distinct().ToList();
            var materialIds = allStockKeys.Select(k => k.MaterialId).Distinct().ToList();

            var lotsQuery = _db.StockLots
                .AsNoTracking()
                .Where(l => l.Quantity > 0 
                    && warehouseIds.Contains(l.WarehouseId) 
                    && materialIds.Contains(l.MaterialId));

            if (warehouseId.HasValue)
                lotsQuery = lotsQuery.Where(l => l.WarehouseId == warehouseId.Value);

            var allLots = await lotsQuery.ToListAsync();
            var lots = allLots.Where(l => allStockKeys.Contains((l.WarehouseId, l.MaterialId))).ToList();
            var lotsByStock = lots.GroupBy(l => (l.WarehouseId, l.MaterialId)).ToList();

            foreach (var group in lotsByStock)
            {
                var (whId, matId) = group.Key;
                var stockLots = group.ToList();
                var stock = allStocks.FirstOrDefault(s => s.WarehouseId == whId && s.MaterialId == matId);
                if (stock == null) continue;

                decimal unitPrice = 0m;
                decimal quantity = stock.Quantity;

                if (costingMethod == "fifo")
                {
                    var oldestLot = stockLots
                        .Where(l => l.UnitPrice.HasValue)
                        .OrderBy(l => l.CreatedAt)
                        .FirstOrDefault();
                    
                    if (oldestLot?.UnitPrice.HasValue == true)
                        unitPrice = oldestLot.UnitPrice.Value;
                }
                else if (costingMethod == "weighted")
                {
                    var totalValueForGroup = stockLots
                        .Where(l => l.UnitPrice.HasValue)
                        .Sum(l => l.Quantity * l.UnitPrice.Value);
                    var totalLotQty = stockLots.Sum(l => l.Quantity);
                    
                    if (totalLotQty > 0)
                        unitPrice = totalValueForGroup / totalLotQty;
                }

                if (unitPrice > 0 && quantity > 0)
                {
                    var value = quantity * unitPrice;
                    stockValues[(whId, matId)] = (unitPrice, value);
                    totalValue += value;
                }
            }
        }

        // Lấy thông tin kho nếu có filter
        string warehouseName = "Tất cả kho";
        if (warehouseId.HasValue)
        {
            var warehouse = await _db.Warehouses.FindAsync(warehouseId.Value);
            warehouseName = warehouse?.Name ?? "Tất cả kho";
        }

        string methodName = costingMethod switch
        {
            "fifo" => "FIFO",
            "weighted" => "Bình quân gia quyền",
            _ => "Kiểm tra định kỳ"
        };

        // Tạo PDF
        QuestPDF.Settings.License = LicenseType.Community;
        var document = QuestPDF.Fluent.Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2f, Unit.Centimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Times New Roman"));

                page.Content()
                    .Column(column =>
                    {
                        // Header - Form number và thông tin đơn vị
                        column.Item().Row(row =>
                        {
                            // Bên trái: Đơn vị, Mã QHNS
                            row.RelativeItem().Column(leftCol =>
                            {
                                leftCol.Item().Text("Đơn vị: ").FontSize(10).FontColor(Colors.Black).FontFamily("Times New Roman");
                                leftCol.Item().PaddingTop(4).Text("Mã QHNS: ").FontSize(10).FontColor(Colors.Black).FontFamily("Times New Roman");
                            });

                            // Bên phải: Mẫu số C30-HD
                            row.RelativeItem().Column(rightCol =>
                            {
                                rightCol.Item().AlignRight().Text("Mẫu số C30 - HD")
                                    .FontSize(10)
                                    .Bold()
                                    .FontColor(Colors.Black)
                                    .FontFamily("Times New Roman");
                                rightCol.Item().PaddingTop(2).AlignRight().Text("(Ban hành kèm theo thông tư 107/2017/TT-BTC")
                                    .FontSize(8)
                                    .Italic()
                                    .FontColor(Colors.Grey.Darken1)
                                    .FontFamily("Times New Roman");
                                rightCol.Item().AlignRight().Text("ngày 24/11/2017)")
                                    .FontSize(8)
                                    .Italic()
                                    .FontColor(Colors.Grey.Darken1)
                                    .FontFamily("Times New Roman");
                            });
                        });

                        column.Item().PaddingTop(15);

                        // Tiêu đề chính
                        column.Item().AlignCenter().Text("BÁO CÁO TỒN KHO")
                            .FontSize(20)
                            .Bold()
                            .FontColor(Colors.Black)
                            .FontFamily("Times New Roman");

                        column.Item().PaddingTop(8);
                        column.Item().AlignCenter().Text($"Ngày xuất: {DateTime.Now:dd/MM/yyyy HH:mm}")
                            .FontSize(10)
                            .FontColor(Colors.Grey.Darken2)
                            .FontFamily("Times New Roman");

                        column.Item().PaddingTop(20);
                        // Thông tin bộ lọc
                        column.Item().PaddingBottom(20).Column(filterCol =>
                        {
                            filterCol.Item().PaddingBottom(6).Row(row =>
                            {
                                row.AutoItem().Text("Kho: ").FontSize(10).FontColor(Colors.Black).FontFamily("Times New Roman");
                                row.RelativeItem().Text(warehouseName).FontSize(10).Bold().FontColor(Colors.Black).FontFamily("Times New Roman");
                            });

                            if (!string.IsNullOrWhiteSpace(q))
                            {
                                filterCol.Item().PaddingBottom(6).Row(row =>
                                {
                                    row.AutoItem().Text("Từ khóa: ").FontSize(10).FontColor(Colors.Black).FontFamily("Times New Roman");
                                    row.RelativeItem().Text(q).FontSize(10).Bold().FontColor(Colors.Black).FontFamily("Times New Roman");
                                });
                            }

                            filterCol.Item().Row(row =>
                            {
                                row.AutoItem().Text("Phương pháp tính: ").FontSize(10).FontColor(Colors.Black).FontFamily("Times New Roman");
                                row.RelativeItem().Text(methodName).FontSize(10).Bold().FontColor(Colors.Black).FontFamily("Times New Roman");
                            });
                        });

                        // Bảng dữ liệu
                        column.Item().Table(table =>
                        {
                            // Column definitions
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(2.2f); // Kho
                                columns.RelativeColumn(2); // Mã NL
                                columns.RelativeColumn(3.5f); // Tên NL
                                columns.RelativeColumn(1.3f); // ĐVT
                                columns.RelativeColumn(2); // Số lượng
                                if (showValue)
                                {
                                    columns.RelativeColumn(2.2f); // Đơn giá
                                    columns.RelativeColumn(2.8f); // Giá trị
                                }
                            });

                            // Header với style đẹp hơn
                            table.Header(header =>
                            {
                                header.Cell().Element(HeaderCellStyle).Text("Kho").Bold().FontSize(11).FontFamily("Times New Roman");
                                header.Cell().Element(HeaderCellStyle).Text("Mã NL").Bold().FontSize(11).FontFamily("Times New Roman");
                                header.Cell().Element(HeaderCellStyle).Text("Tên nguyên liệu").Bold().FontSize(11).FontFamily("Times New Roman");
                                header.Cell().Element(HeaderCellStyle).AlignCenter().Text("ĐVT").Bold().FontSize(11).FontFamily("Times New Roman");
                                header.Cell().Element(HeaderCellStyle).AlignRight().Text("Số lượng").Bold().FontSize(11).FontFamily("Times New Roman");
                                if (showValue)
                                {
                                    header.Cell().Element(HeaderCellStyle).AlignRight().Text("Đơn giá").Bold().FontSize(11).FontFamily("Times New Roman");
                                    header.Cell().Element(HeaderCellStyle).AlignRight().Text("Giá trị").Bold().FontSize(11).FontFamily("Times New Roman");
                                }
                            });

                            // Data rows với alternate colors
                            int rowIndex = 0;
                            foreach (var stock in allStocks)
                            {
                                var key = (stock.WarehouseId, stock.MaterialId);
                                var hasValue = stockValues.TryGetValue(key, out var valueInfo);
                                bool isEvenRow = rowIndex % 2 == 0;
                                
                                table.Cell().Element(c => DataCellStyle(c, isEvenRow)).Text(stock.Warehouse?.Name ?? "").FontSize(10).FontFamily("Times New Roman");
                                table.Cell().Element(c => DataCellStyle(c, isEvenRow)).Text(stock.Material?.Code ?? "").FontSize(10).FontFamily("Times New Roman");
                                table.Cell().Element(c => DataCellStyle(c, isEvenRow)).Text(stock.Material?.Name ?? "").FontSize(10).FontFamily("Times New Roman");
                                table.Cell().Element(c => DataCellStyle(c, isEvenRow)).AlignCenter().Text(stock.Material?.Unit ?? "").FontSize(10).FontFamily("Times New Roman");
                                table.Cell().Element(c => DataCellStyle(c, isEvenRow)).AlignRight().Text(FormatQty(stock.Quantity)).FontSize(10).FontFamily("Times New Roman");
                                
                                if (showValue)
                                {
                                    if (hasValue && valueInfo.UnitPrice > 0)
                                        table.Cell().Element(c => DataCellStyle(c, isEvenRow)).AlignRight().Text(FormatMoney(valueInfo.UnitPrice)).FontSize(10).FontFamily("Times New Roman");
                                    else
                                        table.Cell().Element(c => DataCellStyle(c, isEvenRow)).AlignRight().Text("-").FontSize(10).FontColor(Colors.Grey.Medium).FontFamily("Times New Roman");
                                    
                                    if (hasValue && valueInfo.Value > 0)
                                        table.Cell().Element(c => DataCellStyle(c, isEvenRow)).AlignRight().Text(FormatMoney(valueInfo.Value)).FontSize(10).FontFamily("Times New Roman").Bold();
                                    else
                                        table.Cell().Element(c => DataCellStyle(c, isEvenRow)).AlignRight().Text("-").FontSize(10).FontColor(Colors.Grey.Medium).FontFamily("Times New Roman");
                                }
                                
                                rowIndex++;
                            }
                        });

                        // Separator line trước phần tổng hợp
                        column.Item().PaddingTop(25).PaddingBottom(15).LineHorizontal(1).LineColor(Colors.Black);

                        // Tổng hợp
                        column.Item().PaddingTop(10).Column(summaryCol =>
                        {
                            summaryCol.Item().PaddingBottom(8).Row(row =>
                            {
                                row.AutoItem().Text("Tổng số dòng: ").FontSize(11).FontColor(Colors.Black).FontFamily("Times New Roman");
                                row.RelativeItem().Text(allStocks.Count.ToString()).FontSize(11).Bold().FontColor(Colors.Black).FontFamily("Times New Roman");
                            });

                            summaryCol.Item().PaddingBottom(8).Row(row =>
                            {
                                row.AutoItem().Text("Tổng số lượng: ").FontSize(11).FontColor(Colors.Black).FontFamily("Times New Roman");
                                row.RelativeItem().Text(FormatQty(totalQty)).FontSize(11).Bold().FontFamily("Times New Roman").FontColor(Colors.Black);
                            });

                            if (showValue && totalValue > 0)
                            {
                                summaryCol.Item().Row(row =>
                                {
                                    row.AutoItem().Text("Tổng giá trị: ").FontSize(11).FontColor(Colors.Black).FontFamily("Times New Roman");
                                    row.RelativeItem().Text(FormatMoney(totalValue)).FontSize(12).Bold().FontFamily("Times New Roman").FontColor(Colors.Black);
                                });
                            }
                        });
                    });

                // Footer với border line
                page.Footer()
                    .PaddingTop(10)
                    .BorderTop(1)
                    .BorderColor(Colors.Grey.Lighten1)
                    .AlignCenter()
                    .Text(x =>
                    {
                        x.Span("Trang ").FontSize(9).FontColor(Colors.Grey.Medium).FontFamily("Times New Roman");
                        x.CurrentPageNumber().FontSize(9).FontColor(Colors.Grey.Darken1).Bold().FontFamily("Times New Roman");
                        x.Span(" / ").FontSize(9).FontColor(Colors.Grey.Medium).FontFamily("Times New Roman");
                        x.TotalPages().FontSize(9).FontColor(Colors.Grey.Darken1).Bold().FontFamily("Times New Roman");
                    });
            });
        });

        var pdfBytes = document.GeneratePdf();
        var fileName = $"TonKho_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
        return File(pdfBytes, "application/pdf", fileName);
    }

    private static IContainer HeaderCellStyle(IContainer container)
    {
        return container
            .Border(1)
            .BorderColor(Colors.Black)
            .PaddingVertical(12)
            .PaddingHorizontal(10)
            .Background(Colors.White)
            .AlignMiddle()
            .AlignCenter();
    }

    private static IContainer DataCellStyle(IContainer container, bool isEvenRow)
    {
        return container
            .Border(1)
            .BorderColor(Colors.Black)
            .PaddingVertical(10)
            .PaddingHorizontal(8)
            .Background(Colors.White)
            .AlignMiddle();
    }

    private static string FormatQty(decimal v)
    {
        return (v % 1m == 0m) ? ((long)v).ToString() : v.ToString("0.##");
    }

    private static string FormatMoney(decimal v)
    {
        return v.ToString("N0") + " đ";
    }
}

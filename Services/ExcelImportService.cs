using ClosedXML.Excel;
using MNBEMART.Data;
using MNBEMART.Models;
using MNBEMART.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace MNBEMART.Services
{
    public class ExcelImportService : IImportService
    {
        private readonly AppDbContext _context;

        public ExcelImportService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<ImportResultVM> ImportMaterials(Stream excelStream)
        {
            var result = new ImportResultVM();

            try
            {
                using var workbook = new XLWorkbook(excelStream);
                var worksheet = workbook.Worksheet(1);

                // Get existing codes to check duplicates
                var existingCodes = await _context.Materials
                    .Select(m => m.Code.ToLower())
                    .ToHashSetAsync();

                var suppliersDict = await _context.Suppliers
                    .ToDictionaryAsync(s => s.Name.ToLower(), s => s.Id);

                var warehousesDict = await _context.Warehouses
                    .ToDictionaryAsync(w => w.Name.ToLower(), w => w.Id);

                int rowNumber = 1;
                foreach (var row in worksheet.RowsUsed().Skip(1)) // Skip header
                {
                    rowNumber++;
                    try
                    {
                        var code = row.Cell(1).GetString().Trim();
                        var name = row.Cell(2).GetString().Trim();
                        var unit = row.Cell(3).GetString().Trim();
                        var purchasePriceStr = row.Cell(4).GetString().Trim();
                        var salePriceStr = row.Cell(5).GetString().Trim();
                        var supplierName = row.Cell(6).GetString().Trim();
                        var warehouseName = row.Cell(7).GetString().Trim();
                        var description = row.Cell(8).GetString().Trim();

                        // Validation
                        if (string.IsNullOrWhiteSpace(code))
                        {
                            result.Errors.Add($"Dòng {rowNumber}: Mã vật tư không được để trống");
                            result.ErrorCount++;
                            continue;
                        }

                        if (existingCodes.Contains(code.ToLower()))
                        {
                            result.Errors.Add($"Dòng {rowNumber}: Mã '{code}' đã tồn tại");
                            result.ErrorCount++;
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(name))
                        {
                            result.Errors.Add($"Dòng {rowNumber}: Tên vật tư không được để trống");
                            result.ErrorCount++;
                            continue;
                        }

                        // Parse prices
                        if (!decimal.TryParse(purchasePriceStr.Replace(",", ""), out decimal purchasePrice))
                            purchasePrice = 0;

                        if (!decimal.TryParse(salePriceStr.Replace(",", ""), out decimal salePrice))
                            salePrice = 0;

                        // Find supplier
                        int? supplierId = null;
                        if (!string.IsNullOrWhiteSpace(supplierName))
                        {
                            if (suppliersDict.TryGetValue(supplierName.ToLower(), out int sid))
                                supplierId = sid;
                            else
                                result.Warnings.Add($"Dòng {rowNumber}: Không tìm thấy NCC '{supplierName}'");
                        }

                        // Find warehouse
                        int? warehouseId = null;
                        if (!string.IsNullOrWhiteSpace(warehouseName))
                        {
                            if (warehousesDict.TryGetValue(warehouseName.ToLower(), out int wid))
                                warehouseId = wid;
                            else
                                result.Warnings.Add($"Dòng {rowNumber}: Không tìm thấy kho '{warehouseName}'");
                        }

                        // Create material
                        var material = new Material
                        {
                            Code = code,
                            Name = name,
                            Unit = string.IsNullOrWhiteSpace(unit) ? "Cái" : unit,
                            PurchasePrice = purchasePrice,
                            SupplierId = supplierId,
                            WarehouseId = warehouseId,
                            Description = description
                        };

                        await _context.Materials.AddAsync(material);
                        existingCodes.Add(code.ToLower()); // Prevent duplicates within same import
                        result.SuccessCount++;
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add($"Dòng {rowNumber}: {ex.Message}");
                        result.ErrorCount++;
                    }
                }

                if (result.SuccessCount > 0)
                {
                    await _context.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Lỗi đọc file: {ex.Message}");
                result.ErrorCount++;
            }

            return result;
        }

        public byte[] GenerateMaterialTemplate()
        {
            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Materials");

            // Headers
            var headers = new[] {
                "Mã vật tư (*)",
                "Tên vật tư (*)",
                "Đơn vị",
                "Giá mua",
                "Giá bán",
                "Nhà cung cấp",
                "Kho mặc định",
                "Mô tả"
            };

            for (int i = 0; i < headers.Length; i++)
            {
                var cell = worksheet.Cell(1, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.LightBlue;
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            }

            // Sample data
            worksheet.Cell(2, 1).Value = "VT001";
            worksheet.Cell(2, 2).Value = "Vật tư mẫu 1";
            worksheet.Cell(2, 3).Value = "Cái";
            worksheet.Cell(2, 4).Value = 10000;
            worksheet.Cell(2, 5).Value = 15000;
            worksheet.Cell(2, 6).Value = "Nhà cung cấp A";
            worksheet.Cell(2, 7).Value = "Kho chính";
            worksheet.Cell(2, 8).Value = "Mô tả vật tư";

            worksheet.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }
    }
}


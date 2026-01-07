using ClosedXML.Excel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace MNBEMART.Services
{
    public class ExcelService : IExcelService
    {
        public byte[] ExportToExcel<T>(IEnumerable<T> data, string sheetName = "Sheet1", string[] headers = null)
        {
            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add(sheetName);

            // Get properties
            var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);
            
            // Headers
            int col = 1;
            foreach (var prop in properties)
            {
                var headerName = headers != null && headers.Length >= col 
                    ? headers[col - 1] 
                    : prop.Name;
                    
                worksheet.Cell(1, col).Value = headerName;
                worksheet.Cell(1, col).Style.Font.Bold = true;
                worksheet.Cell(1, col).Style.Fill.BackgroundColor = XLColor.LightGray;
                worksheet.Cell(1, col).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                col++;
            }

            // Data rows
            int row = 2;
            foreach (var item in data)
            {
                col = 1;
                foreach (var prop in properties)
                {
                    var value = prop.GetValue(item);
                    SetCellValue(worksheet.Cell(row, col), value);
                    col++;
                }
                row++;
            }

            // Auto-fit columns
            worksheet.Columns().AdjustToContents();

            // Borders - chỉ tạo range nếu có ít nhất header row
            if (properties.Length > 0 && row > 1)
            {
                var dataRange = worksheet.Range(1, 1, row - 1, properties.Length);
                dataRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                dataRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            }
            else if (properties.Length > 0)
            {
                // Chỉ có header row
                var headerRange = worksheet.Range(1, 1, 1, properties.Length);
                headerRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            }

            // Return as byte array
            using var stream = new System.IO.MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        public byte[] ExportMultipleSheets(Dictionary<string, object> sheets)
        {
            using var workbook = new XLWorkbook();

            foreach (var sheet in sheets)
            {
                var sheetName = sheet.Key;
                var data = sheet.Value;

                // Use reflection to call generic ExportToExcel
                var dataType = data.GetType().GetGenericArguments()[0];
                var method = typeof(ExcelService)
                    .GetMethod("ExportToExcel", BindingFlags.Public | BindingFlags.Instance)
                    .MakeGenericMethod(dataType);

                // Add worksheet manually for multiple sheets scenario
                var worksheet = workbook.Worksheets.Add(sheetName);
                PopulateWorksheet(worksheet, data);
            }

            using var stream = new System.IO.MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        public byte[] ExportWithFormatting<T>(
            IEnumerable<T> data,
            string sheetName,
            Dictionary<string, Func<object, string>> formatters = null,
            bool autoFilter = true,
            bool freezeHeader = true)
        {
            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add(sheetName);

            var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);

            // Headers
            int col = 1;
            foreach (var prop in properties)
            {
                worksheet.Cell(1, col).Value = prop.Name;
                worksheet.Cell(1, col).Style.Font.Bold = true;
                worksheet.Cell(1, col).Style.Fill.BackgroundColor = XLColor.FromHtml("#1e293b");
                worksheet.Cell(1, col).Style.Font.FontColor = XLColor.White;
                worksheet.Cell(1, col).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                worksheet.Cell(1, col).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
                col++;
            }

            // Data rows with formatting
            int row = 2;
            foreach (var item in data)
            {
                col = 1;
                foreach (var prop in properties)
                {
                    var value = prop.GetValue(item);
                    var cell = worksheet.Cell(row, col);

                    // Apply custom formatter if exists
                    if (formatters != null && formatters.ContainsKey(prop.Name))
                    {
                        cell.Value = formatters[prop.Name](value);
                    }
                    else
                    {
                        SetCellValue(cell, value);
                    }

                    // Alternate row colors
                    if (row % 2 == 0)
                    {
                        cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#f8fafc");
                    }

                    cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    col++;
                }
                row++;
            }

            // Auto-fit columns
            worksheet.Columns().AdjustToContents();

            // Auto filter
            if (autoFilter && row > 2)
            {
                worksheet.Range(1, 1, row - 1, properties.Length).SetAutoFilter();
            }

            // Freeze header row
            if (freezeHeader)
            {
                worksheet.SheetView.FreezeRows(1);
            }

            using var stream = new System.IO.MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        private void SetCellValue(IXLCell cell, object value)
        {
            if (value == null)
            {
                cell.Value = "";
                return;
            }

            switch (value)
            {
                case DateTime dt:
                    cell.Value = dt;
                    cell.Style.DateFormat.Format = "dd/MM/yyyy";
                    break;

                case decimal dec:
                    cell.Value = dec;
                    cell.Style.NumberFormat.Format = "#,##0.00";
                    break;

                case double dbl:
                    cell.Value = dbl;
                    cell.Style.NumberFormat.Format = "#,##0.00";
                    break;

                case int integer:
                case long lng:
                    cell.Value = Convert.ToInt64(value);
                    cell.Style.NumberFormat.Format = "#,##0";
                    break;

                case bool boolean:
                    cell.Value = boolean ? "Có" : "Không";
                    break;

                default:
                    cell.Value = value.ToString();
                    break;
            }
        }

        private void PopulateWorksheet(IXLWorksheet worksheet, object data)
        {
            // Get the enumerable type
            var enumerableType = data.GetType();
            var itemType = enumerableType.GetGenericArguments()[0];
            var properties = itemType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            // Headers
            int col = 1;
            foreach (var prop in properties)
            {
                worksheet.Cell(1, col).Value = prop.Name;
                worksheet.Cell(1, col).Style.Font.Bold = true;
                worksheet.Cell(1, col).Style.Fill.BackgroundColor = XLColor.LightGray;
                col++;
            }

            // Data
            int row = 2;
            foreach (var item in (System.Collections.IEnumerable)data)
            {
                col = 1;
                foreach (var prop in properties)
                {
                    var value = prop.GetValue(item);
                    SetCellValue(worksheet.Cell(row, col), value);
                    col++;
                }
                row++;
            }

            worksheet.Columns().AdjustToContents();
        }
    }
}



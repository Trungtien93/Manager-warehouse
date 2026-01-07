using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MNBEMART.Services
{
    public interface IExcelService
    {
        /// <summary>
        /// Export data to Excel with automatic column mapping
        /// </summary>
        byte[] ExportToExcel<T>(IEnumerable<T> data, string sheetName = "Sheet1", string[] headers = null);

        /// <summary>
        /// Export multiple sheets to single Excel file
        /// </summary>
        byte[] ExportMultipleSheets(Dictionary<string, object> sheets);

        /// <summary>
        /// Export with custom styling and formatting
        /// </summary>
        byte[] ExportWithFormatting<T>(
            IEnumerable<T> data, 
            string sheetName,
            Dictionary<string, Func<object, string>> formatters = null,
            bool autoFilter = true,
            bool freezeHeader = true
        );
    }
}



// namespace MNBEMART.Models
// {
//     public class DocumentNumbering
//     {
//         public int Id { get; set; }
//         public string DocumentType { get; set; } = ""; // "StockReceipt","StockIssue","StockTransfer","StockAdjustment"
//         public int? WarehouseId { get; set; }          // đánh số theo kho (tuỳ chọn)
//         public int Year { get; set; }                  // theo năm
//         public string Prefix { get; set; } = "";       // ví dụ "PN", "PX"
//         public int CurrentNo { get; set; }             // số chạy
//         public string? Format { get; set; }            // ví dụ "PN{yyMMdd}-{0000}"
        
//         public Warehouse? Warehouse { get; set; }
//     }
// }
// Models/DocumentNumbering.cs
using System.ComponentModel.DataAnnotations;

namespace MNBEMART.Models
{
    public class DocumentNumbering
    {
        public int Id { get; set; }
        public string DocumentType { get; set; } = "";
        public int? WarehouseId { get; set; }
        public int Year { get; set; }

        public string Prefix { get; set; } = "CT";
        public string? Format { get; set; } = "{Prefix}{yyMMdd}-{No:0000}";
        public int CurrentNo { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    }
}

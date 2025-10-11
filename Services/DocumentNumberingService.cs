// using Microsoft.EntityFrameworkCore;
// using MNBEMART.Data;
// using MNBEMART.Models;

// namespace MNBEMART.Services
// {
//     public class DocumentNumberingService : IDocumentNumberingService
//     {
//         private readonly AppDbContext _db;
//         public DocumentNumberingService(AppDbContext db) => _db = db;

//         public async Task<string> NextAsync(string documentType, int? warehouseId = null)
//         {
//             var y = DateTime.Now.Year;
//             var ent = await _db.DocumentNumberings
//                 .FirstOrDefaultAsync(x => x.DocumentType == documentType && x.WarehouseId == warehouseId && x.Year == y);

//             if (ent == null)
//             {
//                 ent = new DocumentNumbering
//                 {
//                     DocumentType = documentType,
//                     WarehouseId  = warehouseId,
//                     Year         = y,
//                     Prefix       = documentType switch
//                     {
//                         "StockTransfer"   => "CK",
//                         "StockAdjustment" => "DC",
//                         "StockReceipt"    => "PN",
//                         "StockIssue"      => "PX",
//                         _ => "CT"
//                     },
//                     CurrentNo = 0,
//                     Format    = "{Prefix}{yyMMdd}-{No:0000}"
//                 };
//                 _db.DocumentNumberings.Add(ent);
//             }

//             ent.CurrentNo += 1;
//             await _db.SaveChangesAsync();

//             var no = ent.CurrentNo;
//             var today = DateTime.Now;
//             var s = ent.Format?
//                 .Replace("{Prefix}", ent.Prefix)
//                 .Replace("{yyMMdd}", today.ToString("yyMMdd"))
//                 .Replace("{yyyy}", today.ToString("yyyy"))
//                 .Replace("{MM}", today.ToString("MM"))
//                 .Replace("{dd}", today.ToString("dd"))
//                 .Replace("{No:0000}", no.ToString("0000"))
//                 ?? $"{ent.Prefix}{today:yyMMdd}-{no:0000}";

//             return s;
//         }
//     }
// }

// Services/DocumentNumberingService.cs
using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MNBEMART.Data;
using MNBEMART.Models;

namespace MNBEMART.Services
{
    public class DocumentNumberingService : IDocumentNumberingService
    {
        private const int MaxRetries = 3;
        private readonly AppDbContext _db;

        public DocumentNumberingService(AppDbContext db) => _db = db;

        public async Task<string> NextAsync(string documentType, int? warehouseId = null)
        {
            // Dùng "giờ hệ thống" — nếu deploy server UTC, cân nhắc IClock + TZ VN
            var today = DateTime.Now;
            var year  = today.Year;

            for (int attempt = 0; attempt < MaxRetries; attempt++)
            {
                // Transaction ngắn để tránh race giữa đọc-tăng-ghi
                await using var tx = await _db.Database.BeginTransactionAsync();

                var ent = await _db.DocumentNumberings
                    .SingleOrDefaultAsync(x => x.DocumentType == documentType
                                            && x.WarehouseId  == warehouseId
                                            && x.Year         == year);

                if (ent == null)
                {
                    ent = new DocumentNumbering
                    {
                        DocumentType = documentType,
                        WarehouseId  = warehouseId,
                        Year         = year,
                        Prefix       = GetPrefix(documentType),
                        CurrentNo    = 0,
                        Format       = "{Prefix}{yyMMdd}-{No:0000}"
                    };
                    _db.DocumentNumberings.Add(ent);
                    await _db.SaveChangesAsync(); // tạo row trước để có RowVersion cho optimistic concurrency
                }

                ent.CurrentNo += 1;

                try
                {
                    await _db.SaveChangesAsync();

                    // {WH} = tên kho, {WHID} = id kho
                    var whName = warehouseId.HasValue
                        ? await _db.Warehouses.Where(w => w.Id == warehouseId.Value)
                                              .Select(w => w.Name)
                                              .FirstOrDefaultAsync()
                        : null;

                    var number = ApplyFormat(ent, today, whName, warehouseId);
                    await tx.CommitAsync();
                    return number;
                }
                catch (DbUpdateConcurrencyException)
                {
                    // có thread khác vừa tăng — rollback và retry
                    await tx.RollbackAsync();
                }
                catch (DbUpdateException)
                {
                    // phòng khi unique index tranh chấp trong lúc insert bản ghi cấu hình
                    await tx.RollbackAsync();
                }
            }

            throw new InvalidOperationException("Không cấp được số chứng từ do cạnh tranh đồng thời.");
        }

        public async Task<string> PeekAsync(string documentType, int? warehouseId = null)
        {
            var today = DateTime.Now;
            var year  = today.Year;

            var ent = await _db.DocumentNumberings
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.DocumentType == documentType
                                        && x.WarehouseId  == warehouseId
                                        && x.Year         == year);

            if (ent == null)
            {
                ent = new DocumentNumbering
                {
                    DocumentType = documentType,
                    WarehouseId  = warehouseId,
                    Year         = year,
                    Prefix       = GetPrefix(documentType),
                    CurrentNo    = 0,
                    Format       = "{Prefix}{yyMMdd}-{No:0000}"
                };
            }

            // Tạm tính số kế tiếp (không ghi DB)
            var seq = ent.CurrentNo + 1;

            var whName = warehouseId.HasValue
                ? await _db.Warehouses.Where(w => w.Id == warehouseId.Value)
                                      .Select(w => w.Name)
                                      .FirstOrDefaultAsync()
                : null;

            return ApplyFormat(ent, today, whName, warehouseId, seq);
        }

        private static string GetPrefix(string documentType) => documentType switch
        {
            "StockReceipt"    => "PN",
            "StockIssue"      => "PX",
            "StockTransfer"   => "CK",
            "StockAdjustment" => "DC",
            _                 => "CT"
        };

        /// <summary>
        /// Thay token trong Format. Hỗ trợ:
        /// {Prefix}, {yyyy}, {yy}, {MM}, {dd}, {yyMM}, {yyMMdd}, {WH} (tên kho), {WHID} (id kho),
        /// {No:0000} (độ dài tuỳ theo số 0).
        /// </summary>
        private static string ApplyFormat(
            DocumentNumbering ent,
            DateTime date,
            string? warehouseName,
            int? warehouseId,
            int? seqOverride = null)
        {
            var seq = seqOverride ?? ent.CurrentNo;
            var fmt = string.IsNullOrWhiteSpace(ent.Format)
                ? "{Prefix}{yyMMdd}-{No:0000}"
                : ent.Format!;

            var result = fmt
                .Replace("{Prefix}", ent.Prefix ?? "")
                .Replace("{yyyy}",  date.ToString("yyyy"))
                .Replace("{yy}",    date.ToString("yy"))
                .Replace("{MM}",    date.ToString("MM"))
                .Replace("{dd}",    date.ToString("dd"))
                .Replace("{yyMM}",  date.ToString("yyMM"))
                .Replace("{yyMMdd}",date.ToString("yyMMdd"))
                .Replace("{WH}",    warehouseName ?? "")
                .Replace("{WHID}",  warehouseId?.ToString() ?? "");

            // Bắt mọi pattern {No:000}, {No:0000}, {No:000000}, ...
            result = Regex.Replace(result, @"\{No:(0+)\}", m =>
            {
                var pad = m.Groups[1].Value; // ví dụ "0000"
                return seq.ToString(new string('0', pad.Length));
            });

            return result;
        }
    }
}

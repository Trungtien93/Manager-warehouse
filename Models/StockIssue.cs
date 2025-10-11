// using System.ComponentModel.DataAnnotations.Schema;

// namespace MNBEMART.Models
// {
//     using System.Collections.Generic;

//     // Represents a stock issue in the system
//     public class StockIssue
//     {
//         public int Id { get; set; }
//         public string IssueNumber { get; set; }
//         public DateTime CreatedAt { get; set; }
//         public int CreatedById { get; set; }
//         public User CreatedBy { get; set; }
//         public int WarehouseId { get; set; }
//         [ForeignKey("WarehouseId")]
//         public Warehouse Warehouse { get; set; }
//         public string ReceivedByName { get; set; }
//         public string ReferenceDocumentNumber { get; set; }
//         public DateTime? ReferenceDate { get; set; }
//         public string AttachedDocuments { get; set; }
//         // public string Status { get; set; }
//         public DocumentStatus Status { get; set; }  // thay string Status
//         // Thông tin duyệt
//         public int? ApprovedById { get; set; }
//         public User ApprovedBy { get; set; }
//         public DateTime? ApprovedAt { get; set; }
//         public string? Note { get; set; } // ghi chú (tuỳ chọn)

//         public ICollection<StockIssueDetail> Details { get; set; }

       
//     }
// }

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MNBEMART.Models
{
    public class StockIssue
    {
        public int Id { get; set; }

        [Required, StringLength(64)]
        public string IssueNumber { get; set; } = default!;   // số phiếu (bắt buộc)

        public DateTime CreatedAt { get; set; }               // set khi tạo
        public int CreatedById { get; set; }
        public User CreatedBy { get; set; } = default!;

        public int WarehouseId { get; set; }
        public Warehouse Warehouse { get; set; } = default!;  // [ForeignKey] không cần, convention đủ

        // Thông tin người nhận (tuỳ chọn)
        [StringLength(128)]
        public string? ReceivedByName { get; set; }

        // CT gốc (tuỳ chọn)
        [StringLength(64)]
        public string? ReferenceDocumentNumber { get; set; }
        public DateTime? ReferenceDate { get; set; }

        // File đính kèm (tuỳ chọn)
        public string? AttachedDocuments { get; set; }

        // Trạng thái
        [Required]
        public DocumentStatus Status { get; set; }

        // Duyệt (tuỳ chọn)
        public int? ApprovedById { get; set; }
        public User? ApprovedBy { get; set; }
        public DateTime? ApprovedAt { get; set; }

        // Ghi chú (TUỲ CHỌN) → phải cho phép null để tránh lỗi SQL lúc insert
        public string? Note { get; set; }

        // Chi tiết
        public ICollection<StockIssueDetail> Details { get; set; } = new List<StockIssueDetail>();
    }
}

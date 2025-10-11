using System;

namespace MNBEMART.Models
{
    public class StockBalance
    {
        public int Id { get; set; }

        public int WarehouseId { get; set; }
        public int MaterialId  { get; set; }

        // ✅ ĐỂ DateTime (khỏi converter), vẫn lưu cột kiểu 'date'
        public DateTime Date { get; set; }
        // Ghi nhận phát sinh trong NGÀY (không lưu Opening/Closing để khỏi phải propagate)
        public decimal InQty    { get; set; }
        public decimal OutQty   { get; set; }
        public decimal InValue  { get; set; }
        public decimal OutValue { get; set; }

        public DateTime UpdatedAt { get; set; }

        public Warehouse Warehouse { get; set; }
        public Material  Material  { get; set; }
    }
}

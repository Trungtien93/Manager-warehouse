using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace MNBEMART.Models
{
    public class DemandForecast
    {
        public int Id { get; set; }

        [Required]
        public int MaterialId { get; set; }
        public Material? Material { get; set; }

        [Required]
        public int WarehouseId { get; set; }
        public Warehouse? Warehouse { get; set; }

        [Required]
        public DateTime ForecastDate { get; set; } // Ngày dự đoán

        [Required]
        [Precision(18, 3)]
        public decimal ForecastedQuantity { get; set; } // Số lượng dự đoán

        [Precision(5, 2)]
        public decimal ConfidenceLevel { get; set; } // Mức độ tin cậy (0-100)

        [StringLength(50)]
        public string Method { get; set; } = "MovingAverage"; // Phương pháp: MovingAverage, ExponentialSmoothing, LinearRegression

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        // Thông tin bổ sung
        [Precision(18, 3)]
        public decimal? HistoricalAverage { get; set; } // Trung bình lịch sử

        [Precision(18, 3)]
        public decimal? Trend { get; set; } // Xu hướng (tăng/giảm)

        [StringLength(500)]
        public string? Notes { get; set; } // Ghi chú
    }
}


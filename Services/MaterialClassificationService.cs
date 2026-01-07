using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MNBEMART.Data;
using MNBEMART.Models;

namespace MNBEMART.Services
{
    public interface IMaterialClassificationService
    {
        Task<ClassificationResult> ClassifyAsync(int materialId);
        Task<ClassificationResult> ClassifyFromTextAsync(string name, string? description = null);
    }

    public class ClassificationResult
    {
        public string? Category { get; set; }
        public List<string> Tags { get; set; } = new();
        public string? SuggestedNormalizedName { get; set; }
        public decimal Confidence { get; set; }
        public string? Notes { get; set; }
    }

    public class MaterialClassificationService : IMaterialClassificationService
    {
        private readonly AppDbContext _db;
        private readonly ILogger<MaterialClassificationService> _logger;

        // Danh mục phổ biến trong quản lý kho
        private readonly Dictionary<string, List<string>> _categoryKeywords = new()
        {
            ["Thực phẩm"] = new List<string> { "thực phẩm", "đồ ăn", "thức ăn", "món ăn", "nguyên liệu nấu", "gia vị", "bột", "đường", "muối" },
            ["Đồ uống"] = new List<string> { "nước", "đồ uống", "nước giải khát", "sữa", "cà phê", "trà", "nước ép" },
            ["Vật liệu xây dựng"] = new List<string> { "xi măng", "gạch", "cát", "sắt", "thép", "gỗ", "tôn", "ngói" },
            ["Hóa chất"] = new List<string> { "hóa chất", "axit", "bazơ", "dung môi", "chất tẩy", "xà phòng" },
            ["Bao bì"] = new List<string> { "túi", "hộp", "chai", "lọ", "thùng", "bao bì", "giấy gói" },
            ["Dụng cụ"] = new List<string> { "dụng cụ", "thiết bị", "máy", "công cụ", "vật dụng" },
            ["Văn phòng phẩm"] = new List<string> { "giấy", "bút", "mực", "văn phòng", "văn phòng phẩm" },
            ["Điện tử"] = new List<string> { "điện", "điện tử", "pin", "battery", "cáp", "dây điện" }
        };

        public MaterialClassificationService(AppDbContext db, ILogger<MaterialClassificationService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<ClassificationResult> ClassifyAsync(int materialId)
        {
            var material = await _db.Materials.FindAsync(materialId);
            if (material == null)
            {
                return new ClassificationResult
                {
                    Notes = "Không tìm thấy nguyên liệu"
                };
            }

            return await ClassifyFromTextAsync(material.Name, material.Description);
        }

        public async Task<ClassificationResult> ClassifyFromTextAsync(string name, string? description = null)
        {
            var text = $"{name} {description}".ToLowerInvariant();
            var result = new ClassificationResult();

            // Phân loại category
            var categoryScores = new Dictionary<string, int>();
            foreach (var category in _categoryKeywords)
            {
                var score = category.Value.Count(keyword => text.Contains(keyword));
                if (score > 0)
                {
                    categoryScores[category.Key] = score;
                }
            }

            if (categoryScores.Any())
            {
                result.Category = categoryScores.OrderByDescending(x => x.Value).First().Key;
                result.Confidence = Math.Min(0.9m, (decimal)categoryScores.Values.Max() / 5);
            }
            else
            {
                result.Category = "Khác";
                result.Confidence = 0.3m;
            }

            // Tạo tags từ keywords tìm thấy
            var tags = new HashSet<string>();
            foreach (var category in _categoryKeywords)
            {
                foreach (var keyword in category.Value)
                {
                    if (text.Contains(keyword))
                    {
                        tags.Add(keyword);
                    }
                }
            }

            // Thêm tags từ tên (các từ đơn)
            var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var word in words)
            {
                if (word.Length > 3) // Bỏ qua từ quá ngắn
                {
                    tags.Add(word.ToLowerInvariant());
                }
            }

            result.Tags = tags.Take(10).ToList(); // Giới hạn 10 tags

            // Gợi ý tên chuẩn hóa (loại bỏ ký tự đặc biệt, viết hoa chữ cái đầu)
            result.SuggestedNormalizedName = NormalizeName(name);

            result.Notes = $"Phân loại dựa trên từ khóa tìm thấy trong tên và mô tả";

            return result;
        }

        private string NormalizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return name;

            // Loại bỏ ký tự đặc biệt thừa
            var normalized = name.Trim();
            
            // Viết hoa chữ cái đầu mỗi từ
            var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < words.Length; i++)
            {
                if (words[i].Length > 0)
                {
                    words[i] = char.ToUpper(words[i][0]) + words[i].Substring(1).ToLowerInvariant();
                }
            }

            return string.Join(" ", words);
        }
    }
}






























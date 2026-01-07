using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MNBEMART.Services
{
    public interface IImageAnalysisService
    {
        Task<ImageAnalysisResult> AnalyzeImageAsync(byte[] imageBytes, string mimeType);
        Task<ImageAnalysisResult> AnalyzeImageFromUrlAsync(string imageUrl);
    }

    public class ImageAnalysisResult
    {
        public string SuggestedName { get; set; } = "";
        public string SuggestedDescription { get; set; } = "";
        public string? Category { get; set; }
        public List<string> Tags { get; set; } = new();
        public decimal Confidence { get; set; }
        public string? Notes { get; set; }
        public string? Unit { get; set; }
        public string? Specification { get; set; }
        public decimal? SuggestedPurchasePrice { get; set; }
        public decimal? SuggestedSellingPrice { get; set; }
    }

    public class ImageAnalysisService : IImageAnalysisService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ImageAnalysisService> _logger;

        public ImageAnalysisService(
            HttpClient httpClient,
            IConfiguration configuration,
            ILogger<ImageAnalysisService> logger)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<ImageAnalysisResult> AnalyzeImageAsync(byte[] imageBytes, string mimeType)
        {
            var apiKey = _configuration["Gemini:ApiKey"];
            var model = _configuration["Gemini:Model"] ?? "gemini-2.0-flash-exp";

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException("Gemini API key is not configured");
            }

            var modelPath = model.StartsWith("models/", StringComparison.OrdinalIgnoreCase)
                ? model
                : $"models/{model}";

            var url = $"https://generativelanguage.googleapis.com/v1/{modelPath}:generateContent?key={apiKey}";

            // Convert image to base64
            var base64Image = Convert.ToBase64String(imageBytes);

            var prompt = "Phân tích hình ảnh này và đưa ra:\n" +
                        "1. Tên gợi ý cho nguyên liệu/sản phẩm (tiếng Việt)\n" +
                        "2. Mô tả ngắn gọn (2-3 câu)\n" +
                        "3. Danh mục/category phù hợp\n" +
                        "4. Các tags/keywords liên quan\n" +
                        "5. Đơn vị tính (Unit) - CHỈ là đơn vị đo lường: kg, g, gram, l, lít, ml, milliliter, m, mét, cm, quả, cái, viên... KHÔNG phải cách đóng gói\n" +
                        "6. Quy cách (Specification) - CHỈ là cách đóng gói: gói, thùng, chai, hộp, bịch, túi, lọ, vỉ, khay, set, pallet, thanh, bao, bình, bó, bộ, cái, can, lon... KHÔNG phải đơn vị đo lường\n" +
                        "7. Giá nhập đề xuất (SuggestedPurchasePrice) - BẮT BUỘC phải có, ước tính giá nhập dựa trên sản phẩm tương tự, trả về số nguyên (ví dụ: 50000, không có dấu phẩy, dấu chấm)\n" +
                        "8. Giá bán đề xuất (SuggestedSellingPrice) - BẮT BUỘC phải có, ước tính giá bán, thường cao hơn giá nhập 10-30%, trả về số nguyên (ví dụ: 60000, không có dấu phẩy, dấu chấm)\n\n" +
                        "LƯU Ý QUAN TRỌNG:\n" +
                        "- Đơn vị (Unit) và Quy cách (Specification) là KHÁC NHAU:\n" +
                        "  + Đơn vị: kg, g, l, ml, quả, cái... (đơn vị đo lường)\n" +
                        "  + Quy cách: gói, thùng, chai, hộp... (cách đóng gói)\n" +
                        "- Giá nhập và Giá bán PHẢI là số nguyên, KHÔNG được null\n\n" +
                        "Trả về dưới dạng JSON với format:\n" +
                        "{\n" +
                        "  \"suggestedName\": \"tên gợi ý\",\n" +
                        "  \"suggestedDescription\": \"mô tả\",\n" +
                        "  \"category\": \"danh mục\",\n" +
                        "  \"tags\": [\"tag1\", \"tag2\"],\n" +
                        "  \"confidence\": 0.85,\n" +
                        "  \"unit\": \"g\",\n" +
                        "  \"specification\": \"hộp\",\n" +
                        "  \"suggestedPurchasePrice\": 50000,\n" +
                        "  \"suggestedSellingPrice\": 60000\n" +
                        "}";

            var requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new object[]
                        {
                            new { text = prompt },
                            new
                            {
                                inline_data = new
                                {
                                    mime_type = mimeType,
                                    data = base64Image
                                }
                            }
                        }
                    }
                }
            };

            var json = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };

                using var response = await _httpClient.SendAsync(request);
                var body = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Gemini Vision API error: {Status} - {Body}", response.StatusCode, body);
                    throw new Exception($"API error: {response.StatusCode}");
                }

                var data = JsonSerializer.Deserialize<JsonElement>(body);
                var text = data.GetProperty("candidates")[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text")
                    .GetString();

                if (string.IsNullOrWhiteSpace(text))
                {
                    return new ImageAnalysisResult
                    {
                        Notes = "Không thể phân tích hình ảnh"
                    };
                }

                // Parse JSON từ response
                try
                {
                    var result = JsonSerializer.Deserialize<ImageAnalysisResult>(text, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    // Nếu parse thành công nhưng thiếu một số trường, thử extract từ text
                    if (result != null)
                    {
                        // Làm sạch mô tả - loại bỏ JSON metadata nếu có
                        if (!string.IsNullOrWhiteSpace(result.SuggestedDescription))
                        {
                            result.SuggestedDescription = CleanDescriptionFromMetadata(result.SuggestedDescription);
                        }
                        
                        // Đảm bảo các trường được extract nếu chưa có
                        if (string.IsNullOrWhiteSpace(result.Unit))
                            result.Unit = ExtractUnitFromText(text);
                        if (string.IsNullOrWhiteSpace(result.Specification))
                            result.Specification = ExtractSpecificationFromText(text);
                        if (!result.SuggestedPurchasePrice.HasValue)
                            result.SuggestedPurchasePrice = ExtractPriceFromText(text, "purchasePrice", "giá nhập");
                        if (!result.SuggestedSellingPrice.HasValue)
                            result.SuggestedSellingPrice = ExtractPriceFromText(text, "sellingPrice", "giá bán");
                        
                        return result;
                    }
                    
                    return new ImageAnalysisResult
                    {
                        SuggestedName = ExtractNameFromText(text),
                        SuggestedDescription = ExtractDescriptionFromText(text),
                        Unit = ExtractUnitFromText(text),
                        Specification = ExtractSpecificationFromText(text),
                        SuggestedPurchasePrice = ExtractPriceFromText(text, "purchasePrice", "giá nhập"),
                        SuggestedSellingPrice = ExtractPriceFromText(text, "sellingPrice", "giá bán"),
                        Notes = "Phân tích thành công nhưng không parse được JSON, đã trích xuất thông tin cơ bản"
                    };
                }
                catch
                {
                    // Nếu không parse được JSON, thử extract thông tin từ text
                    return new ImageAnalysisResult
                    {
                        SuggestedName = ExtractNameFromText(text),
                        SuggestedDescription = ExtractDescriptionFromText(text),
                        Unit = ExtractUnitFromText(text),
                        Specification = ExtractSpecificationFromText(text),
                        SuggestedPurchasePrice = ExtractPriceFromText(text, "purchasePrice", "giá nhập"),
                        SuggestedSellingPrice = ExtractPriceFromText(text, "sellingPrice", "giá bán"),
                        Notes = "Đã phân tích nhưng format không chuẩn"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing image with Gemini Vision API");
                throw;
            }
        }

        public async Task<ImageAnalysisResult> AnalyzeImageFromUrlAsync(string imageUrl)
        {
            try
            {
                var imageBytes = await _httpClient.GetByteArrayAsync(imageUrl);
                var mimeType = "image/jpeg"; // Default, có thể detect từ URL hoặc response headers
                return await AnalyzeImageAsync(imageBytes, mimeType);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading image from URL: {Url}", imageUrl);
                throw;
            }
        }

        private string ExtractNameFromText(string text)
        {
            // Tìm "suggestedName" hoặc "tên" trong text
            var lines = text.Split('\n');
            foreach (var line in lines)
            {
                if (line.Contains("tên", StringComparison.OrdinalIgnoreCase) || 
                    line.Contains("name", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = line.Split(':');
                    if (parts.Length > 1)
                    {
                        return parts[1].Trim().Trim('"', '\'', ',');
                    }
                }
            }
            return "Nguyên liệu";
        }

        private string ExtractDescriptionFromText(string text)
        {
            // Tìm mô tả trong text - dừng khi gặp JSON metadata
            var lines = text.Split('\n');
            var description = new StringBuilder();
            bool inDescription = false;
            
            // Các pattern báo hiệu bắt đầu JSON metadata
            var metadataPatterns = new[]
            {
                "\"category\"", "\"tags\"", "\"confidence\"", "\"unit\"", "\"specification\"",
                "\"suggestedPurchasePrice\"", "\"suggestedSellingPrice\"",
                "category", "tags", "confidence", "unit", "specification",
                "suggestedPurchasePrice", "suggestedSellingPrice"
            };

            foreach (var line in lines)
            {
                // Kiểm tra nếu dòng này chứa metadata pattern - dừng lại
                bool hasMetadata = metadataPatterns.Any(pattern => 
                    line.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0);
                
                if (hasMetadata && inDescription)
                {
                    // Đã gặp metadata, dừng lại
                    break;
                }
                
                if (line.Contains("mô tả", StringComparison.OrdinalIgnoreCase) || 
                    line.Contains("description", StringComparison.OrdinalIgnoreCase))
                {
                    inDescription = true;
                    var parts = line.Split(':');
                    if (parts.Length > 1)
                    {
                        var descPart = parts[1].Trim().Trim('"', '\'');
                        // Kiểm tra xem phần này có chứa metadata không
                        if (!metadataPatterns.Any(p => descPart.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            description.Append(descPart);
                        }
                        else
                        {
                            // Nếu chứa metadata, chỉ lấy phần trước metadata
                            var metadataIndex = metadataPatterns
                                .Select(p => descPart.IndexOf(p, StringComparison.OrdinalIgnoreCase))
                                .Where(idx => idx >= 0)
                                .DefaultIfEmpty(-1)
                                .Min();
                            
                            if (metadataIndex > 0)
                            {
                                description.Append(descPart.Substring(0, metadataIndex).Trim());
                            }
                            break;
                        }
                    }
                }
                else if (inDescription && !string.IsNullOrWhiteSpace(line))
                {
                    // Kiểm tra xem dòng này có chứa metadata không
                    if (metadataPatterns.Any(p => line.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        break;
                    }
                    
                    var cleanLine = line.Trim().Trim('"', '\'');
                    // Loại bỏ các ký tự JSON còn sót
                    if (!cleanLine.StartsWith("{") && !cleanLine.StartsWith("}") && 
                        !cleanLine.StartsWith("[") && !cleanLine.StartsWith("]"))
                    {
                        description.Append(" ").Append(cleanLine);
                    }
                }
            }

            var result = description.Length > 0 ? description.ToString() : "Nguyên liệu được phân tích từ hình ảnh";
            // Làm sạch thêm lần nữa để đảm bảo
            return CleanDescriptionFromMetadata(result);
        }
        
        /// <summary>
        /// Loại bỏ JSON metadata khỏi mô tả (category, tags, confidence, unit, specification, prices)
        /// </summary>
        private string CleanDescriptionFromMetadata(string description)
        {
            if (string.IsNullOrWhiteSpace(description))
                return description;
            
            var clean = description;
            
            // Loại bỏ các pattern JSON metadata phổ biến
            // Pattern 1: ." , "category": ... hoặc .", "category": ...
            clean = System.Text.RegularExpressions.Regex.Replace(clean, 
                @"\.\s*""\s*,\s*""category""\s*:.*$", "", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            clean = System.Text.RegularExpressions.Regex.Replace(clean, 
                @"\.\s*"",\s*""category""\s*:.*$", "", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
            // Pattern 2: ", "category": ... hoặc , "category": ...
            clean = System.Text.RegularExpressions.Regex.Replace(clean, 
                @"""\s*,\s*""category""\s*:.*$", "", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            clean = System.Text.RegularExpressions.Regex.Replace(clean, 
                @",\s*""category""\s*:.*$", "", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
            // Loại bỏ tags
            clean = System.Text.RegularExpressions.Regex.Replace(clean, 
                @"\.\s*""\s*,\s*""tags""\s*:.*$", "", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            clean = System.Text.RegularExpressions.Regex.Replace(clean, 
                @"\.\s*"",\s*""tags""\s*:.*$", "", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            clean = System.Text.RegularExpressions.Regex.Replace(clean, 
                @"""\s*,\s*""tags""\s*:.*$", "", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            clean = System.Text.RegularExpressions.Regex.Replace(clean, 
                @",\s*""tags""\s*:.*$", "", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
            // Loại bỏ confidence
            clean = System.Text.RegularExpressions.Regex.Replace(clean, 
                @"\.\s*""\s*,\s*""confidence""\s*:.*$", "", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            clean = System.Text.RegularExpressions.Regex.Replace(clean, 
                @"""\s*,\s*""confidence""\s*:.*$", "", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            clean = System.Text.RegularExpressions.Regex.Replace(clean, 
                @",\s*""confidence""\s*:.*$", "", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
            // Loại bỏ unit
            clean = System.Text.RegularExpressions.Regex.Replace(clean, 
                @"\.\s*""\s*,\s*""unit""\s*:.*$", "", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            clean = System.Text.RegularExpressions.Regex.Replace(clean, 
                @"""\s*,\s*""unit""\s*:.*$", "", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            clean = System.Text.RegularExpressions.Regex.Replace(clean, 
                @",\s*""unit""\s*:.*$", "", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
            // Loại bỏ specification
            clean = System.Text.RegularExpressions.Regex.Replace(clean, 
                @"\.\s*""\s*,\s*""specification""\s*:.*$", "", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            clean = System.Text.RegularExpressions.Regex.Replace(clean, 
                @"""\s*,\s*""specification""\s*:.*$", "", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            clean = System.Text.RegularExpressions.Regex.Replace(clean, 
                @",\s*""specification""\s*:.*$", "", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
            // Loại bỏ suggestedPurchasePrice
            clean = System.Text.RegularExpressions.Regex.Replace(clean, 
                @"\.\s*""\s*,\s*""suggestedPurchasePrice""\s*:.*$", "", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            clean = System.Text.RegularExpressions.Regex.Replace(clean, 
                @"""\s*,\s*""suggestedPurchasePrice""\s*:.*$", "", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            clean = System.Text.RegularExpressions.Regex.Replace(clean, 
                @",\s*""suggestedPurchasePrice""\s*:.*$", "", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
            // Loại bỏ suggestedSellingPrice
            clean = System.Text.RegularExpressions.Regex.Replace(clean, 
                @"\.\s*""\s*,\s*""suggestedSellingPrice""\s*:.*$", "", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            clean = System.Text.RegularExpressions.Regex.Replace(clean, 
                @"""\s*,\s*""suggestedSellingPrice""\s*:.*$", "", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            clean = System.Text.RegularExpressions.Regex.Replace(clean, 
                @",\s*""suggestedSellingPrice""\s*:.*$", "", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
            // Loại bỏ JSON object patterns ở cuối
            clean = System.Text.RegularExpressions.Regex.Replace(clean, 
                @"\.\s*""\s*,\s*\{.*\}$", "", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            clean = System.Text.RegularExpressions.Regex.Replace(clean, 
                @"""\s*,\s*\{.*\}$", "", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
            // Loại bỏ các ký tự JSON còn sót ở cuối
            clean = System.Text.RegularExpressions.Regex.Replace(clean, 
                @"\s*[,\s]*\}\s*$", "", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            clean = System.Text.RegularExpressions.Regex.Replace(clean, 
                @"\s*[,\s]*\]\s*$", "", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            clean = System.Text.RegularExpressions.Regex.Replace(clean, 
                @"[,"":\s]+$", "", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
            return clean.Trim();
        }

        private string? ExtractUnitFromText(string text)
        {
            // Tìm "unit" trong text (có thể là "unit": "kg" hoặc "đơn vị": "kg")
            var patterns = new[] { "\"unit\"", "'unit'", "\"đơn vị\"", "'đơn vị'", "unit:", "đơn vị:" };
            foreach (var pattern in patterns)
            {
                var idx = text.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    var start = idx + pattern.Length;
                    var remaining = text.Substring(start).TrimStart(' ', ':', '"', '\'');
                    var end = remaining.IndexOfAny(new[] { '"', '\'', ',', '\n', '\r', '}' });
                    if (end > 0)
                    {
                        var unit = remaining.Substring(0, end).Trim('"', '\'', ' ', ',');
                        if (!string.IsNullOrWhiteSpace(unit))
                            return unit;
                    }
                }
            }
            return null;
        }

        private string? ExtractSpecificationFromText(string text)
        {
            // Tìm "specification" trong text
            var patterns = new[] { "\"specification\"", "'specification'", "\"quy cách\"", "'quy cách'", "specification:", "quy cách:" };
            foreach (var pattern in patterns)
            {
                var idx = text.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    var start = idx + pattern.Length;
                    var remaining = text.Substring(start).TrimStart(' ', ':', '"', '\'');
                    var end = remaining.IndexOfAny(new[] { '"', '\'', ',', '\n', '\r', '}' });
                    if (end > 0)
                    {
                        var spec = remaining.Substring(0, end).Trim('"', '\'', ' ', ',');
                        if (!string.IsNullOrWhiteSpace(spec))
                            return spec;
                    }
                }
            }
            return null;
        }

        private decimal? ExtractPriceFromText(string text, string jsonKey, string vietnameseKey)
        {
            // Tìm giá trong text (có thể là "suggestedPurchasePrice": 20000 hoặc "giá nhập": 20000)
            var patterns = new[] 
            { 
                $"\"{jsonKey}\"", 
                $"'{jsonKey}'", 
                $"\"{vietnameseKey}\"", 
                $"'{vietnameseKey}'",
                $"{jsonKey}:",
                $"{vietnameseKey}:",
                $"purchasePrice", // Tìm cả camelCase
                $"sellingPrice"
            };
            
            foreach (var pattern in patterns)
            {
                var idx = text.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    var start = idx + pattern.Length;
                    var remaining = text.Substring(start).TrimStart(' ', ':', '"', '\'');
                    
                    // Tìm số đầu tiên sau pattern
                    var numberStart = -1;
                    var numberEnd = -1;
                    for (int i = 0; i < remaining.Length; i++)
                    {
                        var c = remaining[i];
                        if (char.IsDigit(c))
                        {
                            if (numberStart == -1) numberStart = i;
                            numberEnd = i;
                        }
                        else if (numberStart >= 0)
                        {
                            // Đã tìm thấy số, dừng lại khi gặp ký tự không phải số
                            break;
                        }
                        else if (c == '"' || c == '\'' || c == ',' || c == '\n' || c == '\r' || c == '}')
                        {
                            // Không có số trước ký tự này
                            break;
                        }
                    }
                    
                    if (numberStart >= 0 && numberEnd >= numberStart)
                    {
                        var priceStr = remaining.Substring(numberStart, numberEnd - numberStart + 1);
                        
                        // Loại bỏ dấu phẩy, chấm (format số)
                        priceStr = priceStr.Replace(",", "").Replace(".", "");
                        
                        if (decimal.TryParse(priceStr, out decimal price) && price > 0)
                        {
                            return price;
                        }
                    }
                }
            }
            
            // Nếu không tìm thấy bằng pattern, thử tìm số lớn trong text (có thể là giá)
            // Tìm các số có 4-7 chữ số (giá thường từ 1000 đến 9999999)
            var numberMatches = System.Text.RegularExpressions.Regex.Matches(text, @"\b\d{4,7}\b");
            foreach (System.Text.RegularExpressions.Match match in numberMatches)
            {
                if (decimal.TryParse(match.Value, out decimal price) && price >= 1000 && price <= 99999999)
                {
                    // Kiểm tra xem số này có gần với từ khóa giá không
                    var contextStart = Math.Max(0, match.Index - 50);
                    var contextEnd = Math.Min(text.Length, match.Index + match.Length + 50);
                    var context = text.Substring(contextStart, contextEnd - contextStart).ToLower();
                    
                    if (context.Contains("giá") || context.Contains("price") || context.Contains("purchase") || context.Contains("selling"))
                    {
                        return price;
                    }
                }
            }
            
            return null;
        }
    }
}



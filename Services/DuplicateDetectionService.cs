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
    public interface IDuplicateDetectionService
    {
        Task<List<DuplicateSuggestion>> DetectDuplicatesAsync(int materialId);
        Task<List<DuplicateSuggestion>> DetectAllDuplicatesAsync(decimal similarityThreshold = 0.7m);
        Task<decimal> CalculateSimilarityAsync(int materialId1, int materialId2);
    }

    public class DuplicateSuggestion
    {
        public int MaterialId1 { get; set; }
        public string MaterialCode1 { get; set; } = "";
        public string MaterialName1 { get; set; } = "";
        public int MaterialId2 { get; set; }
        public string MaterialCode2 { get; set; } = "";
        public string MaterialName2 { get; set; } = "";
        public decimal SimilarityScore { get; set; }
        public string Reason { get; set; } = "";
    }

    public class DuplicateDetectionService : IDuplicateDetectionService
    {
        private readonly AppDbContext _db;
        private readonly ILogger<DuplicateDetectionService> _logger;

        public DuplicateDetectionService(AppDbContext db, ILogger<DuplicateDetectionService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<List<DuplicateSuggestion>> DetectDuplicatesAsync(int materialId)
        {
            var material = await _db.Materials.FindAsync(materialId);
            if (material == null)
            {
                return new List<DuplicateSuggestion>();
            }

            var allMaterials = await _db.Materials
                .Where(m => m.Id != materialId)
                .ToListAsync();

            var suggestions = new List<DuplicateSuggestion>();

            foreach (var other in allMaterials)
            {
                var similarity = CalculateSimilarity(material, other);
                if (similarity >= 0.7m)
                {
                    suggestions.Add(new DuplicateSuggestion
                    {
                        MaterialId1 = materialId,
                        MaterialCode1 = material.Code,
                        MaterialName1 = material.Name,
                        MaterialId2 = other.Id,
                        MaterialCode2 = other.Code,
                        MaterialName2 = other.Name,
                        SimilarityScore = similarity,
                        Reason = GenerateReason(material, other, similarity)
                    });
                }
            }

            return suggestions.OrderByDescending(s => s.SimilarityScore).ToList();
        }

        public async Task<List<DuplicateSuggestion>> DetectAllDuplicatesAsync(decimal similarityThreshold = 0.7m)
        {
            var materials = await _db.Materials.ToListAsync();
            var suggestions = new List<DuplicateSuggestion>();
            var processed = new HashSet<string>();

            for (int i = 0; i < materials.Count; i++)
            {
                for (int j = i + 1; j < materials.Count; j++)
                {
                    var key = $"{Math.Min(materials[i].Id, materials[j].Id)}_{Math.Max(materials[i].Id, materials[j].Id)}";
                    if (processed.Contains(key)) continue;
                    processed.Add(key);

                    var similarity = CalculateSimilarity(materials[i], materials[j]);
                    if (similarity >= similarityThreshold)
                    {
                        suggestions.Add(new DuplicateSuggestion
                        {
                            MaterialId1 = materials[i].Id,
                            MaterialCode1 = materials[i].Code,
                            MaterialName1 = materials[i].Name,
                            MaterialId2 = materials[j].Id,
                            MaterialCode2 = materials[j].Code,
                            MaterialName2 = materials[j].Name,
                            SimilarityScore = similarity,
                            Reason = GenerateReason(materials[i], materials[j], similarity)
                        });
                    }
                }
            }

            return suggestions.OrderByDescending(s => s.SimilarityScore).ToList();
        }

        public async Task<decimal> CalculateSimilarityAsync(int materialId1, int materialId2)
        {
            var material1 = await _db.Materials.FindAsync(materialId1);
            var material2 = await _db.Materials.FindAsync(materialId2);

            if (material1 == null || material2 == null)
            {
                return 0;
            }

            return CalculateSimilarity(material1, material2);
        }

        private decimal CalculateSimilarity(Material m1, Material m2)
        {
            decimal score = 0;
            decimal weight = 0;

            // So sánh Code (40% trọng số)
            if (!string.IsNullOrWhiteSpace(m1.Code) && !string.IsNullOrWhiteSpace(m2.Code))
            {
                var codeSim = CalculateStringSimilarity(m1.Code.ToLowerInvariant(), m2.Code.ToLowerInvariant());
                score += codeSim * 0.4m;
                weight += 0.4m;
            }

            // So sánh Name (50% trọng số)
            if (!string.IsNullOrWhiteSpace(m1.Name) && !string.IsNullOrWhiteSpace(m2.Name))
            {
                var nameSim = CalculateStringSimilarity(m1.Name.ToLowerInvariant(), m2.Name.ToLowerInvariant());
                score += nameSim * 0.5m;
                weight += 0.5m;
            }

            // So sánh Description (10% trọng số)
            if (!string.IsNullOrWhiteSpace(m1.Description) && !string.IsNullOrWhiteSpace(m2.Description))
            {
                var descSim = CalculateStringSimilarity(m1.Description.ToLowerInvariant(), m2.Description.ToLowerInvariant());
                score += descSim * 0.1m;
                weight += 0.1m;
            }

            // So sánh Unit (bonus)
            if (m1.Unit == m2.Unit && !string.IsNullOrWhiteSpace(m1.Unit))
            {
                score += 0.05m;
                weight += 0.05m;
            }

            return weight > 0 ? score / weight : 0;
        }

        private decimal CalculateStringSimilarity(string s1, string s2)
        {
            if (s1 == s2) return 1.0m;
            if (string.IsNullOrWhiteSpace(s1) || string.IsNullOrWhiteSpace(s2)) return 0;

            // Levenshtein distance
            var distance = LevenshteinDistance(s1, s2);
            var maxLen = Math.Max(s1.Length, s2.Length);
            return maxLen > 0 ? 1.0m - (decimal)distance / maxLen : 0;
        }

        private int LevenshteinDistance(string s, string t)
        {
            if (string.IsNullOrEmpty(s)) return string.IsNullOrEmpty(t) ? 0 : t.Length;
            if (string.IsNullOrEmpty(t)) return s.Length;

            int n = s.Length;
            int m = t.Length;
            int[,] d = new int[n + 1, m + 1];

            for (int i = 0; i <= n; d[i, 0] = i++) { }
            for (int j = 0; j <= m; d[0, j] = j++) { }

            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {
                    int cost = (t[j - 1] == s[i - 1]) ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }
            }

            return d[n, m];
        }

        private string GenerateReason(Material m1, Material m2, decimal similarity)
        {
            var reasons = new List<string>();

            if (m1.Code.Equals(m2.Code, StringComparison.OrdinalIgnoreCase))
                reasons.Add("Mã giống nhau");
            else if (CalculateStringSimilarity(m1.Code, m2.Code) > 0.8m)
                reasons.Add("Mã tương tự");

            if (m1.Name.Equals(m2.Name, StringComparison.OrdinalIgnoreCase))
                reasons.Add("Tên giống nhau");
            else if (CalculateStringSimilarity(m1.Name, m2.Name) > 0.8m)
                reasons.Add("Tên tương tự");

            if (m1.Unit == m2.Unit && !string.IsNullOrWhiteSpace(m1.Unit))
                reasons.Add("Cùng đơn vị");

            return reasons.Any() 
                ? string.Join(", ", reasons) 
                : $"Độ tương đồng: {similarity:P0}";
        }
    }
}






























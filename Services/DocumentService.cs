using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using MNBEMART.Data;
using MNBEMART.Models;
using System.IO;

namespace MNBEMART.Services
{
    public class DocumentService : IDocumentService
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _env;
        private const long MaxFileSize = 10 * 1024 * 1024; // 10MB
        private static readonly string[] AllowedExtensions = { ".pdf", ".jpg", ".jpeg", ".png", ".xlsx", ".xls", ".doc", ".docx" };

        public DocumentService(AppDbContext context, IWebHostEnvironment env)
        {
            _context = context;
            _env = env;
        }

        public async Task<Document> UploadFileAsync(string documentType, int documentId, IFormFile file, string? description, int uploadedById, DocumentCategory? category = null)
        {
            // Validate file
            var validation = await ValidateFileAsync(file);
            if (!validation.IsValid)
                throw new InvalidOperationException(validation.ErrorMessage ?? "File không hợp lệ");

            // Create directory structure: wwwroot/uploads/documents/{DocumentType}/{DocumentId}/
            var uploadsRoot = Path.Combine(_env.WebRootPath ?? "", "uploads", "documents", documentType, documentId.ToString());
            Directory.CreateDirectory(uploadsRoot);

            // Generate safe file name
            var originalFileName = file.FileName;
            var extension = Path.GetExtension(originalFileName);
            var safeFileName = $"{Guid.NewGuid():N}{extension}";
            var filePath = Path.Combine(uploadsRoot, safeFileName);

            // Save file
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // Get relative path from wwwroot
            var relativePath = $"/uploads/documents/{documentType}/{documentId}/{safeFileName}";

            // Create document record
            var document = new Document
            {
                DocumentType = documentType,
                DocumentId = documentId,
                FileName = originalFileName,
                FilePath = relativePath,
                FileSize = file.Length,
                MimeType = file.ContentType,
                Description = description,
                Category = category,
                UploadedAt = DateTime.Now,
                UploadedById = uploadedById
            };

            _context.Documents.Add(document);
            await _context.SaveChangesAsync();

            return document;
        }

        public async Task<bool> DeleteFileAsync(int documentId)
        {
            var document = await _context.Documents.FindAsync(documentId);
            if (document == null)
                return false;

            // Delete physical file
            var physicalPath = Path.Combine(_env.WebRootPath ?? "", document.FilePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(physicalPath))
            {
                File.Delete(physicalPath);
            }

            // Delete database record
            _context.Documents.Remove(document);
            await _context.SaveChangesAsync();

            return true;
        }

        public async Task<Document?> GetDocumentAsync(int documentId)
        {
            return await _context.Documents
                .Include(d => d.UploadedBy)
                .FirstOrDefaultAsync(d => d.Id == documentId);
        }

        public async Task<List<Document>> GetDocumentsByDocumentTypeAsync(string documentType, int documentId)
        {
            return await _context.Documents
                .Include(d => d.UploadedBy)
                .Where(d => d.DocumentType == documentType && d.DocumentId == documentId)
                .OrderByDescending(d => d.UploadedAt)
                .ToListAsync();
        }

        public Task<(bool IsValid, string? ErrorMessage)> ValidateFileAsync(IFormFile file)
        {
            // Check file size
            if (file.Length > MaxFileSize)
            {
                return Task.FromResult<(bool, string?)>(
                    (false, $"File quá lớn. Kích thước tối đa là {MaxFileSize / (1024 * 1024)}MB"));
            }

            if (file.Length == 0)
            {
                return Task.FromResult<(bool, string?)>((false, "File rỗng"));
            }

            // Check file extension
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (string.IsNullOrEmpty(extension) || !AllowedExtensions.Contains(extension))
            {
                return Task.FromResult<(bool, string?)>(
                    (false, $"Định dạng file không được phép. Chỉ chấp nhận: {string.Join(", ", AllowedExtensions)}"));
            }

            // Check MIME type
            var allowedMimeTypes = new[]
            {
                "application/pdf",
                "image/jpeg", "image/jpg", "image/png",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "application/vnd.ms-excel",
                "application/msword",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
            };

            if (!string.IsNullOrEmpty(file.ContentType) && !allowedMimeTypes.Contains(file.ContentType.ToLowerInvariant()))
            {
                return Task.FromResult<(bool, string?)>(
                    (false, "Loại file không được phép"));
            }

            return Task.FromResult<(bool, string?)>((true, null));
        }

        public Task<FileStream?> GetFileStreamAsync(Document document)
        {
            var physicalPath = Path.Combine(_env.WebRootPath ?? "", document.FilePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            
            if (!File.Exists(physicalPath))
                return Task.FromResult<FileStream?>(null);

            try
            {
                var stream = new FileStream(physicalPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                return Task.FromResult<FileStream?>(stream);
            }
            catch
            {
                return Task.FromResult<FileStream?>(null);
            }
        }
    }
}



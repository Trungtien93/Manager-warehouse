using MNBEMART.Models;

namespace MNBEMART.Services
{
    public interface IDocumentService
    {
        Task<Document> UploadFileAsync(string documentType, int documentId, IFormFile file, string? description, int uploadedById, DocumentCategory? category = null);
        Task<bool> DeleteFileAsync(int documentId);
        Task<Document?> GetDocumentAsync(int documentId);
        Task<List<Document>> GetDocumentsByDocumentTypeAsync(string documentType, int documentId);
        Task<(bool IsValid, string? ErrorMessage)> ValidateFileAsync(IFormFile file);
        Task<FileStream?> GetFileStreamAsync(Document document);
    }
}



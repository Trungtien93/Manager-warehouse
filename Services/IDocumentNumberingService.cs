using MNBEMART.Models;

namespace MNBEMART.Services
{
    public interface IDocumentNumberingService
    {
        Task<string> NextAsync(string documentType, int? warehouseId = null);
        Task<string> PeekAsync(string documentType, int? warehouseId = null);
    }
}

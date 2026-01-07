using MNBEMART.ViewModels;

namespace MNBEMART.Services
{
    public interface IImportService
    {
        Task<ImportResultVM> ImportMaterials(Stream excelStream);
        byte[] GenerateMaterialTemplate();
    }
}




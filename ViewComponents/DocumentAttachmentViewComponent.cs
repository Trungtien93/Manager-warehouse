using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MNBEMART.Data;
using MNBEMART.Services;

namespace MNBEMART.ViewComponents
{
    public class DocumentAttachmentViewComponent : ViewComponent
    {
        private readonly IDocumentService _documentService;

        public DocumentAttachmentViewComponent(IDocumentService documentService)
        {
            _documentService = documentService;
        }

        public async Task<IViewComponentResult> InvokeAsync(string documentType, int documentId)
        {
            var documents = await _documentService.GetDocumentsByDocumentTypeAsync(documentType, documentId);
            
            ViewBag.DocumentType = documentType;
            ViewBag.DocumentId = documentId;
            ViewBag.Documents = documents;
            
            return View(documents);
        }
    }
}


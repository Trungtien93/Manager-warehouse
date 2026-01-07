using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using MNBEMART.Data;
using MNBEMART.Extensions;
using MNBEMART.Filters;
using MNBEMART.Models;
using MNBEMART.Services;
using System.Security.Claims;

namespace MNBEMART.Controllers
{
    [Authorize]
    public class DocumentManagementController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IDocumentService _documentService;

        public DocumentManagementController(AppDbContext context, IDocumentService documentService)
        {
            _context = context;
            _documentService = documentService;
        }

        private int GetCurrentUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(userIdClaim, out int userId) ? userId : 0;
        }

        // GET: DocumentManagement/List - Xem tất cả documents
        [RequirePermission("Documents", "Read")]
        public async Task<IActionResult> List(
            string? documentType,
            DocumentCategory? category,
            string? q,
            int? uploadedById,
            DateTime? fromDate,
            DateTime? toDate,
            int page = 1,
            int pageSize = 30)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 5, 100);

            var query = _context.Documents
                .AsNoTracking()
                .Include(d => d.UploadedBy)
                .AsQueryable();

            // Filters
            if (!string.IsNullOrEmpty(documentType))
            {
                query = query.Where(d => d.DocumentType == documentType);
            }

            if (category.HasValue)
            {
                query = query.Where(d => d.Category == category);
            }

            if (!string.IsNullOrWhiteSpace(q))
            {
                query = query.Where(d => d.FileName.Contains(q) || (d.Description != null && d.Description.Contains(q)));
            }

            if (uploadedById.HasValue)
            {
                query = query.Where(d => d.UploadedById == uploadedById.Value);
            }

            if (fromDate.HasValue)
            {
                query = query.Where(d => d.UploadedAt >= fromDate.Value.Date);
            }

            if (toDate.HasValue)
            {
                query = query.Where(d => d.UploadedAt < toDate.Value.Date.AddDays(1));
            }

            // Pagination
            var pagedResult = await query
                .OrderByDescending(d => d.UploadedAt)
                .ToPagedResultAsync(page, pageSize);

            // ViewBag for filters
            ViewBag.DocumentTypes = new SelectList(new[]
            {
                new { Value = "PurchaseRequest", Text = "Đề xuất đặt hàng" },
                new { Value = "StockReceipt", Text = "Phiếu nhập kho" },
                new { Value = "StockIssue", Text = "Phiếu xuất kho" },
                new { Value = "StockTransfer", Text = "Phiếu chuyển kho" }
            }, "Value", "Text", documentType);

            ViewBag.Categories = new SelectList(new[]
            {
                new { Value = (int)DocumentCategory.Invoice, Text = "Hóa đơn" },
                new { Value = (int)DocumentCategory.Evidence, Text = "Ảnh minh chứng" },
                new { Value = (int)DocumentCategory.Contract, Text = "Hợp đồng" },
                new { Value = (int)DocumentCategory.DeliveryNote, Text = "Phiếu giao hàng" },
                new { Value = (int)DocumentCategory.Other, Text = "Khác" }
            }, "Value", "Text", category);

            var users = await _context.Users
                .Where(u => u.IsActive)
                .Select(u => new { u.Id, u.FullName })
                .ToListAsync();

            ViewBag.Users = new SelectList(users, "Id", "FullName", uploadedById);
            ViewBag.Page = pagedResult.Page;
            ViewBag.PageSize = pagedResult.PageSize;
            ViewBag.TotalPages = (int)Math.Ceiling(pagedResult.TotalItems / (double)pageSize);
            ViewBag.TotalItems = pagedResult.TotalItems;
            ViewBag.Filter = new
            {
                documentType,
                category,
                q,
                uploadedById,
                fromDate = fromDate?.ToString("yyyy-MM-dd"),
                toDate = toDate?.ToString("yyyy-MM-dd")
            };

            return View(pagedResult.Items);
        }

        // GET: DocumentManagement/Index?documentType=StockReceipt&documentId=1
        [RequirePermission("Documents", "Read")]
        public async Task<IActionResult> Index(string documentType, int documentId)
        {
            if (string.IsNullOrEmpty(documentType) || documentId <= 0)
                return BadRequest();

            var documents = await _documentService.GetDocumentsByDocumentTypeAsync(documentType, documentId);
            ViewBag.DocumentType = documentType;
            ViewBag.DocumentId = documentId;
            
            return View(documents);
        }

        // POST: DocumentManagement/Upload
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Documents", "Create")]
        public async Task<IActionResult> Upload(string documentType, int documentId, IFormFile file, string? description, DocumentCategory? category)
        {
            if (string.IsNullOrEmpty(documentType) || documentId <= 0 || file == null || file.Length == 0)
            {
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                    return Json(new { success = false, message = "Dữ liệu không hợp lệ" });
                return BadRequest();
            }

            try
            {
                // Validate file
                var validation = await _documentService.ValidateFileAsync(file);
                if (!validation.IsValid)
                {
                    if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                        return Json(new { success = false, message = validation.ErrorMessage });
                    TempData["Error"] = validation.ErrorMessage ?? "File không hợp lệ";
                    return RedirectToAction(nameof(Index), new { documentType, documentId });
                }

                var uploadedById = GetCurrentUserId();
                var document = await _documentService.UploadFileAsync(documentType, documentId, file, description, uploadedById, category);

                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                    return Json(new { success = true, message = "Upload thành công", documentId = document.Id });

                TempData["Msg"] = "Upload file thành công";
                return RedirectToAction(nameof(Index), new { documentType, documentId });
            }
            catch (Exception ex)
            {
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                    return Json(new { success = false, message = $"Lỗi: {ex.Message}" });

                TempData["Error"] = $"Lỗi upload: {ex.Message}";
                return RedirectToAction(nameof(Index), new { documentType, documentId });
            }
        }

        // GET: DocumentManagement/Download/5
        [RequirePermission("Documents", "Read")]
        public async Task<IActionResult> Download(int id)
        {
            var document = await _documentService.GetDocumentAsync(id);
            if (document == null)
                return NotFound();

            var stream = await _documentService.GetFileStreamAsync(document);
            if (stream == null)
                return NotFound();

            return File(stream, document.MimeType ?? "application/octet-stream", document.FileName);
        }

        // POST: DocumentManagement/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Documents", "Delete")]
        public async Task<IActionResult> Delete(int id, string? documentType = null, int? documentId = null, string? returnTo = null)
        {
            try
            {
                var document = await _documentService.GetDocumentAsync(id);
                var success = await _documentService.DeleteFileAsync(id);
                
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                    return Json(new { success, message = success ? "Xóa file thành công" : "Không tìm thấy file" });

                if (success)
                    TempData["Msg"] = "Xóa file thành công";
                else
                    TempData["Error"] = "Không tìm thấy file";

                // Redirect về List nếu returnTo = "list" hoặc không có documentType/documentId
                if (returnTo == "list" || (string.IsNullOrEmpty(documentType) && !documentId.HasValue))
                {
                    return RedirectToAction(nameof(List));
                }

                // Ngược lại redirect về Index của document gốc
                if (document != null && !string.IsNullOrEmpty(document.DocumentType))
                {
                    return RedirectToAction(nameof(Index), new { documentType = document.DocumentType, documentId = document.DocumentId });
                }

                // Fallback
                return RedirectToAction(nameof(List));
            }
            catch (Exception ex)
            {
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                    return Json(new { success = false, message = $"Lỗi: {ex.Message}" });

                TempData["Error"] = $"Lỗi xóa file: {ex.Message}";
                
                if (returnTo == "list" || (string.IsNullOrEmpty(documentType) && !documentId.HasValue))
                {
                    return RedirectToAction(nameof(List));
                }
                
                if (!string.IsNullOrEmpty(documentType) && documentId.HasValue)
                {
                    return RedirectToAction(nameof(Index), new { documentType, documentId });
                }
                
                return RedirectToAction(nameof(List));
            }
        }

        // GET: DocumentManagement/GetAttachments?documentType=StockReceipt&documentId=1
        [HttpGet]
        [RequirePermission("Documents", "Read")]
        public async Task<IActionResult> GetAttachments(string documentType, int documentId)
        {
            if (string.IsNullOrEmpty(documentType) || documentId <= 0)
                return Json(new { success = false, documents = new List<object>() });

            var documents = await _documentService.GetDocumentsByDocumentTypeAsync(documentType, documentId);
            
            var result = documents.Select(d => new
            {
                id = d.Id,
                fileName = d.FileName,
                fileSize = d.FileSize,
                mimeType = d.MimeType,
                description = d.Description,
                category = d.Category?.ToString() ?? "",
                categoryName = d.Category switch
                {
                    DocumentCategory.Invoice => "Hóa đơn",
                    DocumentCategory.Evidence => "Ảnh minh chứng",
                    DocumentCategory.Contract => "Hợp đồng",
                    DocumentCategory.DeliveryNote => "Phiếu giao hàng",
                    DocumentCategory.Other => "Khác",
                    _ => ""
                },
                uploadedAt = d.UploadedAt.ToString("dd/MM/yyyy HH:mm"),
                uploadedBy = d.UploadedBy?.FullName ?? "Unknown",
                downloadUrl = Url.Action(nameof(Download), new { id = d.Id }),
                deleteUrl = Url.Action(nameof(Delete), new { id = d.Id, documentType, documentId })
            }).ToList();

            return Json(new { success = true, documents = result });
        }
    }
}


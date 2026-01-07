using Microsoft.AspNetCore.Mvc;
using MNBEMART.Models;

namespace MNBEMART.ViewComponents
{
    public class PaginationViewComponent : ViewComponent
    {
        public IViewComponentResult Invoke(PagedResult<object>? model = null, int? page = null, int? totalPages = null, int? pageSize = null, string? actionName = null, object? routeValues = null)
        {
            // If model is provided, use it
            if (model != null)
            {
                return View(new PaginationViewModel
                {
                    Page = model.Page,
                    TotalPages = model.TotalPages,
                    PageSize = model.PageSize,
                    ActionName = actionName ?? "Index",
                    RouteValues = routeValues
                });
            }

            // Otherwise, use individual parameters (for backward compatibility with ViewBag)
            return View(new PaginationViewModel
            {
                Page = page ?? 1,
                TotalPages = totalPages ?? 1,
                PageSize = pageSize ?? 30,
                ActionName = actionName ?? "Index",
                RouteValues = routeValues
            });
        }
    }

    public class PaginationViewModel
    {
        public int Page { get; set; } = 1;
        public int TotalPages { get; set; } = 1;
        public int PageSize { get; set; } = 30;
        public string ActionName { get; set; } = "Index";
        public object? RouteValues { get; set; }
        public bool HasPrevious => Page > 1;
        public bool HasNext => Page < TotalPages;
    }
}













































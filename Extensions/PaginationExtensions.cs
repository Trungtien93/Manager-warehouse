using Microsoft.EntityFrameworkCore;
using MNBEMART.Models;
using System.Linq;

namespace MNBEMART.Extensions
{
    public static class PaginationExtensions
    {
        /// <summary>
        /// Applies pagination to an IQueryable and returns a PagedResult
        /// </summary>
        public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
            this IQueryable<T> query,
            int page = 1,
            int pageSize = 30,
            int minPageSize = 5,
            int maxPageSize = 100)
        {
            // Validate and clamp page
            page = System.Math.Max(1, page);
            pageSize = System.Math.Clamp(pageSize, minPageSize, maxPageSize);

            // Get total count
            var totalItems = await query.CountAsync();

            // Apply pagination
            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return new PagedResult<T>
            {
                Items = items,
                Page = page,
                PageSize = pageSize,
                TotalItems = totalItems
            };
        }
    }
}













































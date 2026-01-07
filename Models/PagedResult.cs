using System.Collections.Generic;

namespace MNBEMART.Models
{
    public class PagedResult<T>
    {
        public IEnumerable<T> Items { get; set; } = new List<T>();
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 30;
        public int TotalItems { get; set; }
        public int TotalPages => (int)System.Math.Ceiling((double)System.Math.Max(1, TotalItems) / PageSize);
        public bool HasPrevious => Page > 1;
        public bool HasNext => Page < TotalPages;
    }
}













































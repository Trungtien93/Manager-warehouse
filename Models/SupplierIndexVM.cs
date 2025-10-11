using System.Collections.Generic;

namespace MNBEMART.Models
{
    public class SupplierIndexVM
    {
        public IEnumerable<Supplier> Items { get; set; } = new List<Supplier>();
        public string? Q { get; set; }

        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 30;
        public int TotalItems { get; set; }
        public int TotalPages => (int)System.Math.Ceiling((double)System.Math.Max(1, TotalItems) / PageSize);
    }
}

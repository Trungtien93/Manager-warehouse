namespace MNBEMART.ViewModels
{
    public class StockAgingVM
    {
        public List<StockAgingItemVM> Items { get; set; } = new();
    }

    public class StockAgingItemVM
    {
        public int MaterialId { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        public decimal CurrentStock { get; set; }
        public decimal StockValue { get; set; }
        public DateTime? LastIssueDate { get; set; }
        public int DaysSinceLastIssue { get; set; }
        public string AgingCategory { get; set; } = string.Empty; // Fast, Normal, Slow, Dead
    }
}























































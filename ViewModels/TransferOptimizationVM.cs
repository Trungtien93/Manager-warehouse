namespace MNBEMART.ViewModels
{
    public class WarehouseSuggestionVM
    {
        public WarehouseInfoVM? BestWarehouse { get; set; }
        public List<WarehouseInfoVM> Alternatives { get; set; } = new();
        public List<string> Reasons { get; set; } = new();
    }

    public class WarehouseInfoVM
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public decimal AvailableStock { get; set; }
        public decimal DistanceKm { get; set; }
        public decimal EstimatedCost { get; set; }
        public decimal Score { get; set; }
        public string ScoreReason { get; set; } = string.Empty;
    }

    public class TransferCostVM
    {
        public decimal BaseCost { get; set; }
        public decimal DistanceCost { get; set; }
        public decimal WeightCost { get; set; }
        public decimal VolumeCost { get; set; }
        public decimal TotalCost { get; set; }
        public decimal DistanceKm { get; set; }
        public decimal TotalWeight { get; set; }
        public decimal TotalVolume { get; set; }
        public decimal EstimatedTimeHours { get; set; }
    }

    public class TransferItemVM
    {
        public int MaterialId { get; set; }
        public decimal Quantity { get; set; }
        public decimal Weight { get; set; }  // Weight per unit in kg
        public decimal Volume { get; set; }  // Volume per unit in m³
    }

    public class MaterialRequirementVM
    {
        public int MaterialId { get; set; }
        public decimal RequiredQuantity { get; set; }
    }

    public class OptimizedRouteVM
    {
        public int FromWarehouseId { get; set; }
        public string FromWarehouseName { get; set; } = string.Empty;
        public int ToWarehouseId { get; set; }
        public string ToWarehouseName { get; set; } = string.Empty;
        public List<MaterialRequirementVM> Items { get; set; } = new();
        public decimal TotalCost { get; set; }
        public decimal TotalDistance { get; set; }
    }

    public class CalculateCostRequest
    {
        public int FromWarehouseId { get; set; }
        public int ToWarehouseId { get; set; }
        public List<TransferItemVM> Items { get; set; } = new();
    }
}






















































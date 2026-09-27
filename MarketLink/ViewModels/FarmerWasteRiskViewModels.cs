namespace MarketLink.ViewModels
{
    public class FarmerWasteRiskPageViewModel
    {
        public int TotalItems { get; set; }
        public int HighCount { get; set; }
        public int MediumCount { get; set; }
        public int LowCount { get; set; }
        public decimal TotalPredictedExcess { get; set; }

        public List<FarmerWasteRiskItemViewModel> Items { get; set; } = new();
    }

    public class FarmerWasteRiskItemViewModel
    {
        public int WasteRiskPredictionId { get; set; }
        public string ProductName { get; set; } = "";
        public string MarketName { get; set; } = "";
        public DateTime InventoryDate { get; set; }

        public decimal CurrentStock { get; set; }
        public decimal PredictedDemand { get; set; }
        public decimal PredictedExcess { get; set; }
        public decimal ExcessPct { get; set; }

        public string RiskLevel { get; set; } = "";
        public string Recommendation { get; set; } = "";
        public DateTime CreatedAt { get; set; }
    }
}

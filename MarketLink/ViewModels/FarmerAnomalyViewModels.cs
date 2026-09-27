namespace MarketLink.ViewModels
{
    public class FarmerAnomalyPageViewModel
    {
        public int TotalOpen { get; set; }
        public int CriticalCount { get; set; }
        public int HighCount { get; set; }
        public int MediumCount { get; set; }
        public DateTime? LastDetectedAt { get; set; }

        public List<FarmerAnomalyItemViewModel> Items { get; set; } = new();
    }

    public class FarmerAnomalyItemViewModel
    {
        public int AnomalyDetectionId { get; set; }
        public string ProductName { get; set; } = "";
        public string MarketName { get; set; } = "";
        public decimal BaselineAverage { get; set; }
        public decimal ObservedDemand { get; set; }
        public decimal DeviationRatio { get; set; }
        public string Severity { get; set; } = "";
        public string Message { get; set; } = "";
        public DateTime DetectedAt { get; set; }
        public bool IsAcknowledged { get; set; }
    }
}

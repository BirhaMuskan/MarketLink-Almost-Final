using System.ComponentModel.DataAnnotations;

namespace MarketLink.ViewModels
{
    public class FarmerAiForecastPageViewModel
    {
        public List<FarmerAiForecastSelectionViewModel> Products { get; set; } = new();
        public List<FarmerAiForecastSelectionViewModel> Markets { get; set; } = new();
        public List<FarmerAiForecastResultViewModel> RecentPredictions { get; set; } = new();

        public int? SelectedFarmerProductId { get; set; }
        public int? SelectedFarmerMarketId { get; set; }

        [DataType(DataType.Date)]
        public DateTime PredictionDate { get; set; } = DateTime.Today.AddDays(7);
    }

    public class FarmerAiForecastSelectionViewModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    public class FarmerAiForecastResultViewModel
    {
        public int DemandPredictionId { get; set; }
        public string ProductName { get; set; } = "";
        public string MarketName { get; set; } = "";
        public DateTime PredictionDate { get; set; }

        public decimal PredictedDemand { get; set; }
        public decimal RecommendedStock { get; set; }
        public decimal CurrentReservations { get; set; }
        public decimal? ConfidenceScore { get; set; }

        public string ShortageRisk { get; set; } = "";
        public string ModelName { get; set; } = "";
        public string MetricsJson { get; set; } = "";
        public DateTime CreatedAt { get; set; }
    }
}

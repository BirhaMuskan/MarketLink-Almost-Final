namespace MarketLink.Services
{
    public interface IMarketLinkAiService
    {
        Task<AiForecastResponse> ForecastAsync(
            AiForecastRequest request,
            CancellationToken cancellationToken = default);
    }

    public class AiForecastRequest
    {
        public int FarmerProductId { get; set; }
        public int FarmerMarketId { get; set; }

        public DateTime PredictionDate { get; set; }

        public decimal CurrentReservations { get; set; }
        public decimal CurrentStock { get; set; }
        public decimal CurrentPrice { get; set; }

        public List<AiHistoricalDemandRow> History { get; set; } = new();
    }

    public class AiHistoricalDemandRow
    {
        public DateTime Date { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal StockQuantity { get; set; }
        public decimal ReservedQuantity { get; set; }
        public decimal SoldQuantity { get; set; }
    }

    public class AiForecastResponse
    {
        public string Algorithm { get; set; } = "";
        public decimal PredictedDemand { get; set; }
        public decimal RecommendedStock { get; set; }
        public string ShortageRisk { get; set; } = "Low";
        public decimal ConfidenceScore { get; set; }
        public decimal HistoricalAverage { get; set; }
        public decimal Mae { get; set; }
        public decimal Mape { get; set; }
        public int TrainingRows { get; set; }
        public string Explanation { get; set; } = "";
    }
}

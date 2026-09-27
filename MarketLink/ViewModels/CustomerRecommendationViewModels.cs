namespace MarketLink.ViewModels
{
    public class CustomerRecommendationsPageViewModel
    {
        public List<CustomerRecommendationCardViewModel> Recommendations { get; set; } = new();

        public int CompletedOrders { get; set; }
        public int PurchasedProducts { get; set; }
        public int FavoriteProducts { get; set; }

        public string RecommendationMode { get; set; } = "";
        public DateTime GeneratedAt { get; set; }
    }

    public class CustomerRecommendationCardViewModel
    {
        public int FarmerProductId { get; set; }

        public string ProductName { get; set; } = "";
        public string FarmerName { get; set; } = "";
        public string CategoryName { get; set; } = "";
        public string UnitName { get; set; } = "";

        public decimal Price { get; set; }
        public decimal AvailableQuantity { get; set; }

        public string? ImageUrl { get; set; }

        public decimal Score { get; set; }
        public string Reason { get; set; } = "";

        public bool IsFavorite { get; set; }
    }
}

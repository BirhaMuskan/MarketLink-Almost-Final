using System.ComponentModel.DataAnnotations;

namespace MarketLink.ViewModels
{
    public class CustomerSmartSearchPageViewModel
    {
        [StringLength(300)]
        public string Query { get; set; } = "";

        public SmartSearchInterpretationViewModel? Interpretation { get; set; }

        public List<SmartSearchProductViewModel> Results { get; set; } = new();

        public int ResultCount { get; set; }

        public bool HasSearched { get; set; }

        public string? NoResultsMessage { get; set; }

        public List<string> ExampleQueries { get; set; } = new()
        {
            "vegetables under Rs. 1000",
            "tomatoes available Saturday",
            "fruit from Green Market under 500",
            "fresh products from my favorite farmer",
            "spinach on Sunday"
        };
    }

    public class SmartSearchInterpretationViewModel
    {
        public string? ProductIntent { get; set; }
        public string? Category { get; set; }
        public decimal? MaxPrice { get; set; }
        public string? Day { get; set; }
        public string? Market { get; set; }
        public string? Farmer { get; set; }

        public bool AvailableOnly { get; set; } = true;

        public bool UseFavoriteFarmers { get; set; }
        public bool UseFavoriteProducts { get; set; }
        public bool UseFavoriteMarkets { get; set; }

        public List<string> SearchTerms { get; set; } = new();

        public string Summary { get; set; } = "";
    }

    public class SmartSearchProductViewModel
    {
        public int FarmerProductId { get; set; }

        public string ProductName { get; set; } = "";
        public string FarmerName { get; set; } = "";
        public string CategoryName { get; set; } = "";
        public string UnitName { get; set; } = "";

        public decimal Price { get; set; }
        public decimal AvailableQuantity { get; set; }

        public string MarketName { get; set; } = "";
        public string MarketAddress { get; set; } = "";
        public DateTime InventoryDate { get; set; }

        public string? ImageUrl { get; set; }

        public decimal MatchScore { get; set; }

        public string MatchReason { get; set; } = "";
    }
}

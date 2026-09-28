namespace MarketLink.ViewModels
{
    public class HomeViewModel
    {
        public int TotalMarkets { get; set; }
        public int TotalFarmers { get; set; }
        public int TotalAvailableProducts { get; set; }
        public int TotalCategories { get; set; }
        public int TotalOrders { get; set; }

        public bool IsPersonalizedProducts { get; set; }

        public HomeMarketViewModel? HeroMarket { get; set; }

        public HomeProductViewModel? HeroProduct { get; set; }

        public List<HomeProductViewModel> Products { get; set; } = new();

        public List<HomeMarketViewModel> Markets { get; set; } = new();

        public List<HomeFarmerViewModel> Farmers { get; set; } = new();

        public List<HomeReviewViewModel> Reviews { get; set; } = new();

        public List<string> MarketDays { get; set; } = new();
    }


    // =========================================================
    // HOME PRODUCT
    // =========================================================

    public class HomeProductViewModel
    {
        public int FarmerProductId { get; set; }

        public string ProductName { get; set; } = "";

        public string CategoryName { get; set; } = "Uncategorized";

        public string FarmerName { get; set; } = "Local Farmer";

        public string UnitName { get; set; } = "unit";

        public decimal Price { get; set; }

        public decimal AvailableQuantity { get; set; }

        public string? ImageUrl { get; set; }

        public string AvailabilityText { get; set; } = "Available";

        public string AvailabilityClass { get; set; } = "available";
    }


    // =========================================================
    // HOME MARKET
    // =========================================================

    public class HomeMarketViewModel
    {
        public int MarketId { get; set; }

        public string MarketName { get; set; } = "";

        public string Address { get; set; } = "";

        public decimal? Latitude { get; set; }

        public decimal? Longitude { get; set; }

        public string OperatingDaysText { get; set; } = "";

        public string OpeningHoursText { get; set; } = "";

        public int FarmerCount { get; set; }

        public int ProductCount { get; set; }

        public bool HasSchedule { get; set; }

        public string StatusText { get; set; } = "Active";
    }


    // =========================================================
    // HOME FARMER
    // =========================================================

    public class HomeFarmerViewModel
    {
        public int FarmerId { get; set; }

        public string BusinessName { get; set; } = "";

        public string? ProfileImageUrl { get; set; }
    }


    // =========================================================
    // HOME REVIEW
    // =========================================================

    public class HomeReviewViewModel
    {
        public string CustomerName { get; set; } = "Customer";

        public string Comment { get; set; } = "";

        public int Rating { get; set; }

        public DateTime ReviewDate { get; set; }
    }
}
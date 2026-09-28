namespace MarketLink.ViewModels
{
    public class ProductListingViewModel
    {
        public int FarmerProductId { get; set; }

        // Market in which this farmer is selling the product
        public int FarmerMarketId { get; set; }

        public string ProductName { get; set; } = "";

        public string CategoryName { get; set; } = "";

        public string CategorySlug { get; set; } = "";

        public string FarmerName { get; set; } = "";

        public string MarketName { get; set; } = "";

        public string UnitName { get; set; } = "";

        public decimal Price { get; set; }

        public decimal AvailableQuantity { get; set; }

        public string ImageUrl { get; set; } = "";

        public decimal Rating { get; set; }

        public int ReviewCount { get; set; }

        public string BadgeText { get; set; } = "Fresh";

        public string AvailabilityText { get; set; } = "";

        public bool IsOrganic { get; set; }
    }
}
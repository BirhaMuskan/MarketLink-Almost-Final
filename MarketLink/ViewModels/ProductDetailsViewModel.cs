namespace MarketLink.ViewModels
{
    public class ProductDetailsViewModel
    {
        // =========================================================
        // PRODUCT
        // =========================================================

        public int FarmerProductId { get; set; }

        public int ProductId { get; set; }

        public string ProductName { get; set; } = "";

        public string CategoryName { get; set; } = "Uncategorized";

        public string Description { get; set; } = "";

        public string FarmerDescription { get; set; } = "";

        public string ImageUrl { get; set; } =
            "/images/products/default-product.jpg";


        // =========================================================
        // PRICE / UNIT
        // =========================================================

        public decimal Price { get; set; }

        public string UnitName { get; set; } = "unit";


        // =========================================================
        // INVENTORY
        // =========================================================

        public decimal AvailableQuantity { get; set; }

        public string AvailabilityText { get; set; } =
            "Available";


        public bool IsAvailable { get; set; }


        // =========================================================
        // RATING
        // =========================================================

        public decimal Rating { get; set; }

        public int ReviewCount { get; set; }


        // =========================================================
        // FARMER
        // =========================================================

        public int FarmerId { get; set; }

        public string FarmerName { get; set; } =
            "Local Farmer";

        public bool IsFarmerVerified { get; set; }

        public string FarmerDescriptionText { get; set; } = "";


        // =========================================================
        // MARKET
        // =========================================================

        public int? FarmerMarketId { get; set; }

        public int? MarketId { get; set; }

        public string MarketName { get; set; } =
            "Local Market";


        // =========================================================
        // RELATED PRODUCTS
        // =========================================================

        public List<RelatedProductViewModel> RelatedProducts { get; set; }
            = new();
    }


    // =============================================================
    // RELATED PRODUCT VIEW MODEL
    // =============================================================

    public class RelatedProductViewModel
    {
        public int FarmerProductId { get; set; }

        public string ProductName { get; set; } = "";

        public string CategoryName { get; set; } = "";

        public decimal Price { get; set; }

        public string UnitName { get; set; } = "unit";

        public string ImageUrl { get; set; } =
            "/images/products/default-product.jpg";

        public decimal AvailableQuantity { get; set; }

        public string BadgeText { get; set; } = "Fresh";
    }
}
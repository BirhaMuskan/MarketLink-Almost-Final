namespace MarketLink.ViewModels
{
    public class FarmerDetailsViewModel
    {
        // =========================================================
        // FARMER INFORMATION
        // =========================================================

        public int FarmerId { get; set; }

        public string BusinessName { get; set; } = "";

        public string Description { get; set; } = "";

        public string Address { get; set; } = "";

        public string? ProfileImageUrl { get; set; }

        public bool IsApproved { get; set; }

        public bool IsActive { get; set; }


        // =========================================================
        // FARMER RATING
        // =========================================================

        public decimal Rating { get; set; }

        public int ReviewCount { get; set; }


        // =========================================================
        // FARMER PRODUCTS
        // =========================================================

        public int ProductCount { get; set; }

        public List<FarmerDetailsProductViewModel> Products { get; set; }
            = new();


        // =========================================================
        // FAVORITE
        // =========================================================

        public bool IsFavorite { get; set; }
    }


    // =============================================================
    // FARMER PRODUCT VIEW MODEL
    // =============================================================

    public class FarmerDetailsProductViewModel
    {
        public int FarmerProductId { get; set; }

        public int ProductId { get; set; }

        public string ProductName { get; set; } = "";

        public string CategoryName { get; set; } = "";

        public string Description { get; set; } = "";

        public string? ImageUrl { get; set; }

        public decimal Price { get; set; }

        public string UnitName { get; set; } = "";

        public bool IsAvailable { get; set; }

        public bool IsApproved { get; set; }

        public bool IsActive { get; set; }
    }
}
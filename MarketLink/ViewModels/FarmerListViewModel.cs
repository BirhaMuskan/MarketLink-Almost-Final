namespace MarketLink.ViewModels
{
    public class FarmerListViewModel
    {
        public int FarmerId { get; set; }

        public string BusinessName { get; set; } = "";

        public string Description { get; set; } = "";

        public string Address { get; set; } = "";

        public string? ProfileImageUrl { get; set; }

        public bool IsApproved { get; set; }

        public decimal Rating { get; set; }

        public int ProductCount { get; set; }
    }
}
namespace MarketLink.ViewModels
{
    public class HowItWorksViewModel
    {
        // ==============================
        // PLATFORM STATISTICS
        // ==============================

        public int TotalFarmers { get; set; }

        public int TotalCustomers { get; set; }

        public int TotalProducts { get; set; }

        public int TotalMarkets { get; set; }

        public int TotalCategories { get; set; }

        public int TotalOrders { get; set; }


        // ==============================
        // FAQS
        // ==============================

        public List<HowItWorksFaqViewModel> FAQs { get; set; }
            = new List<HowItWorksFaqViewModel>();
    }


    public class HowItWorksFaqViewModel
    {
        public int Id { get; set; }

        public string Question { get; set; } = string.Empty;

        public string Answer { get; set; } = string.Empty;
    }
}
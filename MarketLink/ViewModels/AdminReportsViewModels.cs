namespace MarketLink.ViewModels
{
    public class AdminReportsPageViewModel
    {
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }

        public int TotalOrders { get; set; }
        public int CompletedOrders { get; set; }
        public int CancelledOrders { get; set; }
        public int ActiveFarmers { get; set; }

        public decimal Revenue { get; set; }
        public decimal AverageOrderValue { get; set; }

        public List<AdminOrderStatusMetricViewModel> OrderStatusMix { get; set; } = new();
        public List<AdminFarmerPerformanceViewModel> TopFarmers { get; set; } = new();
        public List<AdminMarketPerformanceViewModel> MarketPerformance { get; set; } = new();
        public List<AdminProductPerformanceViewModel> TopProducts { get; set; } = new();
        public List<AdminMonthlyPerformanceViewModel> MonthlyPerformance { get; set; } = new();
    }

    public class AdminOrderStatusMetricViewModel
    {
        public string Status { get; set; } = "";
        public int Count { get; set; }
        public decimal Percentage { get; set; }
    }

    public class AdminFarmerPerformanceViewModel
    {
        public int FarmerId { get; set; }
        public string FarmerName { get; set; } = "";
        public int OrderCount { get; set; }
        public int CompletedOrders { get; set; }
        public decimal Revenue { get; set; }
    }

    public class AdminMarketPerformanceViewModel
    {
        public int MarketId { get; set; }
        public string MarketName { get; set; } = "";
        public int OrderCount { get; set; }
        public int CompletedOrders { get; set; }
        public decimal Revenue { get; set; }
    }

    public class AdminProductPerformanceViewModel
    {
        public string ProductName { get; set; } = "";
        public decimal QuantitySold { get; set; }
        public decimal Revenue { get; set; }
        public int OrderCount { get; set; }
    }

    public class AdminMonthlyPerformanceViewModel
    {
        public string MonthLabel { get; set; } = "";
        public int OrderCount { get; set; }
        public int CompletedOrders { get; set; }
        public decimal Revenue { get; set; }
    }
}

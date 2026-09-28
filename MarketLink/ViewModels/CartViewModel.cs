using MarketLink.Models;

namespace MarketLink.ViewModels
{
    public class CartViewModel
    {
        public Cart? Cart { get; set; }

        public List<CartItemViewModel> Items { get; set; } = new();

        public decimal TotalQuantity { get; set; }

        public decimal Subtotal { get; set; }

        public decimal GrandTotal { get; set; }

        public bool IsEmpty => Items.Count == 0;
    }

    public class CartItemViewModel
    {
        public int CartItemId { get; set; }

        public int FarmerProductId { get; set; }

        public string ProductName { get; set; } = "";

        public string ProductDescription { get; set; } = "";

        public string CategoryName { get; set; } = "";

        public string? ImageUrl { get; set; }

        public string? UnitName { get; set; }

        public decimal Quantity { get; set; }

        public decimal UnitPrice { get; set; }

        public decimal TotalPrice => Quantity * UnitPrice;

        public int FarmerId { get; set; }

        public int FarmerMarketId { get; set; }
    }
}
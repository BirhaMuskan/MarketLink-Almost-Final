using MarketLink.Models;
using MarketLink.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketLink.Controllers
{
    public class HowItWorksController : Controller
    {
        private readonly ApplicationDbContext _context;

        public HowItWorksController(ApplicationDbContext context)
        {
            _context = context;
        }


        // =========================================================
        // HOW IT WORKS
        // =========================================================

        public async Task<IActionResult> Index()
        {
            var model = new HowItWorksViewModel
            {
                // =================================================
                // LIVE DATABASE STATISTICS
                // =================================================

                TotalFarmers = await _context.farmers.CountAsync(),

                TotalCustomers = await _context.customers.CountAsync(),

                TotalProducts = await _context.products.CountAsync(),

                TotalMarkets = await _context.markets.CountAsync(),

                TotalCategories = await _context.categories.CountAsync(),

                TotalOrders = await _context.orders.CountAsync(),


                // =================================================
                // FAQS
                // Based on MarketLink SRS
                // =================================================

                FAQs = new List<HowItWorksFaqViewModel>
                {
                    new HowItWorksFaqViewModel
                    {
                        Id = 1,

                        Question = "What is MarketLink?",

                        Answer =
                            "MarketLink is a web-based platform that connects local farmers, farmers markets and customers. Customers can discover markets, browse products, find farmers, place pre-orders for pickup and leave reviews."
                    },

                    new HowItWorksFaqViewModel
                    {
                        Id = 2,

                        Question = "How can I find products available at local markets?",

                        Answer =
                            "Customers can browse products by category and use available filters such as price, market and market day. Product details include the farmer, price, unit and available quantity."
                    },

                    new HowItWorksFaqViewModel
                    {
                        Id = 3,

                        Question = "Can I place an order through MarketLink?",

                        Answer =
                            "Yes. Customers can add available products to their cart and place a pre-order for pickup. The customer can select an available pickup date and time slot provided by the farmer."
                    },

                    new HowItWorksFaqViewModel
                    {
                        Id = 4,

                        Question = "Does MarketLink provide home delivery?",

                        Answer =
                            "No. According to the current MarketLink requirements, the platform supports pickup at the market. Delivery and courier logistics are outside the project scope."
                    },

                    new HowItWorksFaqViewModel
                    {
                        Id = 5,

                        Question = "How is payment handled?",

                        Answer =
                            "MarketLink does not include an online payment gateway. Customers pay for their pre-orders in person when they collect their order at the selected pickup point."
                    },

                    new HowItWorksFaqViewModel
                    {
                        Id = 6,

                        Question = "What can farmers do on MarketLink?",

                        Answer =
                            "Farmers can create their profile, add their market locations, manage weekly stock and pricing, manage pickup slots, receive pre-orders and update order status."
                    },

                    new HowItWorksFaqViewModel
                    {
                        Id = 7,

                        Question = "Can customers save favorite farmers and products?",

                        Answer =
                            "Yes. Customers can save preferred farmers and products as favorites so they can access them quickly and keep track of products they are interested in."
                    },

                    new HowItWorksFaqViewModel
                    {
                        Id = 8,

                        Question = "Does MarketLink have an AI assistant?",

                        Answer =
                            "MarketLink includes an optional AI assistant concept. It can help customers find items and answer common questions about markets, farmer availability, pickup windows and product information."
                    }
                }
            };

            return View(model);
        }
    }
}
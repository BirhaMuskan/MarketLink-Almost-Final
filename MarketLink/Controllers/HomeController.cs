using System.Diagnostics;
using System.Security.Claims;
using MarketLink.Models;
using MarketLink.Services;
using MarketLink.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketLink.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly ApplicationDbContext _context;
        private readonly ICustomerRecommendationService _recommendationService;

        public HomeController(
            ILogger<HomeController> logger,
            ApplicationDbContext context,
            ICustomerRecommendationService recommendationService)
        {
            _logger = logger;
            _context = context;
            _recommendationService = recommendationService;
        }


        // =========================================================
        // HOME PAGE
        // =========================================================

        [HttpGet]
        public async Task<IActionResult> Index(
            CancellationToken cancellationToken)
        {
            var today = DateTime.Today;


            // =====================================================
            // 1. ACTIVE / APPROVED FARMERS
            // =====================================================

            var activeFarmers = await _context.farmers
                .AsNoTracking()
                .Where(f =>
                    f.IsApproved &&
                    f.IsActive)
                .OrderBy(f => f.BusinessName)
                .ToListAsync(cancellationToken);

            var activeFarmerIds = activeFarmers
                .Select(f => f.FarmerId)
                .ToHashSet();


            // =====================================================
            // 2. ACTIVE MARKETS
            // =====================================================

            var activeMarkets = await _context.markets
                .AsNoTracking()
                .Where(m => m.IsActive)
                .OrderBy(m => m.MarketName)
                .ToListAsync(cancellationToken);

            var activeMarketIds = activeMarkets
                .Select(m => m.MarketId)
                .ToHashSet();


            // =====================================================
            // 3. MARKET DAYS / SCHEDULES
            // =====================================================

            var marketDays = await _context.marketdays
                .AsNoTracking()
                .Where(md =>
                    md.IsActive &&
                    activeMarketIds.Contains(md.MarketId))
                .ToListAsync(cancellationToken);


            // =====================================================
            // 4. FARMERS PARTICIPATING IN MARKETS
            // =====================================================

            var farmerMarkets = await _context.farmermarkets
                .AsNoTracking()
                .Where(fm =>
                    fm.IsActive &&
                    activeFarmerIds.Contains(fm.FarmerId) &&
                    activeMarketIds.Contains(fm.MarketId))
                .ToListAsync(cancellationToken);


            // =====================================================
            // 5. ACTIVE FARMER PRODUCTS
            // =====================================================

            var farmerProducts = await _context.farmerproducts
                .AsNoTracking()
                .Include(fp => fp.Farmer)
                .Include(fp => fp.Product)
                    .ThenInclude(p => p!.Category)
                .Include(fp => fp.UnitOfMeasure)
                .Where(fp =>
                    fp.IsApproved &&
                    fp.IsActive &&
                    fp.IsAvailable &&
                    activeFarmerIds.Contains(fp.FarmerId) &&
                    fp.Product != null &&
                    fp.Product.IsActive)
                .ToListAsync(cancellationToken);


            var farmerProductIds = farmerProducts
                .Select(fp => fp.FarmerProductId)
                .ToHashSet();


            // =====================================================
            // 6. TODAY'S INVENTORY
            // =====================================================

            //var inventories = await _context.inventories
            //    .AsNoTracking()
            //    .Where(i =>
            //        farmerProductIds.Contains(i.FarmerProductId) &&
            //        i.InventoryDate.Date == today &&
            //        i.IsAvailable &&
            //        !i.IsSoldOut)
            //    .ToListAsync(cancellationToken);

            var inventories = await _context.inventories
    .AsNoTracking()
    .Where(i =>
        farmerProductIds.Contains(i.FarmerProductId) &&
        i.InventoryDate.Date >= today &&
        i.IsAvailable &&
        !i.IsSoldOut &&
        (i.StockQuantity - i.ReservedQuantity - i.SoldQuantity) > 0)
    .OrderBy(i => i.InventoryDate)
    .ThenByDescending(i => i.UpdatedAt)
    .ToListAsync(cancellationToken);
            // =====================================================
            // 7. AVAILABLE INVENTORY BY PRODUCT
            // =====================================================

            //var inventoryByProduct = inventories
            //    .Where(i =>
            //        i.StockQuantity -
            //        i.ReservedQuantity -
            //        i.SoldQuantity > 0)
            //    .GroupBy(i => i.FarmerProductId)
            //    .ToDictionary(
            //        g => g.Key,
            //        g => g
            //            .OrderByDescending(i => i.UpdatedAt)
            //            .First());

            var inventoryByProduct = inventories
    .GroupBy(i => i.FarmerProductId)
    .ToDictionary(
        g => g.Key,
        g => g
            .OrderBy(i => i.InventoryDate)
            .ThenByDescending(i => i.UpdatedAt)
            .First());

            // =====================================================
            // 8. PRODUCT IMAGES
            // =====================================================

            var imageRows = await _context.productimages
                .AsNoTracking()
                .Where(pi =>
                    farmerProductIds.Contains(
                        pi.FarmerProductId))
                .OrderByDescending(pi => pi.IsPrimary)
                .ThenBy(pi => pi.SortOrder)
                .ToListAsync(cancellationToken);


            var primaryImages = imageRows
                .GroupBy(pi => pi.FarmerProductId)
                .ToDictionary(
                    g => g.Key,
                    g => g.FirstOrDefault()?.ImageUrl);


            // =====================================================
            // 9. BUILD DYNAMIC PRODUCT CARDS
            // =====================================================

            var homeProducts = farmerProducts
                .Where(fp =>
                    inventoryByProduct.ContainsKey(
                        fp.FarmerProductId))
                .Select(fp =>
                {
                    var inventory =
                        inventoryByProduct[
                            fp.FarmerProductId];

                    var available =
                        inventory.StockQuantity -
                        inventory.ReservedQuantity -
                        inventory.SoldQuantity;


                    var availabilityClass =
                        available <= 5
                            ? "limited"
                            : "available";


                    var availabilityText =
                        available <= 5
                            ? "Limited stock"
                            : "Plenty available";


                    return new HomeProductViewModel
                    {
                        FarmerProductId =
                            fp.FarmerProductId,

                        ProductName =
                            fp.Product?.ProductName
                            ?? "Product",

                        CategoryName =
                            fp.Product?.Category?.CategoryName
                            ?? "Uncategorized",

                        FarmerName =
                            fp.Farmer?.BusinessName
                            ?? "Local Farmer",

                        UnitName =
                            fp.UnitOfMeasure?.UnitCode
                            ?? fp.UnitOfMeasure?.UnitName
                            ?? "unit",

                        Price =
                            inventory.UnitPrice > 0
                                ? inventory.UnitPrice
                                : fp.Price,

                        AvailableQuantity =
                            available,

                        ImageUrl =
                            primaryImages.TryGetValue(
                                fp.FarmerProductId,
                                out var image)
                                ? image
                                : fp.Product?.DefaultImageUrl,

                        AvailabilityText =
                            availabilityText,

                        AvailabilityClass =
                            availabilityClass
                    };
                })
                .OrderByDescending(
                    p => p.AvailableQuantity)
                .ThenBy(
                    p => p.ProductName)
                .Take(8)
                .ToList();


            // =====================================================
            // 9B. CUSTOMER RECOMMENDATIONS FOR HOME SECTION 2
            // =====================================================

            var isPersonalizedProducts = false;

            if (User.Identity?.IsAuthenticated == true &&
                User.IsInRole("Customer"))
            {
                var userIdValue =
                    User.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? User.FindFirstValue("sub");

                if (int.TryParse(userIdValue, out var userId))
                {
                    var customer = await _context.customers
                        .AsNoTracking()
                        .FirstOrDefaultAsync(
                            c => c.UserId == userId,
                            cancellationToken);

                    if (customer != null)
                    {
                        var recommendationModel =
                            await _recommendationService.GenerateAsync(
                                customer,
                                cancellationToken);

                        var recommendedProducts = recommendationModel
                            .Recommendations
                            .Take(8)
                            .Select(r => new HomeProductViewModel
                            {
                                FarmerProductId = r.FarmerProductId,
                                ProductName = r.ProductName,
                                CategoryName = r.CategoryName,
                                FarmerName = r.FarmerName,
                                UnitName = r.UnitName,
                                Price = r.Price,
                                AvailableQuantity = r.AvailableQuantity,
                                ImageUrl = r.ImageUrl,
                                AvailabilityText =
                                    r.AvailableQuantity <= 5
                                        ? "Limited stock"
                                        : "Available",
                                AvailabilityClass =
                                    r.AvailableQuantity <= 5
                                        ? "limited"
                                        : "available"
                            })
                            .ToList();

                        // Keep public dynamic products as the fallback for
                        // brand-new customers with no recommendation history.
                        if (recommendedProducts.Any())
                        {
                            homeProducts = recommendedProducts;
                            isPersonalizedProducts = true;
                        }
                    }
                }
            }


            // =====================================================
            // 10. BUILD DYNAMIC MARKET CARDS
            // =====================================================

            var homeMarkets = activeMarkets
                .Select(market =>
                {
                    var days = marketDays
                        .Where(md =>
                            md.MarketId ==
                            market.MarketId)
                        .OrderBy(
                            md => DayOrder(md.DayName))
                        .ToList();


                    var participatingFarmerIds =
                        farmerMarkets
                            .Where(fm =>
                                fm.MarketId ==
                                market.MarketId)
                            .Select(fm =>
                                fm.FarmerId)
                            .Distinct()
                            .ToHashSet();


                    var productCount =
                        farmerProducts
                            .Where(fp =>
                                participatingFarmerIds
                                    .Contains(fp.FarmerId)
                                &&
                                inventoryByProduct
                                    .ContainsKey(
                                        fp.FarmerProductId))
                            .Select(fp =>
                                fp.ProductId)
                            .Distinct()
                            .Count();


                    return new HomeMarketViewModel
                    {
                        MarketId =
                            market.MarketId,

                        MarketName =
                            market.MarketName,

                        Address =
                            market.Address,

                        Latitude =
                            market.Latitude,

                        Longitude =
                            market.Longitude,

                        OperatingDaysText =
                            days.Count == 0
                                ? "Schedule not set"
                                : string.Join(
                                    " & ",
                                    days.Select(
                                        d => ShortDay(
                                            d.DayName))),

                        OpeningHoursText =
                            days.Count == 0
                                ? "Hours not set"
                                : FormatHours(
                                    days.Min(
                                        d => d.OpeningTime),
                                    days.Max(
                                        d => d.ClosingTime)),

                        FarmerCount =
                            participatingFarmerIds.Count,

                        ProductCount =
                            productCount,

                        HasSchedule =
                            days.Count > 0,

                        StatusText =
                            days.Count > 0
                                ? "Open This Week"
                                : "Schedule Pending"
                    };
                })
                .OrderByDescending(
                    m => m.FarmerCount)
                .ThenBy(
                    m => m.MarketName)
                .Take(8)
                .ToList();


            // =====================================================
            // 11. FEATURED FARMERS
            // =====================================================

            var featuredFarmers =
                activeFarmers
                    .Where(f =>
                        !string.IsNullOrWhiteSpace(
                            f.BusinessName))
                    .Take(6)
                    .Select(f =>
                        new HomeFarmerViewModel
                        {
                            FarmerId =
                                f.FarmerId,

                            BusinessName =
                                f.BusinessName,

                            ProfileImageUrl =
                                f.ProfileImageUrl
                        })
                    .ToList();


            // =====================================================
            // 12. PUBLIC REVIEWS
            // =====================================================

            var reviews =
                await _context.reviews
                    .AsNoTracking()
                    .Include(r => r.Customer)
                        .ThenInclude(c => c!.User)
                    .Where(r =>
                        r.IsVisible &&
                        r.Rating >= 1 &&
                        r.Rating <= 5)
                    .OrderByDescending(
                        r => r.ReviewDate)
                    .Take(6)
                    .Select(r =>
                        new HomeReviewViewModel
                        {
                            CustomerName =
                                r.Customer != null &&
                                r.Customer.User != null
                                    ? r.Customer.User.FullName
                                    : "MarketLink Customer",

                            Comment =
                                r.Comment,

                            Rating =
                                r.Rating,

                            ReviewDate =
                                r.ReviewDate
                        })
                    .ToListAsync(
                        cancellationToken);


            // =====================================================
            // 13. DYNAMIC MARKET DAYS
            // =====================================================

            var daysForFilter =
                marketDays
                    .Select(d => d.DayName)
                    .Where(d =>
                        !string.IsNullOrWhiteSpace(d))
                    .Distinct(
                        StringComparer.OrdinalIgnoreCase)
                    .OrderBy(DayOrder)
                    .ToList();


            // =====================================================
            // 14. TOTAL ORDERS
            // =====================================================

            var totalOrders =
                await _context.orders
                    .AsNoTracking()
                    .CountAsync(
                        cancellationToken);


            // =====================================================
            // 15. ACTIVE CATEGORIES
            // =====================================================

            var totalCategories =
                await _context.categories
                    .AsNoTracking()
                    .CountAsync(
                        c => c.IsActive,
                        cancellationToken);


            // =====================================================
            // 16. FINAL HOME MODEL
            // =====================================================

            var model = new HomeViewModel
            {
                TotalMarkets =
                    activeMarkets.Count,

                TotalFarmers =
                    activeFarmers.Count,

                TotalAvailableProducts =
                    homeProducts.Count,

                TotalCategories =
                    totalCategories,

                TotalOrders =
                    totalOrders,

                IsPersonalizedProducts =
                    isPersonalizedProducts,

                HeroMarket =
                    homeMarkets.FirstOrDefault(),

                HeroProduct =
                    homeProducts.FirstOrDefault(),

                Products =
                    homeProducts,

                Markets =
                    homeMarkets,

                Farmers =
                    featuredFarmers,

                Reviews =
                    reviews,

                MarketDays =
                    daysForFilter
            };


            return View(model);
        }


        // =========================================================
        // PRIVACY
        // =========================================================

        public IActionResult Privacy()
        {
            return View();
        }


        // =========================================================
        // ERROR
        // =========================================================

        [ResponseCache(
            Duration = 0,
            Location = ResponseCacheLocation.None,
            NoStore = true)]
        public IActionResult Error()
        {
            return View(
                new ErrorViewModel
                {
                    RequestId =
                        Activity.Current?.Id
                        ?? HttpContext.TraceIdentifier
                });
        }


        // =========================================================
        // DAY ORDER
        // =========================================================

        private static int DayOrder(
            string? dayName)
        {
            return dayName?
                .Trim()
                .ToLowerInvariant() switch
            {
                "monday" => 1,
                "tuesday" => 2,
                "wednesday" => 3,
                "thursday" => 4,
                "friday" => 5,
                "saturday" => 6,
                "sunday" => 7,
                _ => 99
            };
        }


        // =========================================================
        // SHORT DAY
        // =========================================================

        private static string ShortDay(
            string dayName)
        {
            return dayName
                .Trim()
                .ToLowerInvariant() switch
            {
                "monday" => "Mon",
                "tuesday" => "Tue",
                "wednesday" => "Wed",
                "thursday" => "Thu",
                "friday" => "Fri",
                "saturday" => "Sat",
                "sunday" => "Sun",
                _ => dayName
            };
        }


        // =========================================================
        // FORMAT HOURS
        // =========================================================

        private static string FormatHours(
            TimeSpan opening,
            TimeSpan closing)
        {
            return
                $"{DateTime.Today.Add(opening):h:mm tt} – " +
                $"{DateTime.Today.Add(closing):h:mm tt}";
        }
    }
}
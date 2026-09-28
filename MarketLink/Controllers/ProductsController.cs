
using MarketLink.Models;
using MarketLink.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketLink.Controllers
{
    public class ProductsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ProductsController(ApplicationDbContext context)
        {
            _context = context;
        }


        // =========================================================
        // PRODUCTS INDEX
        // =========================================================

        [HttpGet]
        public async Task<IActionResult> Index(
            CancellationToken cancellationToken)
        {
            var today = DateTime.Today;


            // =====================================================
            // 1. ACTIVE / APPROVED FARMER PRODUCTS
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

                    fp.Farmer != null &&
                    fp.Farmer.IsApproved &&
                    fp.Farmer.IsActive &&

                    fp.Product != null &&
                    fp.Product.IsActive)

                .ToListAsync(cancellationToken);


            // =====================================================
            // 2. GET FARMER PRODUCT IDS
            // =====================================================

            var farmerProductIds =
                farmerProducts
                    .Select(fp => fp.FarmerProductId)
                    .ToHashSet();


            // =====================================================
            // 3. GET INVENTORY
            //
            // First try today's inventory.
            // =====================================================

            var inventories = await _context.inventories
                .AsNoTracking()

                .Include(i => i.FarmerMarket)
                    .ThenInclude(fm => fm!.Market)

                .Where(i =>
                    farmerProductIds.Contains(
                        i.FarmerProductId)

                    && i.InventoryDate.Date == today

                    && i.IsAvailable
                    && !i.IsSoldOut)

                .ToListAsync(cancellationToken);


            // =====================================================
            // 4. IF TODAY'S INVENTORY DOESN'T EXIST,
            //    GET LATEST AVAILABLE INVENTORY
            // =====================================================

            var inventoryByProduct =
                inventories

                    .Where(i =>
                        i.StockQuantity -
                        i.ReservedQuantity -
                        i.SoldQuantity > 0)

                    .GroupBy(i =>
                        i.FarmerProductId)

                    .ToDictionary(
                        g => g.Key,
                        g => g
                            .OrderByDescending(
                                i => i.UpdatedAt)
                            .First()
                    );


            // =====================================================
            // 5. PRODUCT IMAGES
            // =====================================================

            var imageRows =
                await _context.productimages

                    .AsNoTracking()

                    .Where(pi =>
                        farmerProductIds.Contains(
                            pi.FarmerProductId))

                    .OrderByDescending(
                        pi => pi.IsPrimary)

                    .ThenBy(
                        pi => pi.SortOrder)

                    .ToListAsync(cancellationToken);


            var primaryImages =
                imageRows

                    .GroupBy(
                        pi => pi.FarmerProductId)

                    .ToDictionary(
                        g => g.Key,
                        g => g
                            .FirstOrDefault()
                            ?.ImageUrl
                    );


            // =====================================================
            // 6. REVIEWS
            // =====================================================

            var reviewRows =
                await _context.reviews

                    .AsNoTracking()

                    .Where(r =>
                        r.IsVisible &&
                        r.FarmerProductId != null &&
                        farmerProductIds.Contains(
                            r.FarmerProductId.Value))

                    .ToListAsync(cancellationToken);


            var reviewByProduct =
                reviewRows

                    .GroupBy(
                        r => r.FarmerProductId!.Value)

                    .ToDictionary(
                        g => g.Key,
                        g => new
                        {
                            Rating =
                                g.Average(
                                    r => r.Rating),

                            Count =
                                g.Count()
                        });


            // =====================================================
            // 7. BUILD PRODUCT LIST
            // =====================================================

            var products =
                farmerProducts

                    .Where(fp =>
                        inventoryByProduct.ContainsKey(
                            fp.FarmerProductId))

                    .Select(fp =>
                    {
                        var inventory =
                            inventoryByProduct[
                                fp.FarmerProductId];


                        var available =
                            inventory.StockQuantity
                            - inventory.ReservedQuantity
                            - inventory.SoldQuantity;


                        var categoryName =
                            fp.Product?.Category?.CategoryName
                            ?? "Uncategorized";


                        var isOrganic =
                            categoryName
                                .Contains(
                                    "organic",
                                    StringComparison.OrdinalIgnoreCase);


                        var rating =
                            reviewByProduct.TryGetValue(
                                fp.FarmerProductId,
                                out var review)
                                    ? (decimal)review.Rating
                                    : 0;


                        var reviewCount =
                            reviewByProduct.TryGetValue(
                                fp.FarmerProductId,
                                out var reviewData)
                                    ? reviewData.Count
                                    : 0;


                        var imageUrl =
                            primaryImages.TryGetValue(
                                fp.FarmerProductId,
                                out var image)
                                    ? image
                                    : fp.Product?.DefaultImageUrl;


                        var badge =
                            isOrganic
                                ? "Organic"
                                : available <= 5
                                    ? "Limited Stock"
                                    : "Fresh";


                        return new ProductListingViewModel
                        {
                            FarmerProductId =
                                fp.FarmerProductId,

                            ProductName =
                                fp.Product?.ProductName
                                ?? "Product",

                            CategoryName =
                                categoryName,

                            CategorySlug =
                                categoryName
                                    .ToLower()
                                    .Replace(" ", "-"),

                            FarmerName =
                                fp.Farmer?.BusinessName
                                ?? "Local Farmer",

                            MarketName =
                                inventory.FarmerMarket
                                    ?.Market
                                    ?.MarketName
                                ?? "Local Market",

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
                                imageUrl
                                ?? "/images/products/default-product.jpg",

                            Rating =
                                rating,

                            ReviewCount =
                                reviewCount,

                            BadgeText =
                                badge,

                            AvailabilityText =
                                available <= 5
                                    ? "Limited stock"
                                    : "Available",

                            IsOrganic =
                                isOrganic
                        };
                    })

                    .OrderByDescending(
                        p => p.AvailableQuantity)

                    .ThenBy(
                        p => p.ProductName)

                    .ToList();


            // =====================================================
            // 8. DYNAMIC CATEGORIES
            // =====================================================

            var categories =
                products

                    .GroupBy(
                        p => new
                        {
                            p.CategoryName,
                            p.CategorySlug
                        })

                    .Select(g =>
                        new ProductCategoryViewModel
                        {
                            CategoryName =
                                g.Key.CategoryName,

                            CategorySlug =
                                g.Key.CategorySlug
                        })

                    .OrderBy(
                        c => c.CategoryName)

                    .ToList();


            // =====================================================
            // 9. SEND DATA TO VIEW
            // =====================================================

            ViewBag.Categories = categories;


            return View(products);
        }


        // =========================================================
        // PRODUCT DETAILS
        // =========================================================

        [HttpGet]
        public async Task<IActionResult> Details(
            int id,
            CancellationToken cancellationToken)
        {
            // =========================================================
            // 1. GET FARMER PRODUCT
            // =========================================================

            var farmerProduct =
                await _context.farmerproducts
                    .AsNoTracking()

                    .Include(fp => fp.Farmer)

                    .Include(fp => fp.Product)
                        .ThenInclude(p => p!.Category)

                    .Include(fp => fp.UnitOfMeasure)

                    .FirstOrDefaultAsync(
                        fp =>
                            fp.FarmerProductId == id
                            &&
                            fp.IsApproved
                            &&
                            fp.IsActive
                            &&
                            fp.IsAvailable
                            &&
                            fp.Farmer != null
                            &&
                            fp.Farmer.IsApproved
                            &&
                            fp.Farmer.IsActive
                            &&
                            fp.Product != null
                            &&
                            fp.Product.IsActive,
                        cancellationToken);


            // =========================================================
            // 2. PRODUCT NOT FOUND
            // =========================================================

            if (farmerProduct == null)
            {
                return NotFound();
            }


            // =========================================================
            // 3. GET INVENTORY
            //
            // First today's available inventory.
            // If not found, get latest available inventory.
            // =========================================================

            var inventory =
                await _context.inventories
                    .AsNoTracking()

                    .Include(i => i.FarmerMarket)
                        .ThenInclude(fm => fm!.Market)

                    .Where(i =>
                        i.FarmerProductId ==
                        farmerProduct.FarmerProductId

                        &&
                        i.IsAvailable

                        &&
                        !i.IsSoldOut

                        &&
                        (
                            i.StockQuantity
                            -
                            i.ReservedQuantity
                            -
                            i.SoldQuantity
                        ) > 0
                    )

                    .OrderByDescending(i =>
                        i.InventoryDate.Date == DateTime.Today)

                    .ThenByDescending(i => i.UpdatedAt)

                    .FirstOrDefaultAsync(cancellationToken);


            // =========================================================
            // 4. AVAILABLE QUANTITY
            // =========================================================

            decimal availableQuantity = 0;

            if (inventory != null)
            {
                availableQuantity =
                    inventory.StockQuantity
                    -
                    inventory.ReservedQuantity
                    -
                    inventory.SoldQuantity;
            }


            // =========================================================
            // 5. PRIMARY PRODUCT IMAGE
            // =========================================================

            var productImage =
                await _context.productimages
                    .AsNoTracking()

                    .Where(pi =>
                        pi.FarmerProductId ==
                        farmerProduct.FarmerProductId)

                    .OrderByDescending(pi => pi.IsPrimary)

                    .ThenBy(pi => pi.SortOrder)

                    .FirstOrDefaultAsync(cancellationToken);


            var imageUrl =
                productImage?.ImageUrl
                ??
                farmerProduct.Product?.DefaultImageUrl
                ??
                "/images/products/default-product.jpg";


            // =========================================================
            // 6. REVIEWS
            // =========================================================

            var reviews =
                await _context.reviews
                    .AsNoTracking()

                    .Where(r =>
                        r.IsVisible
                        &&
                        r.FarmerProductId ==
                        farmerProduct.FarmerProductId)

                    .Select(r => r.Rating)

                    .ToListAsync(cancellationToken);


            decimal rating = 0;

            int reviewCount = reviews.Count;


            if (reviews.Count > 0)
            {
                rating =
                    (decimal)reviews.Average();
            }


            // =========================================================
            // 7. CATEGORY
            // =========================================================

            var categoryName =
                farmerProduct.Product?.Category?.CategoryName
                ??
                "Uncategorized";


            // =========================================================
            // 8. FARMER
            // =========================================================

            var farmerName =
                farmerProduct.Farmer?.BusinessName
                ??
                "Local Farmer";


            var farmerVerified =
                farmerProduct.Farmer != null
                &&
                farmerProduct.Farmer.IsApproved
                &&
                farmerProduct.Farmer.IsActive;


            // =========================================================
            // 9. PRICE
            //
            // Inventory price has priority.
            // If inventory price is zero, use FarmerProduct price.
            // =========================================================

            var price =
                inventory != null &&
                inventory.UnitPrice > 0
                    ? inventory.UnitPrice
                    : farmerProduct.Price;


            // =========================================================
            // 10. AVAILABILITY TEXT
            // =========================================================

            string availabilityText;

            if (availableQuantity <= 0)
            {
                availabilityText = "Out of Stock";
            }
            else if (availableQuantity <= 5)
            {
                availabilityText = "Limited Stock";
            }
            else
            {
                availabilityText = "In Stock";
            }


            // =========================================================
            // 11. MARKET
            // =========================================================

            int? farmerMarketId =
                inventory?.FarmerMarketId;

            int? marketId =
                inventory?.FarmerMarket?.MarketId;

            string marketName =
                inventory?.FarmerMarket?.Market?.MarketName
                ??
                "Local Market";


            // =========================================================
            // 12. RELATED PRODUCTS
            // =========================================================

            var relatedFarmerProducts =
                await _context.farmerproducts
                    .AsNoTracking()

                    .Include(fp => fp.Product)
                        .ThenInclude(p => p!.Category)

                    .Include(fp => fp.UnitOfMeasure)

                    .Where(fp =>
                        fp.FarmerProductId !=
                        farmerProduct.FarmerProductId

                        &&
                        fp.IsApproved

                        &&
                        fp.IsActive

                        &&
                        fp.IsAvailable

                        &&
                        fp.Farmer != null

                        &&
                        fp.Farmer.IsApproved

                        &&
                        fp.Farmer.IsActive

                        &&
                        fp.Product != null

                        &&
                        fp.Product.IsActive

                        &&
                        fp.Product.CategoryId ==
                        farmerProduct.Product!.CategoryId
                    )

                    .OrderByDescending(fp => fp.CreatedAt)

                    .Take(8)

                    .ToListAsync(cancellationToken);


            var relatedIds =
                relatedFarmerProducts
                    .Select(fp => fp.FarmerProductId)
                    .ToHashSet();


            // =========================================================
            // 13. RELATED INVENTORY
            // =========================================================

            var relatedInventories =
                await _context.inventories
                    .AsNoTracking()

                    .Where(i =>
                        relatedIds.Contains(
                            i.FarmerProductId)

                        &&
                        i.IsAvailable

                        &&
                        !i.IsSoldOut

                        &&
                        (
                            i.StockQuantity
                            -
                            i.ReservedQuantity
                            -
                            i.SoldQuantity
                        ) > 0
                    )

                    .OrderByDescending(i => i.UpdatedAt)

                    .ToListAsync(cancellationToken);


            var relatedInventoryByProduct =
                relatedInventories

                    .GroupBy(i =>
                        i.FarmerProductId)

                    .ToDictionary(
                        g => g.Key,
                        g => g.First()
                    );


            // =========================================================
            // 14. RELATED IMAGES
            // =========================================================

            var relatedImages =
                await _context.productimages
                    .AsNoTracking()

                    .Where(pi =>
                        relatedIds.Contains(
                            pi.FarmerProductId))

                    .OrderByDescending(pi =>
                        pi.IsPrimary)

                    .ThenBy(pi =>
                        pi.SortOrder)

                    .ToListAsync(cancellationToken);


            var relatedImageByProduct =
                relatedImages

                    .GroupBy(pi =>
                        pi.FarmerProductId)

                    .ToDictionary(
                        g => g.Key,
                        g => g.First().ImageUrl
                    );


            // =========================================================
            // 15. BUILD RELATED PRODUCTS
            // =========================================================

            var relatedProducts =
                relatedFarmerProducts

                    .Where(fp =>
                        relatedInventoryByProduct.ContainsKey(
                            fp.FarmerProductId))

                    .Select(fp =>
                    {
                        var relatedInventory =
                            relatedInventoryByProduct[
                                fp.FarmerProductId];


                        var relatedAvailable =
                            relatedInventory.StockQuantity
                            -
                            relatedInventory.ReservedQuantity
                            -
                            relatedInventory.SoldQuantity;


                        var relatedImage =
                            relatedImageByProduct.TryGetValue(
                                fp.FarmerProductId,
                                out var image)
                                    ? image
                                    : fp.Product?.DefaultImageUrl;


                        string badge;

                        if (relatedAvailable <= 5)
                        {
                            badge = "Limited Stock";
                        }
                        else
                        {
                            badge = "Fresh";
                        }


                        return new RelatedProductViewModel
                        {
                            FarmerProductId =
                                fp.FarmerProductId,

                            ProductName =
                                fp.Product?.ProductName
                                ??
                                "Product",

                            CategoryName =
                                fp.Product?.Category?.CategoryName
                                ??
                                "Product",

                            Price =
                                relatedInventory.UnitPrice > 0
                                    ? relatedInventory.UnitPrice
                                    : fp.Price,

                            UnitName =
                                fp.UnitOfMeasure?.UnitCode
                                ??
                                fp.UnitOfMeasure?.UnitName
                                ??
                                "unit",

                            ImageUrl =
                                relatedImage
                                ??
                                "/images/products/default-product.jpg",

                            AvailableQuantity =
                                relatedAvailable,

                            BadgeText =
                                badge
                        };
                    })

                    .Take(4)

                    .ToList();


            // =========================================================
            // 16. BUILD DETAILS VIEW MODEL
            // =========================================================

            var model =
                new ProductDetailsViewModel
                {
                    FarmerProductId =
                        farmerProduct.FarmerProductId,

                    ProductId =
                        farmerProduct.ProductId,

                    ProductName =
                        farmerProduct.Product?.ProductName
                        ??
                        "Product",

                    CategoryName =
                        categoryName,

                    Description =
                        farmerProduct.Product?.Description
                        ??
                        "",

                    FarmerDescription =
                        farmerProduct.FarmerDescription
                        ??
                        "",

                    ImageUrl =
                        imageUrl,

                    Price =
                        price,

                    UnitName =
                        farmerProduct.UnitOfMeasure?.UnitCode
                        ??
                        farmerProduct.UnitOfMeasure?.UnitName
                        ??
                        "unit",

                    AvailableQuantity =
                        availableQuantity,

                    AvailabilityText =
                        availabilityText,

                    IsAvailable =
                        availableQuantity > 0,

                    Rating =
                        rating,

                    ReviewCount =
                        reviewCount,

                    FarmerId =
                        farmerProduct.FarmerId,

                    FarmerName =
                        farmerName,

                    IsFarmerVerified =
                        farmerVerified,

                    FarmerDescriptionText =
                        farmerProduct.FarmerDescription
                        ??
                        "Local farmer providing fresh products.",

                    FarmerMarketId =
                        farmerMarketId,

                    MarketId =
                        marketId,

                    MarketName =
                        marketName,

                    RelatedProducts =
                        relatedProducts
                };


            // =========================================================
            // 17. RETURN VIEW
            // =========================================================

            return View(model);
        }
    }
}
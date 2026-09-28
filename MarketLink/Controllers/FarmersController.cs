using MarketLink.Models;
using MarketLink.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace MarketLink.Controllers
{
    public class FarmersController : Controller
    {
        private readonly ApplicationDbContext _context;

        public FarmersController(ApplicationDbContext context)
        {
            _context = context;
        }


        // =========================================================
        // FARMERS INDEX
        // =========================================================

        [HttpGet]
        public async Task<IActionResult> Index(
            CancellationToken cancellationToken)
        {
            // =====================================================
            // 1. GET ACTIVE + APPROVED FARMERS
            // =====================================================

            var farmers = await _context.farmers
                .AsNoTracking()

                .Where(f =>
                    f.IsApproved &&
                    f.IsActive)

                .OrderBy(f => f.BusinessName)

                .ToListAsync(cancellationToken);


            // =====================================================
            // 2. GET FARMER IDS
            // =====================================================

            var farmerIds = farmers
                .Select(f => f.FarmerId)
                .ToHashSet();


            // =====================================================
            // 3. GET FARMER PRODUCTS
            // =====================================================

            var farmerProducts = await _context.farmerproducts
                .AsNoTracking()

                .Where(fp =>
                    farmerIds.Contains(fp.FarmerId) &&
                    fp.IsApproved &&
                    fp.IsActive)

                .ToListAsync(cancellationToken);


            // =====================================================
            // 4. GET REVIEWS
            // =====================================================

            var farmerProductIds = farmerProducts
                .Select(fp => fp.FarmerProductId)
                .ToHashSet();


            var reviews = await _context.reviews
                .AsNoTracking()

                .Where(r =>
                    r.IsVisible &&
                    r.FarmerProductId != null &&
                    farmerProductIds.Contains(
                        r.FarmerProductId.Value))

                .ToListAsync(cancellationToken);


            // =====================================================
            // 5. BUILD FARMER LIST
            // =====================================================

            var farmerList = farmers
                .Select(f =>
                {
                    // Products belonging to this farmer
                    var products = farmerProducts
                        .Where(fp =>
                            fp.FarmerId == f.FarmerId)
                        .ToList();


                    // Reviews belonging to this farmer
                    var farmerProductIdList = products
                        .Select(p => p.FarmerProductId)
                        .ToHashSet();


                    var farmerReviews = reviews
                        .Where(r =>
                            r.FarmerProductId != null &&
                            farmerProductIdList.Contains(
                                r.FarmerProductId.Value))
                        .ToList();


                    // Rating
                    decimal rating = farmerReviews.Any()
                        ? (decimal)farmerReviews.Average(r => r.Rating)
                        : 0;


                    return new FarmerListViewModel
                    {
                        FarmerId = f.FarmerId,

                        BusinessName =
                            string.IsNullOrWhiteSpace(f.BusinessName)
                                ? "Local Farmer"
                                : f.BusinessName,

                        Description =
                            string.IsNullOrWhiteSpace(f.Description)
                                ? "Fresh quality products from a local farmer."
                                : f.Description,

                        Address =
                            string.IsNullOrWhiteSpace(f.Address)
                                ? "Local Market"
                                : f.Address,

                        ProfileImageUrl =
                            f.ProfileImageUrl,

                        IsApproved =
                            f.IsApproved,

                        Rating =
                            Math.Round(rating, 1),

                        ProductCount =
                            products.Count
                    };
                })

                .ToList();


            // =====================================================
            // 6. SEND TO VIEW
            // =====================================================

            return View(farmerList);
        }



        // =========================================================
        // FARMER DETAILS
        // =========================================================

        [HttpGet]
        public async Task<IActionResult> Details(
            int id,
            CancellationToken cancellationToken)
        {
            // =========================================================
            // 1. GET FARMER
            // =========================================================

            var farmer = await _context.farmers
                .AsNoTracking()
                .Include(f => f.User)
                .FirstOrDefaultAsync(
                    f =>
                        f.FarmerId == id &&
                        f.IsApproved &&
                        f.IsActive,
                    cancellationToken);


            // =========================================================
            // FARMER NOT FOUND
            // =========================================================

            if (farmer == null)
            {
                return NotFound();
            }


            // =========================================================
            // 2. GET FARMER PRODUCTS
            // =========================================================

            var farmerProducts = await _context.farmerproducts
                .AsNoTracking()

                .Include(fp => fp.Product)
                .ThenInclude(p => p.Category)

                .Include(fp => fp.UnitOfMeasure)

                .Where(fp =>
                    fp.FarmerId == id &&
                    fp.IsApproved &&
                    fp.IsActive)

                .ToListAsync(cancellationToken);


            // =========================================================
            // 3. GET PRODUCT IDS
            // =========================================================

            var farmerProductIds = farmerProducts
                .Select(fp => fp.FarmerProductId)
                .ToHashSet();


            // =========================================================
            // 4. GET FARMER REVIEWS
            // =========================================================

            var reviews = await _context.reviews
                .AsNoTracking()

                .Where(r =>
                    r.IsVisible &&
                    r.FarmerProductId != null &&
                    farmerProductIds.Contains(
                        r.FarmerProductId.Value))

                .ToListAsync(cancellationToken);


            // =========================================================
            // 5. CALCULATE RATING
            // =========================================================

            decimal rating = reviews.Any()
                ? Math.Round(
                    (decimal)reviews.Average(r => r.Rating),
                    1)
                : 0;


            // =========================================================
            // 6. BUILD PRODUCT LIST
            // =========================================================

            var products = farmerProducts
                .Select(fp => new FarmerDetailsProductViewModel
                {
                    FarmerProductId =
                        fp.FarmerProductId,

                    ProductId =
                        fp.ProductId,

                    ProductName =
                        fp.Product != null &&
                        !string.IsNullOrWhiteSpace(fp.Product.ProductName)
                            ? fp.Product.ProductName
                            : "Product",

                    CategoryName =
                        fp.Product != null &&
                        fp.Product.Category != null
                            ? fp.Product.Category.CategoryName
                            : "Fresh Produce",

                    Description =
                        !string.IsNullOrWhiteSpace(fp.FarmerDescription)
                            ? fp.FarmerDescription
                            : (
                                fp.Product != null
                                    ? fp.Product.Description ?? ""
                                    : ""
                            ),

                    ImageUrl =
                        fp.Product != null
                            ? fp.Product.DefaultImageUrl
                            : null,

                    Price =
                        fp.Price,

                    UnitName =
                        fp.UnitOfMeasure != null
                            ? fp.UnitOfMeasure.UnitName
                            : "",

                    IsAvailable =
                        fp.IsAvailable,

                    IsApproved =
                        fp.IsApproved,

                    IsActive =
                        fp.IsActive
                })
                .ToList();


            // =========================================================
            // 7. CUSTOMER FAVORITE STATE
            // =========================================================

            bool isFavorite = false;

            if (User.Identity?.IsAuthenticated == true &&
                int.TryParse(
                    User.FindFirstValue(ClaimTypes.NameIdentifier),
                    out var userId))
            {
                var customerId = await _context.customers
                    .AsNoTracking()
                    .Where(c => c.UserId == userId)
                    .Select(c => (int?)c.CustomerId)
                    .FirstOrDefaultAsync(cancellationToken);

                if (customerId.HasValue)
                {
                    isFavorite = await _context.favoritefarmers
                        .AsNoTracking()
                        .AnyAsync(
                            ff =>
                                ff.CustomerId == customerId.Value &&
                                ff.FarmerId == farmer.FarmerId,
                            cancellationToken);
                }
            }


            // =========================================================
            // 8. BUILD FARMER DETAILS VIEW MODEL
            // =========================================================

            var model = new FarmerDetailsViewModel
            {
                FarmerId =
                    farmer.FarmerId,

                BusinessName =
                    string.IsNullOrWhiteSpace(farmer.BusinessName)
                        ? "Local Farmer"
                        : farmer.BusinessName,

                Description =
                    string.IsNullOrWhiteSpace(farmer.Description)
                        ? "Fresh quality products from a local farmer."
                        : farmer.Description,

                Address =
                    string.IsNullOrWhiteSpace(farmer.Address)
                        ? "Local Market"
                        : farmer.Address,

                ProfileImageUrl =
                    farmer.ProfileImageUrl,

                ContactEmail =
                    farmer.User?.Email,

                IsFavorite =
                    isFavorite,

                IsApproved =
                    farmer.IsApproved,

                IsActive =
                    farmer.IsActive,

                Rating =
                    rating,

                ReviewCount =
                    reviews.Count,

                ProductCount =
                    products.Count,

                Products =
                    products
            };


            // =========================================================
            // 8. SEND MODEL TO VIEW
            // =========================================================

            return View(model);
        }



        // =========================================================
        // FARMER DASHBOARD
        // =========================================================

        [HttpGet]
        public IActionResult Dashboard()
        {
            return View();
        }
    }
}
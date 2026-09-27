using MarketLink.Models;
using MarketLink.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace MarketLink.Services
{
    public interface ICustomerRecommendationService
    {
        Task<CustomerRecommendationsPageViewModel> GenerateAsync(
            Customer customer,
            CancellationToken cancellationToken = default);
    }

    public class CustomerRecommendationService
        : ICustomerRecommendationService
    {
        private readonly ApplicationDbContext _context;

        public CustomerRecommendationService(
            ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<CustomerRecommendationsPageViewModel> GenerateAsync(
            Customer customer,
            CancellationToken cancellationToken = default)
        {
            var completedOrders = await _context.orders
                .AsNoTracking()
                .Where(o =>
                    o.CustomerId == customer.CustomerId &&
                    o.OrderStatus == "Completed")
                .ToListAsync(cancellationToken);

            var completedOrderIds = completedOrders
                .Select(o => o.OrderId)
                .ToList();

            var purchasedItems = await _context.orderitems
                .AsNoTracking()
                .Where(oi => completedOrderIds.Contains(oi.OrderId))
                .ToListAsync(cancellationToken);

            var favoriteProducts = await _context.favoriteproducts
                .AsNoTracking()
                .Where(fp => fp.CustomerId == customer.CustomerId)
                .ToListAsync(cancellationToken);

            var favoriteFarmerIds = await _context.favoritefarmers
                .AsNoTracking()
                .Where(ff => ff.CustomerId == customer.CustomerId)
                .Select(ff => ff.FarmerId)
                .ToListAsync(cancellationToken);

            var purchasedFarmerProductIds = purchasedItems
                .Select(x => x.FarmerProductId)
                .Distinct()
                .ToList();

            var signalFarmerProductIds = purchasedFarmerProductIds
                .Concat(
                    favoriteProducts.Select(x => x.FarmerProductId))
                .Distinct()
                .ToList();

            var signalListings = await _context.farmerproducts
                .AsNoTracking()
                .Where(fp =>
                    signalFarmerProductIds.Contains(fp.FarmerProductId))
                .ToListAsync(cancellationToken);

            var signalProductIds = signalListings
                .Select(fp => fp.ProductId)
                .Distinct()
                .ToList();

            var signalProducts = await _context.products
                .AsNoTracking()
                .Where(p => signalProductIds.Contains(p.ProductId))
                .ToDictionaryAsync(
                    p => p.ProductId,
                    cancellationToken);

            var categoryWeights = new Dictionary<int, decimal>();
            var farmerWeights = new Dictionary<int, decimal>();

            foreach (var item in purchasedItems)
            {
                var listing = signalListings
                    .FirstOrDefault(
                        x => x.FarmerProductId == item.FarmerProductId);

                if (listing == null)
                    continue;

                if (signalProducts.TryGetValue(
                        listing.ProductId,
                        out var product))
                {
                    AddWeight(
                        categoryWeights,
                        product.CategoryId,
                        Math.Max(1m, item.Quantity));
                }

                AddWeight(
                    farmerWeights,
                    listing.FarmerId,
                    1m);
            }

            foreach (var fav in favoriteProducts)
            {
                var listing = signalListings
                    .FirstOrDefault(
                        x => x.FarmerProductId == fav.FarmerProductId);

                if (listing == null)
                    continue;

                if (signalProducts.TryGetValue(
                        listing.ProductId,
                        out var product))
                {
                    AddWeight(
                        categoryWeights,
                        product.CategoryId,
                        2m);
                }

                AddWeight(
                    farmerWeights,
                    listing.FarmerId,
                    1.5m);
            }

            foreach (var farmerId in favoriteFarmerIds)
            {
                AddWeight(
                    farmerWeights,
                    farmerId,
                    2m);
            }

            var candidates = await _context.farmerproducts
                .AsNoTracking()
                .Where(fp =>
                    fp.IsApproved &&
                    fp.IsActive &&
                    fp.IsAvailable)
                .ToListAsync(cancellationToken);

            var activeFarmerIds = await _context.farmers
                .AsNoTracking()
                .Where(f =>
                    f.IsApproved &&
                    f.IsActive)
                .Select(f => f.FarmerId)
                .ToListAsync(cancellationToken);

            var activeFarmerSet = activeFarmerIds.ToHashSet();

            candidates = candidates
                .Where(fp =>
                    activeFarmerSet.Contains(fp.FarmerId))
                .ToList();

            var candidateProductIds = candidates
                .Select(fp => fp.ProductId)
                .Distinct()
                .ToList();

            var products = await _context.products
                .AsNoTracking()
                .Where(p =>
                    candidateProductIds.Contains(p.ProductId) &&
                    p.IsActive)
                .ToDictionaryAsync(
                    p => p.ProductId,
                    cancellationToken);

            var candidateCategoryIds = products.Values
                .Select(p => p.CategoryId)
                .Distinct()
                .ToList();

            var categories = await _context.categories
                .AsNoTracking()
                .Where(c => candidateCategoryIds.Contains(c.CategoryId))
                .ToDictionaryAsync(
                    c => c.CategoryId,
                    cancellationToken);

            var candidateFarmerIds = candidates
                .Select(fp => fp.FarmerId)
                .Distinct()
                .ToList();

            var farmers = await _context.farmers
                .AsNoTracking()
                .Where(f => candidateFarmerIds.Contains(f.FarmerId))
                .ToDictionaryAsync(
                    f => f.FarmerId,
                    cancellationToken);

            var unitIds = candidates
                .Select(fp => fp.UnitOfMeasureId)
                .Distinct()
                .ToList();

            var units = await _context.unitofmeasures
                .AsNoTracking()
                .Where(u => unitIds.Contains(u.UnitOfMeasureId))
                .ToDictionaryAsync(
                    u => u.UnitOfMeasureId,
                    cancellationToken);

            var candidateListingIds = candidates
                .Select(fp => fp.FarmerProductId)
                .ToList();

            var futureInventories = await _context.inventories
                .AsNoTracking()
                .Where(i =>
                    i.InventoryDate >= DateTime.Today &&
                    candidateListingIds.Contains(i.FarmerProductId) &&
                    i.IsAvailable &&
                    !i.IsSoldOut)
                .ToListAsync(cancellationToken);

            futureInventories = futureInventories
                .Where(i => i.AvailableQuantity > 0)
                .ToList();

            var availabilityByListing = futureInventories
                .GroupBy(i => i.FarmerProductId)
                .ToDictionary(
                    g => g.Key,
                    g => g.Sum(x => x.AvailableQuantity));

            candidates = candidates
                .Where(fp =>
                    availabilityByListing.ContainsKey(fp.FarmerProductId))
                .ToList();

            var popularCounts = await BuildPopularityAsync(
                candidates.Select(x => x.FarmerProductId).ToList(),
                cancellationToken);

            var maxCategoryWeight =
                categoryWeights.Count == 0
                    ? 1m
                    : categoryWeights.Values.Max();

            var maxFarmerWeight =
                farmerWeights.Count == 0
                    ? 1m
                    : farmerWeights.Values.Max();

            var maxPopularity =
                popularCounts.Count == 0
                    ? 1
                    : Math.Max(1, popularCounts.Values.Max());

            var favoriteSet = favoriteProducts
                .Select(x => x.FarmerProductId)
                .ToHashSet();

            var purchasedSet = purchasedFarmerProductIds.ToHashSet();

            var scored = new List<ScoredCandidate>();

            foreach (var listing in candidates)
            {
                if (!products.TryGetValue(
                        listing.ProductId,
                        out var product))
                {
                    continue;
                }

                decimal score = 0m;
                var reasons = new List<string>();

                if (categoryWeights.TryGetValue(
                        product.CategoryId,
                        out var categoryWeight))
                {
                    var component =
                        0.48m *
                        (categoryWeight / maxCategoryWeight);

                    score += component;

                    if (categories.TryGetValue(
                            product.CategoryId,
                            out var category))
                    {
                        reasons.Add(
                            $"Matches your interest in {category.CategoryName}");
                    }
                }

                if (farmerWeights.TryGetValue(
                        listing.FarmerId,
                        out var farmerWeight))
                {
                    score +=
                        0.22m *
                        (farmerWeight / maxFarmerWeight);

                    if (farmers.TryGetValue(
                            listing.FarmerId,
                            out var farmer))
                    {
                        reasons.Add(
                            $"From {farmer.BusinessName}, a farmer you have interacted with");
                    }
                }

                if (popularCounts.TryGetValue(
                        listing.FarmerProductId,
                        out var popularity))
                {
                    var popularityScore =
                        (decimal)popularity / maxPopularity;

                    score += 0.20m * popularityScore;

                    if (popularityScore >= 0.5m)
                        reasons.Add("Popular with MarketLink customers");
                }

                if (!purchasedSet.Contains(
                        listing.FarmerProductId))
                {
                    score += 0.07m;
                }

                if (favoriteSet.Contains(
                        listing.FarmerProductId))
                {
                    score += 0.03m;
                    reasons.Add("Already in your favorites");
                }

                score = Math.Min(1m, score);

                if (reasons.Count == 0)
                {
                    reasons.Add(
                        completedOrders.Count == 0
                            ? "Popular and currently available on MarketLink"
                            : "Available now and similar to current marketplace demand");
                }

                scored.Add(new ScoredCandidate
                {
                    Listing = listing,
                    Product = product,
                    Score = score,
                    Reason = string.Join(". ", reasons.Take(2)) + ".",
                    AvailableQuantity =
                        availabilityByListing[listing.FarmerProductId]
                });
            }

            // Cold-start fallback: rank available products by marketplace popularity.
            if (categoryWeights.Count == 0 &&
                farmerWeights.Count == 0)
            {
                scored = scored
                    .OrderByDescending(x =>
                        popularCounts.TryGetValue(
                            x.Listing.FarmerProductId,
                            out var count)
                            ? count
                            : 0)
                    .ThenByDescending(x => x.AvailableQuantity)
                    .ThenBy(x => x.Product.ProductName)
                    .ToList();

                for (var i = 0; i < scored.Count; i++)
                {
                    var popularity =
                        popularCounts.TryGetValue(
                            scored[i].Listing.FarmerProductId,
                            out var count)
                            ? count
                            : 0;

                    scored[i].Score =
                        Math.Min(
                            1m,
                            0.40m +
                            ((decimal)popularity / maxPopularity) * 0.45m);

                    scored[i].Reason =
                        "Popular and currently available on MarketLink.";
                }
            }
            else
            {
                scored = scored
                    .OrderByDescending(x => x.Score)
                    .ThenByDescending(x => x.AvailableQuantity)
                    .ThenBy(x => x.Product.ProductName)
                    .ToList();
            }

            var top = scored
                .Take(12)
                .ToList();

            await StoreRecommendationsAsync(
                customer.UserId,
                top,
                cancellationToken);

            var imageListingIds = top
                .Select(x => x.Listing.FarmerProductId)
                .ToList();

            var images = await _context.productimages
                .AsNoTracking()
                .Where(pi =>
                    imageListingIds.Contains(pi.FarmerProductId))
                .OrderByDescending(pi => pi.IsPrimary)
                .ThenBy(pi => pi.SortOrder)
                .ToListAsync(cancellationToken);

            var cards = top.Select(x =>
            {
                farmers.TryGetValue(
                    x.Listing.FarmerId,
                    out var farmer);

                categories.TryGetValue(
                    x.Product.CategoryId,
                    out var category);

                units.TryGetValue(
                    x.Listing.UnitOfMeasureId,
                    out var unit);

                var image = images
                    .FirstOrDefault(
                        img =>
                            img.FarmerProductId ==
                            x.Listing.FarmerProductId);

                return new CustomerRecommendationCardViewModel
                {
                    FarmerProductId = x.Listing.FarmerProductId,
                    ProductName = x.Product.ProductName,
                    FarmerName = farmer?.BusinessName ?? "Farmer",
                    CategoryName = category?.CategoryName ?? "Product",
                    UnitName = unit?.UnitCode ?? unit?.UnitName ?? "",
                    Price = x.Listing.Price,
                    AvailableQuantity = x.AvailableQuantity,
                    ImageUrl =
                        image?.ImageUrl ??
                        x.Product.DefaultImageUrl,
                    Score = x.Score,
                    Reason = x.Reason,
                    IsFavorite =
                        favoriteSet.Contains(
                            x.Listing.FarmerProductId)
                };
            }).ToList();

            return new CustomerRecommendationsPageViewModel
            {
                Recommendations = cards,
                CompletedOrders = completedOrders.Count,
                PurchasedProducts =
                    purchasedItems
                        .Select(x => x.FarmerProductId)
                        .Distinct()
                        .Count(),
                FavoriteProducts = favoriteProducts.Count,
                RecommendationMode =
                    categoryWeights.Count == 0 &&
                    farmerWeights.Count == 0
                        ? "Marketplace Discovery"
                        : "Personalized",
                GeneratedAt = DateTime.Now
            };
        }

        private async Task<Dictionary<int, int>> BuildPopularityAsync(
            List<int> candidateIds,
            CancellationToken cancellationToken)
        {
            var cutoff = DateTime.Today.AddDays(-90);

            var completedOrderIds = await _context.orders
                .AsNoTracking()
                .Where(o =>
                    o.OrderStatus == "Completed" &&
                    o.OrderDate >= cutoff)
                .Select(o => o.OrderId)
                .ToListAsync(cancellationToken);

            return await _context.orderitems
                .AsNoTracking()
                .Where(oi =>
                    completedOrderIds.Contains(oi.OrderId) &&
                    candidateIds.Contains(oi.FarmerProductId))
                .GroupBy(oi => oi.FarmerProductId)
                .Select(g => new
                {
                    FarmerProductId = g.Key,
                    Count = g.Select(x => x.OrderId).Distinct().Count()
                })
                .ToDictionaryAsync(
                    x => x.FarmerProductId,
                    x => x.Count,
                    cancellationToken);
        }

        private async Task StoreRecommendationsAsync(
            int userId,
            List<ScoredCandidate> recommendations,
            CancellationToken cancellationToken)
        {
            var old = await _context.airecommendations
                .Where(r =>
                    r.UserId == userId &&
                    r.RecommendationType == "CustomerProduct" &&
                    (!r.ExpiresAt.HasValue ||
                     r.ExpiresAt >= DateTime.Now))
                .ToListAsync(cancellationToken);

            var now = DateTime.Now;

            foreach (var item in old)
            {
                item.ExpiresAt = now;
            }

            foreach (var item in recommendations)
            {
                _context.airecommendations.Add(
                    new AiRecommendation
                    {
                        UserId = userId,
                        FarmerProductId =
                            item.Listing.FarmerProductId,
                        RecommendationType =
                            "CustomerProduct",
                        Score =
                            decimal.Round(
                                item.Score,
                                4),
                        Reason = item.Reason,
                        CreatedAt = now,
                        ExpiresAt = now.AddHours(24)
                    });
            }

            await _context.SaveChangesAsync(cancellationToken);
        }

        private static void AddWeight(
            Dictionary<int, decimal> dictionary,
            int key,
            decimal amount)
        {
            if (dictionary.ContainsKey(key))
                dictionary[key] += amount;
            else
                dictionary[key] = amount;
        }

        private class ScoredCandidate
        {
            public FarmerProduct Listing { get; set; } = null!;
            public Product Product { get; set; } = null!;
            public decimal Score { get; set; }
            public decimal AvailableQuantity { get; set; }
            public string Reason { get; set; } = "";
        }
    }
}

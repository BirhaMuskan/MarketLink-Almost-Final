using System.Text.Json;
using System.Text.RegularExpressions;
using MarketLink.Models;
using MarketLink.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace MarketLink.Services
{
    public interface IIntelligentSearchService
    {
        Task<CustomerSmartSearchPageViewModel> SearchAsync(
            string query,
            int? userId,
            CancellationToken cancellationToken = default);
    }

    public class IntelligentSearchService : IIntelligentSearchService
    {
        private static readonly string[] DayNames =
        {
            "Monday", "Tuesday", "Wednesday", "Thursday",
            "Friday", "Saturday", "Sunday"
        };

        private static readonly HashSet<string> StopWords =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "i", "me", "my", "we", "our",
                "need", "want", "looking",
                "for", "a", "an", "the", "some",
                "please", "find", "show", "get",
                "buy", "purchase",
                "with", "from", "at", "on", "in",
                "of", "to", "and", "or",

                "available", "availability",
                "stock", "fresh",
                "today", "tomorrow", "this", "next",

                "family", "dinner", "meal",

                "under", "below", "less", "than",
                "upto", "up", "maximum", "max",
                "budget", "rs", "pkr", "rupees",

                "product", "products",
                "item", "items",
                "farmer", "farmers",
                "market", "markets",
                "seller", "sellers",

                "favorite", "favorites",
                "favourite", "favourites",
                "saved", "preferred"
            };

        private readonly ApplicationDbContext _context;

        public IntelligentSearchService(
            ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<CustomerSmartSearchPageViewModel> SearchAsync(
            string query,
            int? userId,
            CancellationToken cancellationToken = default)
        {
            query = (query ?? "").Trim();

            var model = new CustomerSmartSearchPageViewModel
            {
                Query = query,
                HasSearched = true
            };

            if (string.IsNullOrWhiteSpace(query))
            {
                model.Interpretation =
                    new SmartSearchInterpretationViewModel
                    {
                        Summary =
                            "Type what you are looking for in normal language."
                    };

                return model;
            }

            var categories = await _context.categories
                .AsNoTracking()
                .Where(c => c.IsActive)
                .OrderBy(c => c.CategoryName)
                .ToListAsync(cancellationToken);

            var markets = await _context.markets
                .AsNoTracking()
                .Where(m => m.IsActive)
                .OrderByDescending(m => m.MarketName.Length)
                .ToListAsync(cancellationToken);

            var farmers = await _context.farmers
                .AsNoTracking()
                .Where(f => f.IsApproved && f.IsActive)
                .OrderByDescending(f => f.BusinessName.Length)
                .ToListAsync(cancellationToken);

            var products = await _context.products
                .AsNoTracking()
                .Where(p => p.IsActive)
                .ToListAsync(cancellationToken);

            var parsed = ParseQuery(
                query,
                categories,
                markets,
                farmers,
                products);

            model.Interpretation = parsed;

            Customer? customer = null;

            if (userId.HasValue)
            {
                customer = await _context.customers
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        c => c.UserId == userId.Value,
                        cancellationToken);
            }

            var favoriteFarmerIds = new HashSet<int>();
            var favoriteProductIds = new HashSet<int>();
            var favoriteMarketIds = new HashSet<int>();

            if (customer != null)
            {
                if (parsed.UseFavoriteFarmers)
                {
                    favoriteFarmerIds = (
                        await _context.favoritefarmers
                            .AsNoTracking()
                            .Where(x => x.CustomerId == customer.CustomerId)
                            .Select(x => x.FarmerId)
                            .ToListAsync(cancellationToken)
                    ).ToHashSet();
                }

                if (parsed.UseFavoriteProducts)
                {
                    favoriteProductIds = (
                        await _context.favoriteproducts
                            .AsNoTracking()
                            .Where(x => x.CustomerId == customer.CustomerId)
                            .Select(x => x.FarmerProductId)
                            .ToListAsync(cancellationToken)
                    ).ToHashSet();
                }

                if (parsed.UseFavoriteMarkets)
                {
                    favoriteMarketIds = (
                        await _context.favoritemarkets
                            .AsNoTracking()
                            .Where(x => x.CustomerId == customer.CustomerId)
                            .Select(x => x.MarketId)
                            .ToListAsync(cancellationToken)
                    ).ToHashSet();
                }
            }

            if (parsed.UseFavoriteFarmers &&
                favoriteFarmerIds.Count == 0)
            {
                model.NoResultsMessage =
                    "You asked for products from your favorite farmer, but you do not currently have any saved farmers.";

                await SaveSearchHistoryAsync(
                    userId,
                    query,
                    parsed,
                    0,
                    cancellationToken);

                return model;
            }

            if (parsed.UseFavoriteProducts &&
                favoriteProductIds.Count == 0)
            {
                model.NoResultsMessage =
                    "You asked for your favorite products, but you do not currently have any saved products.";

                await SaveSearchHistoryAsync(
                    userId,
                    query,
                    parsed,
                    0,
                    cancellationToken);

                return model;
            }

            if (parsed.UseFavoriteMarkets &&
                favoriteMarketIds.Count == 0)
            {
                model.NoResultsMessage =
                    "You asked for products at a saved market, but you do not currently have any saved markets.";

                await SaveSearchHistoryAsync(
                    userId,
                    query,
                    parsed,
                    0,
                    cancellationToken);

                return model;
            }

            var activeListings = await _context.farmerproducts
                .AsNoTracking()
                .Where(fp =>
                    fp.IsApproved &&
                    fp.IsActive &&
                    fp.IsAvailable)
                .ToListAsync(cancellationToken);

            var activeFarmerIds = farmers
                .Select(f => f.FarmerId)
                .ToHashSet();

            activeListings = activeListings
                .Where(fp =>
                    activeFarmerIds.Contains(fp.FarmerId))
                .ToList();

            if (parsed.UseFavoriteFarmers)
            {
                activeListings = activeListings
                    .Where(fp =>
                        favoriteFarmerIds.Contains(fp.FarmerId))
                    .ToList();
            }

            if (parsed.UseFavoriteProducts)
            {
                activeListings = activeListings
                    .Where(fp =>
                        favoriteProductIds.Contains(
                            fp.FarmerProductId))
                    .ToList();
            }

            if (parsed.MaxPrice.HasValue)
            {
                activeListings = activeListings
                    .Where(fp =>
                        fp.Price <= parsed.MaxPrice.Value)
                    .ToList();
            }

            if (!string.IsNullOrWhiteSpace(parsed.Farmer))
            {
                var farmerId = farmers
                    .FirstOrDefault(f =>
                        f.BusinessName.Equals(
                            parsed.Farmer,
                            StringComparison.OrdinalIgnoreCase))
                    ?.FarmerId;

                if (farmerId.HasValue)
                {
                    activeListings = activeListings
                        .Where(fp =>
                            fp.FarmerId == farmerId.Value)
                        .ToList();
                }
            }

            var productMap = products
                .ToDictionary(p => p.ProductId);

            if (!string.IsNullOrWhiteSpace(parsed.Category))
            {
                var categoryId = categories
                    .FirstOrDefault(c =>
                        c.CategoryName.Equals(
                            parsed.Category,
                            StringComparison.OrdinalIgnoreCase))
                    ?.CategoryId;

                if (categoryId.HasValue)
                {
                    activeListings = activeListings
                        .Where(fp =>
                            productMap.TryGetValue(
                                fp.ProductId,
                                out var p)
                            &&
                            p.CategoryId == categoryId.Value)
                        .ToList();
                }
            }

            if (!string.IsNullOrWhiteSpace(
                    parsed.ProductIntent))
            {
                var productIntent =
                    parsed.ProductIntent;

                activeListings = activeListings
                    .Where(fp =>
                        productMap.TryGetValue(
                            fp.ProductId,
                            out var p)
                        &&
                        (
                            p.ProductName.Contains(
                                productIntent,
                                StringComparison.OrdinalIgnoreCase)
                            ||
                            p.Description.Contains(
                                productIntent,
                                StringComparison.OrdinalIgnoreCase)
                        ))
                    .ToList();
            }
            else if (parsed.SearchTerms.Count > 0)
            {
                activeListings = activeListings
                    .Where(fp =>
                    {
                        if (!productMap.TryGetValue(
                                fp.ProductId,
                                out var p))
                        {
                            return false;
                        }

                        var text =
                            $"{p.ProductName} {p.Description}";

                        return parsed.SearchTerms.Any(term =>
                            text.Contains(
                                term,
                                StringComparison.OrdinalIgnoreCase));
                    })
                    .ToList();
            }

            var matchingListingCount =
                activeListings.Count;

            if (matchingListingCount == 0)
            {
                model.NoResultsMessage =
                    BuildNoListingMessage(parsed);

                await SaveSearchHistoryAsync(
                    userId,
                    query,
                    parsed,
                    0,
                    cancellationToken);

                return model;
            }

            var listingIds = activeListings
                .Select(fp => fp.FarmerProductId)
                .ToList();

            var inventories = await _context.inventories
                .AsNoTracking()
                .Where(i =>
                    listingIds.Contains(i.FarmerProductId) &&
                    i.InventoryDate >= DateTime.Today &&
                    i.IsAvailable &&
                    !i.IsSoldOut)
                .OrderBy(i => i.InventoryDate)
                .ToListAsync(cancellationToken);

            inventories = inventories
                .Where(i =>
                    i.AvailableQuantity > 0)
                .ToList();

            var farmerMarkets = await _context.farmermarkets
                .AsNoTracking()
                .Where(fm => fm.IsActive)
                .ToListAsync(cancellationToken);

            if (parsed.UseFavoriteMarkets)
            {
                var allowedFarmerMarketIds =
                    farmerMarkets
                        .Where(fm =>
                            favoriteMarketIds.Contains(
                                fm.MarketId))
                        .Select(fm =>
                            fm.FarmerMarketId)
                        .ToHashSet();

                inventories = inventories
                    .Where(i =>
                        allowedFarmerMarketIds.Contains(
                            i.FarmerMarketId))
                    .ToList();
            }

            if (!string.IsNullOrWhiteSpace(parsed.Market))
            {
                var marketId = markets
                    .FirstOrDefault(m =>
                        m.MarketName.Equals(
                            parsed.Market,
                            StringComparison.OrdinalIgnoreCase))
                    ?.MarketId;

                if (marketId.HasValue)
                {
                    var allowedFarmerMarketIds =
                        farmerMarkets
                            .Where(fm =>
                                fm.MarketId == marketId.Value)
                            .Select(fm =>
                                fm.FarmerMarketId)
                            .ToHashSet();

                    inventories = inventories
                        .Where(i =>
                            allowedFarmerMarketIds.Contains(
                                i.FarmerMarketId))
                        .ToList();
                }
            }

            if (!string.IsNullOrWhiteSpace(parsed.Day))
            {
                inventories = inventories
                    .Where(i =>
                        i.InventoryDate.DayOfWeek
                            .ToString()
                            .Equals(
                                parsed.Day,
                                StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            if (inventories.Count == 0)
            {
                model.NoResultsMessage =
                    BuildNoInventoryMessage(parsed);

                await SaveSearchHistoryAsync(
                    userId,
                    query,
                    parsed,
                    0,
                    cancellationToken);

                return model;
            }

            var unitIds = activeListings
                .Select(fp => fp.UnitOfMeasureId)
                .Distinct()
                .ToList();

            var units = await _context.unitofmeasures
                .AsNoTracking()
                .Where(u =>
                    unitIds.Contains(
                        u.UnitOfMeasureId))
                .ToDictionaryAsync(
                    u => u.UnitOfMeasureId,
                    cancellationToken);

            var categoryMap = categories
                .ToDictionary(c => c.CategoryId);

            var farmerMap = farmers
                .ToDictionary(f => f.FarmerId);

            var marketMap = markets
                .ToDictionary(m => m.MarketId);

            var farmerMarketMap = farmerMarkets
                .ToDictionary(fm => fm.FarmerMarketId);

            var imageListingIds = activeListings
                .Select(fp => fp.FarmerProductId)
                .ToList();

            var images = await _context.productimages
                .AsNoTracking()
                .Where(pi =>
                    imageListingIds.Contains(
                        pi.FarmerProductId))
                .OrderByDescending(pi => pi.IsPrimary)
                .ThenBy(pi => pi.SortOrder)
                .ToListAsync(cancellationToken);

            var results =
                new List<SmartSearchProductViewModel>();

            foreach (var listing in activeListings)
            {
                if (!productMap.TryGetValue(
                        listing.ProductId,
                        out var product))
                {
                    continue;
                }

                if (!farmerMap.TryGetValue(
                        listing.FarmerId,
                        out var farmer))
                {
                    continue;
                }

                var listingInventories =
                    inventories
                        .Where(i =>
                            i.FarmerProductId ==
                            listing.FarmerProductId)
                        .ToList();

                foreach (var inventory in listingInventories)
                {
                    if (!farmerMarketMap.TryGetValue(
                            inventory.FarmerMarketId,
                            out var farmerMarket))
                    {
                        continue;
                    }

                    if (!marketMap.TryGetValue(
                            farmerMarket.MarketId,
                            out var market))
                    {
                        continue;
                    }

                    categoryMap.TryGetValue(
                        product.CategoryId,
                        out var category);

                    units.TryGetValue(
                        listing.UnitOfMeasureId,
                        out var unit);

                    var score = CalculateScore(
                        parsed,
                        listing,
                        product,
                        farmer,
                        market,
                        inventory,
                        category?.CategoryName);

                    var reason = BuildMatchReason(
                        parsed,
                        product,
                        farmer,
                        market,
                        inventory,
                        category?.CategoryName);

                    var image = images
                        .FirstOrDefault(img =>
                            img.FarmerProductId ==
                            listing.FarmerProductId);

                    results.Add(
                        new SmartSearchProductViewModel
                        {
                            FarmerProductId =
                                listing.FarmerProductId,
                            ProductName =
                                product.ProductName,
                            FarmerName =
                                farmer.BusinessName,
                            CategoryName =
                                category?.CategoryName ??
                                "Product",
                            UnitName =
                                unit?.UnitCode ??
                                unit?.UnitName ??
                                "",
                            Price =
                                inventory.UnitPrice > 0
                                    ? inventory.UnitPrice
                                    : listing.Price,
                            AvailableQuantity =
                                inventory.AvailableQuantity,
                            MarketName =
                                market.MarketName,
                            MarketAddress =
                                market.Address,
                            InventoryDate =
                                inventory.InventoryDate,
                            ImageUrl =
                                image?.ImageUrl ??
                                product.DefaultImageUrl,
                            MatchScore =
                                decimal.Round(
                                    score,
                                    4),
                            MatchReason =
                                reason
                        });
                }
            }

            results = results
                .OrderByDescending(x =>
                    x.MatchScore)
                .ThenBy(x =>
                    x.Price)
                .ThenBy(x =>
                    x.InventoryDate)
                .Take(30)
                .ToList();

            model.Results = results;
            model.ResultCount = results.Count;

            if (results.Count == 0)
            {
                model.NoResultsMessage =
                    BuildNoInventoryMessage(parsed);
            }

            await SaveSearchHistoryAsync(
                userId,
                query,
                parsed,
                results.Count,
                cancellationToken);

            return model;
        }

        private static SmartSearchInterpretationViewModel ParseQuery(
            string query,
            List<Category> categories,
            List<Market> markets,
            List<Farmer> farmers,
            List<Product> products)
        {
            var parsed =
                new SmartSearchInterpretationViewModel();

            var normalized =
                Regex.Replace(
                    query.ToLowerInvariant(),
                    @"\s+",
                    " ")
                .Trim();

            parsed.UseFavoriteFarmers =
                Regex.IsMatch(
                    normalized,
                    @"\b(my\s+)?(favorite|favourite|saved|preferred)\s+farmer(s)?\b",
                    RegexOptions.IgnoreCase);

            parsed.UseFavoriteProducts =
                Regex.IsMatch(
                    normalized,
                    @"\b(my\s+)?(favorite|favourite|saved)\s+(product|products|item|items)\b",
                    RegexOptions.IgnoreCase);

            parsed.UseFavoriteMarkets =
                Regex.IsMatch(
                    normalized,
                    @"\b(my\s+)?(favorite|favourite|saved|preferred)\s+market(s)?\b",
                    RegexOptions.IgnoreCase);

            parsed.MaxPrice =
                ExtractMaxPrice(normalized);

            parsed.Day = DayNames
                .FirstOrDefault(day =>
                    Regex.IsMatch(
                        normalized,
                        $@"\b{Regex.Escape(
                            day.ToLowerInvariant())}\b",
                        RegexOptions.IgnoreCase));

            parsed.Market = markets
                .FirstOrDefault(m =>
                    ContainsPhrase(
                        normalized,
                        m.MarketName))
                ?.MarketName;

            parsed.Farmer = farmers
                .FirstOrDefault(f =>
                    ContainsPhrase(
                        normalized,
                        f.BusinessName))
                ?.BusinessName;

            var category = categories
                .FirstOrDefault(c =>
                    ContainsFlexiblePhrase(
                        normalized,
                        c.CategoryName));

            if (category != null)
            {
                parsed.Category =
                    category.CategoryName;
            }

            var product = products
                .OrderByDescending(p =>
                    p.ProductName.Length)
                .FirstOrDefault(p =>
                    ContainsFlexiblePhrase(
                        normalized,
                        p.ProductName));

            if (product != null)
            {
                parsed.ProductIntent =
                    product.ProductName;
            }

            parsed.AvailableOnly =
                !Regex.IsMatch(
                    normalized,
                    @"\b(include|show)\s+(sold\s*out|unavailable)\b",
                    RegexOptions.IgnoreCase);

            parsed.SearchTerms =
                ExtractSearchTerms(
                    normalized,
                    parsed,
                    categories,
                    markets,
                    farmers);

            var understood =
                new List<string>();

            if (!string.IsNullOrWhiteSpace(
                    parsed.ProductIntent))
            {
                understood.Add(
                    $"product: {parsed.ProductIntent}");
            }

            if (!string.IsNullOrWhiteSpace(
                    parsed.Category))
            {
                understood.Add(
                    $"category: {parsed.Category}");
            }

            if (parsed.MaxPrice.HasValue)
            {
                understood.Add(
                    $"max price: Rs. {parsed.MaxPrice.Value:N0}");
            }

            if (!string.IsNullOrWhiteSpace(
                    parsed.Day))
            {
                understood.Add(
                    $"day: {parsed.Day}");
            }

            if (!string.IsNullOrWhiteSpace(
                    parsed.Market))
            {
                understood.Add(
                    $"market: {parsed.Market}");
            }

            if (!string.IsNullOrWhiteSpace(
                    parsed.Farmer))
            {
                understood.Add(
                    $"farmer: {parsed.Farmer}");
            }

            if (parsed.UseFavoriteFarmers)
            {
                understood.Add(
                    "your favorite farmer(s)");
            }

            if (parsed.UseFavoriteProducts)
            {
                understood.Add(
                    "your favorite products");
            }

            if (parsed.UseFavoriteMarkets)
            {
                understood.Add(
                    "your saved market(s)");
            }

            if (parsed.AvailableOnly)
            {
                understood.Add(
                    "available stock only");
            }

            if (parsed.SearchTerms.Count > 0 &&
                string.IsNullOrWhiteSpace(
                    parsed.ProductIntent))
            {
                understood.Add(
                    "keywords: " +
                    string.Join(
                        ", ",
                        parsed.SearchTerms.Take(4)));
            }

            parsed.Summary =
                understood.Count == 0
                    ? "MarketLink will search currently available products using your words."
                    : "I understood " +
                      string.Join(
                          " · ",
                          understood) +
                      ".";

            return parsed;
        }

        private static decimal? ExtractMaxPrice(
            string query)
        {
            var patterns = new[]
            {
                @"(?:under|below|less\s+than|up\s*to|upto|max(?:imum)?|budget(?:\s+of)?)[^\d]{0,12}(?:rs\.?|pkr|rupees?)?\s*([\d,]+(?:\.\d{1,2})?)",
                @"(?:rs\.?|pkr|rupees?)\s*([\d,]+(?:\.\d{1,2})?)\s*(?:or\s+less|maximum|max|budget)?"
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(
                    query,
                    pattern,
                    RegexOptions.IgnoreCase);

                if (!match.Success)
                    continue;

                var number =
                    match.Groups[1]
                        .Value
                        .Replace(",", "");

                if (decimal.TryParse(
                        number,
                        out var value)
                    &&
                    value >= 0)
                {
                    return value;
                }
            }

            return null;
        }

        private static List<string> ExtractSearchTerms(
            string normalized,
            SmartSearchInterpretationViewModel parsed,
            List<Category> categories,
            List<Market> markets,
            List<Farmer> farmers)
        {
            var cleaned = normalized;

            foreach (var phrase in categories
                .Select(x => x.CategoryName)
                .Concat(
                    markets.Select(x => x.MarketName))
                .Concat(
                    farmers.Select(x => x.BusinessName))
                .Where(x =>
                    !string.IsNullOrWhiteSpace(x)))
            {
                cleaned = Regex.Replace(
                    cleaned,
                    Regex.Escape(phrase),
                    " ",
                    RegexOptions.IgnoreCase);
            }

            cleaned = Regex.Replace(
                cleaned,
                @"\b(my\s+)?(favorite|favourite|saved|preferred)\s+(farmer|farmers|product|products|item|items|market|markets)\b",
                " ",
                RegexOptions.IgnoreCase);

            cleaned = Regex.Replace(
                cleaned,
                @"(?:rs\.?|pkr|rupees?)?\s*[\d,]+(?:\.\d{1,2})?",
                " ",
                RegexOptions.IgnoreCase);

            cleaned = Regex.Replace(
                cleaned,
                @"[^a-z0-9\s-]",
                " ",
                RegexOptions.IgnoreCase);

            return cleaned
                .Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x =>
                    x.Length >= 3 &&
                    !StopWords.Contains(x) &&
                    !DayNames.Contains(
                        x,
                        StringComparer.OrdinalIgnoreCase))
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .ToList();
        }

        private static decimal CalculateScore(
            SmartSearchInterpretationViewModel parsed,
            FarmerProduct listing,
            Product product,
            Farmer farmer,
            Market market,
            Inventory inventory,
            string? categoryName)
        {
            decimal score = 0.10m;

            if (!string.IsNullOrWhiteSpace(
                    parsed.ProductIntent)
                &&
                product.ProductName.Contains(
                    parsed.ProductIntent,
                    StringComparison.OrdinalIgnoreCase))
            {
                score += 0.36m;
            }

            if (!string.IsNullOrWhiteSpace(
                    parsed.Category)
                &&
                string.Equals(
                    categoryName,
                    parsed.Category,
                    StringComparison.OrdinalIgnoreCase))
            {
                score += 0.16m;
            }

            if (!string.IsNullOrWhiteSpace(
                    parsed.Market)
                &&
                market.MarketName.Equals(
                    parsed.Market,
                    StringComparison.OrdinalIgnoreCase))
            {
                score += 0.10m;
            }

            if (!string.IsNullOrWhiteSpace(
                    parsed.Farmer)
                &&
                farmer.BusinessName.Equals(
                    parsed.Farmer,
                    StringComparison.OrdinalIgnoreCase))
            {
                score += 0.16m;
            }

            if (parsed.UseFavoriteFarmers)
                score += 0.12m;

            if (parsed.UseFavoriteProducts)
                score += 0.18m;

            if (parsed.UseFavoriteMarkets)
                score += 0.10m;

            if (!string.IsNullOrWhiteSpace(
                    parsed.Day)
                &&
                inventory.InventoryDate.DayOfWeek
                    .ToString()
                    .Equals(
                        parsed.Day,
                        StringComparison.OrdinalIgnoreCase))
            {
                score += 0.08m;
            }

            if (parsed.MaxPrice.HasValue)
            {
                var price =
                    inventory.UnitPrice > 0
                        ? inventory.UnitPrice
                        : listing.Price;

                if (price <=
                    parsed.MaxPrice.Value)
                {
                    score += 0.06m;

                    if (parsed.MaxPrice.Value > 0)
                    {
                        var valueScore =
                            1m -
                            Math.Min(
                                1m,
                                price /
                                parsed.MaxPrice.Value);

                        score +=
                            valueScore *
                            0.03m;
                    }
                }
            }

            var searchable =
                $"{product.ProductName} {product.Description} " +
                $"{farmer.BusinessName} {market.MarketName} {categoryName}";

            var matchedTerms =
                parsed.SearchTerms.Count(term =>
                    searchable.Contains(
                        term,
                        StringComparison.OrdinalIgnoreCase));

            if (parsed.SearchTerms.Count > 0)
            {
                score +=
                    0.10m *
                    ((decimal)matchedTerms /
                     parsed.SearchTerms.Count);
            }

            if (inventory.AvailableQuantity > 0)
                score += 0.03m;

            return Math.Min(
                1m,
                score);
        }

        private static string BuildMatchReason(
            SmartSearchInterpretationViewModel parsed,
            Product product,
            Farmer farmer,
            Market market,
            Inventory inventory,
            string? categoryName)
        {
            var reasons =
                new List<string>();

            if (!string.IsNullOrWhiteSpace(
                    parsed.ProductIntent))
            {
                reasons.Add(
                    $"matches {product.ProductName}");
            }

            if (!string.IsNullOrWhiteSpace(
                    parsed.Category)
                &&
                string.Equals(
                    categoryName,
                    parsed.Category,
                    StringComparison.OrdinalIgnoreCase))
            {
                reasons.Add(
                    $"in {categoryName}");
            }

            if (!string.IsNullOrWhiteSpace(
                    parsed.Market))
            {
                reasons.Add(
                    $"at {market.MarketName}");
            }

            if (!string.IsNullOrWhiteSpace(
                    parsed.Farmer))
            {
                reasons.Add(
                    $"sold by {farmer.BusinessName}");
            }

            if (parsed.UseFavoriteFarmers)
            {
                reasons.Add(
                    $"{farmer.BusinessName} is in your favorite farmers");
            }

            if (parsed.UseFavoriteProducts)
            {
                reasons.Add(
                    "saved in your favorite products");
            }

            if (parsed.UseFavoriteMarkets)
            {
                reasons.Add(
                    $"{market.MarketName} is one of your saved markets");
            }

            if (!string.IsNullOrWhiteSpace(
                    parsed.Day))
            {
                reasons.Add(
                    $"available {inventory.InventoryDate:dddd}");
            }

            if (parsed.MaxPrice.HasValue)
            {
                reasons.Add(
                    $"within Rs. {parsed.MaxPrice.Value:N0} limit");
            }

            if (reasons.Count == 0)
            {
                reasons.Add(
                    "matches your search and is currently available");
            }

            return char.ToUpperInvariant(
                       reasons[0][0]) +
                   reasons[0][1..] +
                   (reasons.Count > 1
                       ? " · " +
                         string.Join(
                             " · ",
                             reasons.Skip(1))
                       : "") +
                   ".";
        }

        private static string BuildNoListingMessage(
            SmartSearchInterpretationViewModel parsed)
        {
            if (!string.IsNullOrWhiteSpace(
                    parsed.Farmer))
            {
                return
                    $"{parsed.Farmer} was found, but no active product listing matched the rest of your search.";
            }

            if (parsed.UseFavoriteFarmers)
            {
                return
                    "Your favorite farmer(s) were found, but none of their active product listings matched the rest of your search.";
            }

            if (parsed.UseFavoriteProducts)
            {
                return
                    "Your saved products were found, but none currently match the other conditions in your search.";
            }

            return
                "No active product listing matched all of the conditions MarketLink understood from your search.";
        }

        private static string BuildNoInventoryMessage(
            SmartSearchInterpretationViewModel parsed)
        {
            if (!string.IsNullOrWhiteSpace(
                    parsed.Farmer))
            {
                return
                    $"{parsed.Farmer} has matching products listed, but MarketLink could not find currently available dated stock for your search.";
            }

            if (parsed.UseFavoriteFarmers)
            {
                return
                    "Your favorite farmer(s) have matching products listed, but there is no currently available dated stock matching this search.";
            }

            if (parsed.UseFavoriteProducts)
            {
                return
                    "Your favorite products exist, but they do not currently have available dated stock matching this search.";
            }

            if (parsed.UseFavoriteMarkets)
            {
                return
                    "Your saved market(s) were found, but no currently available dated stock matched this search.";
            }

            if (!string.IsNullOrWhiteSpace(
                    parsed.Day))
            {
                return
                    $"Matching products exist, but no available inventory was found for {parsed.Day}.";
            }

            return
                "Matching product listings exist, but no current or future inventory with available stock was found.";
        }

        private async Task SaveSearchHistoryAsync(
            int? userId,
            string query,
            SmartSearchInterpretationViewModel parsed,
            int resultCount,
            CancellationToken cancellationToken)
        {
            var filters =
                JsonSerializer.Serialize(
                    new
                    {
                        parsed.ProductIntent,
                        parsed.Category,
                        parsed.MaxPrice,
                        parsed.Day,
                        parsed.Market,
                        parsed.Farmer,
                        parsed.UseFavoriteFarmers,
                        parsed.UseFavoriteProducts,
                        parsed.UseFavoriteMarkets,
                        parsed.AvailableOnly,
                        parsed.SearchTerms
                    });

            _context.searchhistories.Add(
                new SearchHistory
                {
                    UserId = userId,
                    SearchText =
                        query.Length <= 300
                            ? query
                            : query[..300],
                    FiltersJson = filters,
                    ResultCount = resultCount,
                    SearchedAt = DateTime.Now
                });

            await _context.SaveChangesAsync(
                cancellationToken);
        }

        private static bool ContainsPhrase(
            string normalizedQuery,
            string phrase)
        {
            if (string.IsNullOrWhiteSpace(phrase))
                return false;

            return normalizedQuery.Contains(
                phrase.ToLowerInvariant(),
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool ContainsFlexiblePhrase(
            string normalizedQuery,
            string phrase)
        {
            if (ContainsPhrase(
                    normalizedQuery,
                    phrase))
            {
                return true;
            }

            var p =
                phrase
                    .Trim()
                    .ToLowerInvariant();

            if (p.EndsWith("ies"))
            {
                var singular =
                    p[..^3] + "y";

                if (ContainsPhrase(
                        normalizedQuery,
                        singular))
                {
                    return true;
                }
            }

            if (p.EndsWith("s"))
            {
                var singular =
                    p[..^1];

                if (ContainsPhrase(
                        normalizedQuery,
                        singular))
                {
                    return true;
                }
            }
            else
            {
                var plural =
                    p + "s";

                if (ContainsPhrase(
                        normalizedQuery,
                        plural))
                {
                    return true;
                }
            }

            return false;
        }
    }
}

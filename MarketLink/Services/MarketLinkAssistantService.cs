using System.Text.Json;
using System.Text.RegularExpressions;
using MarketLink.Models;
using MarketLink.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace MarketLink.Services
{
    public interface IMarketLinkAssistantService
    {
        Task<MarketLinkAssistantReply> AskAsync(
            int? userId,
            int? conversationId,
            string question,
            CancellationToken cancellationToken = default);
    }

    public sealed class MarketLinkAssistantService : IMarketLinkAssistantService
    {
        private readonly ApplicationDbContext _context;
        private readonly IIntelligentSearchService _smartSearch;
        private readonly IGroqAssistantService _groq;
        private readonly IAssistantActionService _actionService;
        private readonly ILogger<MarketLinkAssistantService> _logger;

        public MarketLinkAssistantService(
            ApplicationDbContext context,
            IIntelligentSearchService smartSearch,
            IGroqAssistantService groq,
            IAssistantActionService actionService,
            ILogger<MarketLinkAssistantService> logger)
        {
            _context = context;
            _smartSearch = smartSearch;
            _groq = groq;
            _actionService = actionService;
            _logger = logger;
        }

        public async Task<MarketLinkAssistantReply> AskAsync(
            int? userId,
            int? conversationId,
            string question,
            CancellationToken cancellationToken = default)
        {
            question = (question ?? "").Trim();

            if (string.IsNullOrWhiteSpace(question))
            {
                return new MarketLinkAssistantReply
                {
                    Intent = "Empty",
                    Text = "Ask me about products, farmers, markets, availability or pickup times."
                };
            }

            var recentConversation =
                await GetRecentConversationAsync(
                    userId,
                    conversationId,
                    cancellationToken);

            var farmers = await _context.farmers
                .AsNoTracking()
                .Where(f => f.IsActive && f.IsApproved)
                .OrderBy(f => f.BusinessName)
                .ToListAsync(cancellationToken);

            var markets = await _context.markets
                .AsNoTracking()
                .Where(m => m.IsActive)
                .OrderBy(m => m.MarketName)
                .ToListAsync(cancellationToken);

            var products = await _context.products
                .AsNoTracking()
                .Where(p => p.IsActive)
                .OrderBy(p => p.ProductName)
                .ToListAsync(cancellationToken);

            var interpretation =
                await _groq.InterpretAsync(
                    question,
                    recentConversation,
                    farmers.Select(f => f.BusinessName).ToList(),
                    markets.Select(m => m.MarketName).ToList(),
                    products.Select(p => p.ProductName).ToList(),
                    cancellationToken)
                ?? BuildLocalInterpretation(
                    question,
                    recentConversation,
                    farmers,
                    markets,
                    products);

            try
            {
                // Real account-changing actions are executed by MarketLink first.
                // Their confirmation text is returned exactly as the backend reports it;
                // Groq is never allowed to "pretend" an action succeeded.
                var executedAction =
                    await _actionService.TryExecuteAsync(
                        userId,
                        interpretation,
                        cancellationToken);

                if (executedAction != null)
                {
                    executedAction.Metadata["groq"] =
                        _groq.IsConfigured
                            ? "enabled"
                            : "fallback";

                    return executedAction;
                }

                var grounded = interpretation.Intent switch
                {
                    "FarmerLookup" =>
                        await AnswerFarmerLookupAsync(
                            interpretation,
                            farmers,
                            cancellationToken),

                    "FarmerProducts" =>
                        await AnswerFarmerProductsAsync(
                            interpretation,
                            farmers,
                            products,
                            cancellationToken),

                    "MarketHours" =>
                        await AnswerMarketHoursAsync(
                            interpretation,
                            markets,
                            cancellationToken),

                    "MarketLookup" =>
                        await AnswerMarketLookupAsync(
                            interpretation,
                            markets,
                            cancellationToken),

                    "PickupSlots" =>
                        await AnswerPickupSlotsAsync(
                            interpretation,
                            farmers,
                            markets,
                            cancellationToken),

                    "FarmerAvailability" =>
                        await AnswerFarmerAvailabilityAsync(
                            interpretation,
                            farmers,
                            markets,
                            cancellationToken),

                    "ProductPrice" =>
                        await AnswerProductSearchAsync(
                            userId,
                            interpretation,
                            question,
                            "ProductPrice",
                            cancellationToken),

                    "Favorites" =>
                        await AnswerProductSearchAsync(
                            userId,
                            interpretation,
                            question,
                            "Favorites",
                            cancellationToken),

                    "ProductSearch" =>
                        await AnswerProductSearchAsync(
                            userId,
                            interpretation,
                            question,
                            "ProductSearch",
                            cancellationToken),

                    _ =>
                        await AnswerUnknownAsync(
                            userId,
                            interpretation,
                            question,
                            cancellationToken)
                };

                var polished =
                    await _groq.PolishAnswerAsync(
                        question,
                        grounded.VerifiedFacts,
                        recentConversation,
                        cancellationToken);

                if (!string.IsNullOrWhiteSpace(polished))
                {
                    grounded.Reply.Text = polished!;
                }

                grounded.Reply.Metadata["groq"] =
                    _groq.IsConfigured
                        ? "enabled"
                        : "fallback";

                grounded.Reply.Metadata["intent"] =
                    grounded.Reply.Intent;

                return grounded.Reply;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "MarketLink assistant failed for question: {Question}",
                    question);

                return new MarketLinkAssistantReply
                {
                    Intent = "Error",
                    Text =
                        "I couldn't complete that lookup just now. Please try again, or ask me about a specific product, farmer, market, or pickup time."
                };
            }
        }

        private async Task<GroundedAssistantAnswer> AnswerFarmerLookupAsync(
            GroqIntentResult intent,
            List<Farmer> farmers,
            CancellationToken cancellationToken)
        {
            var farmer =
                ResolveFarmer(intent.Farmer, farmers);

            if (farmer == null)
            {
                return Ground(
                    "FarmerLookup",
                    "I couldn't find a matching active farmer.",
                    """
No matching active, approved farmer was found in MarketLink for the requested name.
""");
            }

            var activeListingCount =
                await _context.farmerproducts
                    .AsNoTracking()
                    .CountAsync(
                        fp =>
                            fp.FarmerId == farmer.FarmerId &&
                            fp.IsActive &&
                            fp.IsApproved &&
                            fp.IsAvailable,
                        cancellationToken);

            var facts = $"""
Farmer exists: yes
Farmer name: {farmer.BusinessName}
Farmer description: {farmer.Description}
Farmer address: {farmer.Address}
Active approved available product listings: {activeListingCount}
""";

            return Ground(
                "FarmerLookup",
                $"Yes, {farmer.BusinessName} is an active farmer on MarketLink.",
                facts,
                new CustomerAssistantActionViewModel
                {
                    Label = "View Farmers",
                    Controller = "CustomerFarmers",
                    Action = "Index"
                },
                metadata: new Dictionary<string, object?>
                {
                    ["farmer"] = farmer.BusinessName
                });
        }

        private async Task<GroundedAssistantAnswer> AnswerFarmerProductsAsync(
            GroqIntentResult intent,
            List<Farmer> farmers,
            List<Product> products,
            CancellationToken cancellationToken)
        {
            var farmer =
                ResolveFarmer(intent.Farmer, farmers);

            if (farmer == null)
            {
                return Ground(
                    "FarmerProducts",
                    "I need the farmer's name before I can check their products.",
                    """
No farmer could be resolved from the current question and recent conversation.
""");
            }

            var listings = await _context.farmerproducts
                .AsNoTracking()
                .Where(fp =>
                    fp.FarmerId == farmer.FarmerId &&
                    fp.IsActive &&
                    fp.IsApproved)
                .OrderBy(fp => fp.ProductId)
                .ToListAsync(cancellationToken);

            if (listings.Count == 0)
            {
                return Ground(
                    "FarmerProducts",
                    $"{farmer.BusinessName} is on MarketLink, but does not currently have any active approved product listings.",
                    $"""
Farmer name: {farmer.BusinessName}
Farmer exists: yes
Active approved product listings: 0
""",
                    metadata: new Dictionary<string, object?>
                    {
                        ["farmer"] = farmer.BusinessName
                    });
            }

            var productMap =
                products.ToDictionary(p => p.ProductId);

            var listingIds =
                listings
                    .Select(x => x.FarmerProductId)
                    .ToList();

            var futureInventory = await _context.inventories
                .AsNoTracking()
                .Where(i =>
                    listingIds.Contains(i.FarmerProductId) &&
                    i.InventoryDate >= DateTime.Today &&
                    i.IsAvailable &&
                    !i.IsSoldOut)
                .OrderBy(i => i.InventoryDate)
                .ToListAsync(cancellationToken);

            var lines = new List<string>();

            foreach (var listing in listings)
            {
                if (!productMap.TryGetValue(
                        listing.ProductId,
                        out var product))
                {
                    continue;
                }

                var availableRows =
                    futureInventory
                        .Where(i =>
                            i.FarmerProductId ==
                            listing.FarmerProductId)
                        .Where(i =>
                            i.AvailableQuantity > 0)
                        .ToList();

                var stockText =
                    availableRows.Count > 0
                        ? $"current/future available stock exists; next date {availableRows[0].InventoryDate:yyyy-MM-dd}, available quantity {availableRows[0].AvailableQuantity:N3}"
                        : "listed, but no current/future available dated stock was found";

                lines.Add(
                    $"{product.ProductName}: listing price Rs. {listing.Price:N2}; {stockText}");
            }

            var facts = $"""
Farmer name: {farmer.BusinessName}
Active approved product listing count: {lines.Count}
Products:
{string.Join("\n", lines.Select(x => "- " + x))}
""";

            return Ground(
                "FarmerProducts",
                $"{farmer.BusinessName} has {lines.Count} active product listing(s).",
                facts,
                new CustomerAssistantActionViewModel
                {
                    Label = "Browse Products",
                    Controller = "CustomerProducts",
                    Action = "Index"
                },
                metadata: new Dictionary<string, object?>
                {
                    ["farmer"] = farmer.BusinessName
                });
        }

        private async Task<GroundedAssistantAnswer> AnswerMarketHoursAsync(
            GroqIntentResult intent,
            List<Market> markets,
            CancellationToken cancellationToken)
        {
            var market =
                ResolveMarket(intent.Market, markets);

            if (market == null)
            {
                if (!string.IsNullOrWhiteSpace(intent.Day))
                {
                    var dayRows =
                        await _context.marketdays
                            .AsNoTracking()
                            .Where(md =>
                                md.IsActive &&
                                md.DayName == intent.Day)
                            .ToListAsync(cancellationToken);

                    var marketIds =
                        dayRows
                            .Select(x => x.MarketId)
                            .Distinct()
                            .ToList();

                    var openMarkets =
                        markets
                            .Where(m => marketIds.Contains(m.MarketId))
                            .ToList();

                    if (openMarkets.Count == 0)
                    {
                        return Ground(
                            "MarketHours",
                            $"I couldn't find an active market schedule for {intent.Day}.",
                            $"Requested day: {intent.Day}\nMatching active market schedules: 0");
                    }

                    var scheduleFacts = new List<string>();

                    foreach (var m in openMarkets)
                    {
                        var rows =
                            dayRows
                                .Where(x => x.MarketId == m.MarketId)
                                .ToList();

                        foreach (var row in rows)
                        {
                            scheduleFacts.Add(
                                $"{m.MarketName}: {row.DayName}, {FormatTime(row.OpeningTime)} to {FormatTime(row.ClosingTime)}, address {m.Address}");
                        }
                    }

                    return Ground(
                        "MarketHours",
                        $"I found {scheduleFacts.Count} active market schedule(s) for {intent.Day}.",
                        string.Join("\n", scheduleFacts.Select(x => "- " + x)),
                        new CustomerAssistantActionViewModel
                        {
                            Label = "View Markets",
                            Controller = "CustomerMarkets",
                            Action = "Index"
                        });
                }

                return Ground(
                    "MarketHours",
                    "Tell me the market name or day you want to check.",
                    "No specific market or day could be resolved.");
            }

            var query =
                _context.marketdays
                    .AsNoTracking()
                    .Where(md =>
                        md.MarketId == market.MarketId &&
                        md.IsActive);

            if (!string.IsNullOrWhiteSpace(intent.Day))
            {
                query =
                    query.Where(md =>
                        md.DayName == intent.Day);
            }

            var schedules =
                await query
                    .OrderBy(md => md.MarketDayId)
                    .ToListAsync(cancellationToken);

            var facts = schedules.Count == 0
                ? $"""
Market name: {market.MarketName}
Address: {market.Address}
Active saved schedules matching the request: 0
"""
                : $"""
Market name: {market.MarketName}
Address: {market.Address}
Active schedules:
{string.Join("\n", schedules.Select(x => $"- {x.DayName}: {FormatTime(x.OpeningTime)} to {FormatTime(x.ClosingTime)}"))}
""";

            return Ground(
                "MarketHours",
                schedules.Count == 0
                    ? $"{market.MarketName} is active, but I couldn't find an opening schedule matching that request."
                    : $"{market.MarketName} has {schedules.Count} matching schedule(s).",
                facts,
                new CustomerAssistantActionViewModel
                {
                    Label = "View Markets",
                    Controller = "CustomerMarkets",
                    Action = "Index"
                },
                metadata: new Dictionary<string, object?>
                {
                    ["market"] = market.MarketName
                });
        }

        private Task<GroundedAssistantAnswer> AnswerMarketLookupAsync(
            GroqIntentResult intent,
            List<Market> markets,
            CancellationToken cancellationToken)
        {
            var market =
                ResolveMarket(intent.Market, markets);

            if (market == null)
            {
                return Task.FromResult(
                    Ground(
                        "MarketLookup",
                        "I couldn't find a matching active market.",
                        "No matching active market was found."));
            }

            return Task.FromResult(
                Ground(
                    "MarketLookup",
                    $"Yes, {market.MarketName} is an active MarketLink market.",
                    $"""
Market name: {market.MarketName}
Address: {market.Address}
Description: {market.Description}
Map provider: {market.MapProvider}
""",
                    new CustomerAssistantActionViewModel
                    {
                        Label = "View Markets",
                        Controller = "CustomerMarkets",
                        Action = "Index"
                    },
                    metadata: new Dictionary<string, object?>
                    {
                        ["market"] = market.MarketName
                    }));
        }

        private async Task<GroundedAssistantAnswer> AnswerPickupSlotsAsync(
            GroqIntentResult intent,
            List<Farmer> farmers,
            List<Market> markets,
            CancellationToken cancellationToken)
        {
            var farmer =
                ResolveFarmer(intent.Farmer, farmers);

            var market =
                ResolveMarket(intent.Market, markets);

            if (farmer == null)
            {
                return Ground(
                    "PickupSlots",
                    "Which farmer would you like me to check pickup times for?",
                    "No farmer could be resolved.");
            }

            var farmerMarkets =
                await _context.farmermarkets
                    .AsNoTracking()
                    .Where(fm =>
                        fm.FarmerId == farmer.FarmerId &&
                        fm.IsActive)
                    .ToListAsync(cancellationToken);

            if (market != null)
            {
                farmerMarkets =
                    farmerMarkets
                        .Where(fm =>
                            fm.MarketId == market.MarketId)
                        .ToList();
            }

            var farmerMarketIds =
                farmerMarkets
                    .Select(x => x.FarmerMarketId)
                    .ToList();

            var slots =
                await _context.pickupslots
                    .AsNoTracking()
                    .Where(ps =>
                        farmerMarketIds.Contains(ps.FarmerMarketId) &&
                        ps.IsAvailable &&
                        ps.PickupDate >= DateTime.Today &&
                        ps.BookedOrders < ps.MaximumOrders)
                    .OrderBy(ps => ps.PickupDate)
                    .ThenBy(ps => ps.StartTime)
                    .ToListAsync(cancellationToken);

            if (!string.IsNullOrWhiteSpace(intent.Day))
            {
                slots =
                    slots
                        .Where(s =>
                            s.PickupDate.DayOfWeek
                                .ToString()
                                .Equals(
                                    intent.Day,
                                    StringComparison.OrdinalIgnoreCase))
                        .ToList();
            }

            var targetTime =
                ParseTime(intent.Time);

            if (targetTime.HasValue)
            {
                var from =
                    targetTime.Value -
                    TimeSpan.FromHours(1);

                var to =
                    targetTime.Value +
                    TimeSpan.FromHours(1);

                slots =
                    slots
                        .Where(s =>
                            s.StartTime <= to &&
                            s.EndTime >= from)
                        .ToList();
            }

            var marketByFarmerMarket =
                farmerMarkets
                    .ToDictionary(
                        x => x.FarmerMarketId,
                        x => x.MarketId);

            var marketMap =
                markets.ToDictionary(m => m.MarketId);

            var lines =
                slots
                    .Take(8)
                    .Select(s =>
                    {
                        var marketName = "Market";

                        if (marketByFarmerMarket.TryGetValue(
                                s.FarmerMarketId,
                                out var marketId)
                            &&
                            marketMap.TryGetValue(
                                marketId,
                                out var resolvedMarket))
                        {
                            marketName =
                                resolvedMarket.MarketName;
                        }

                        return
                            $"{s.PickupDate:dddd, dd MMM yyyy}: " +
                            $"{FormatTime(s.StartTime)} to {FormatTime(s.EndTime)} " +
                            $"at {marketName}; remaining capacity {Math.Max(0, s.MaximumOrders - s.BookedOrders)}";
                    })
                    .ToList();

            var facts = $"""
Farmer: {farmer.BusinessName}
Requested market: {market?.MarketName ?? "(not specified)"}
Requested day: {intent.Day ?? "(not specified)"}
Requested approximate time: {intent.Time ?? "(not specified)"}
Available matching pickup slot count: {slots.Count}
Pickup slots:
{(lines.Count == 0 ? "(none)" : string.Join("\n", lines.Select(x => "- " + x)))}
""";

            return Ground(
                "PickupSlots",
                slots.Count == 0
                    ? $"I couldn't find an available pickup slot for {farmer.BusinessName} matching that request."
                    : $"I found {slots.Count} available pickup slot(s) for {farmer.BusinessName}.",
                facts,
                metadata: new Dictionary<string, object?>
                {
                    ["farmer"] = farmer.BusinessName,
                    ["market"] = market?.MarketName
                });
        }

        private async Task<GroundedAssistantAnswer> AnswerFarmerAvailabilityAsync(
            GroqIntentResult intent,
            List<Farmer> farmers,
            List<Market> markets,
            CancellationToken cancellationToken)
        {
            var farmer =
                ResolveFarmer(intent.Farmer, farmers);

            if (farmer == null)
            {
                return Ground(
                    "FarmerAvailability",
                    "Which farmer would you like me to check?",
                    "No farmer could be resolved.");
            }

            var farmerMarkets =
                await _context.farmermarkets
                    .AsNoTracking()
                    .Where(fm =>
                        fm.FarmerId == farmer.FarmerId &&
                        fm.IsActive)
                    .ToListAsync(cancellationToken);

            var fmIds =
                farmerMarkets
                    .Select(x => x.FarmerMarketId)
                    .ToList();

            var attendance =
                await _context.farmermarketdays
                    .AsNoTracking()
                    .Where(fmd =>
                        fmIds.Contains(fmd.FarmerMarketId) &&
                        fmd.IsActive)
                    .ToListAsync(cancellationToken);

            var marketDayIds =
                attendance
                    .Select(x => x.MarketDayId)
                    .Distinct()
                    .ToList();

            var days =
                await _context.marketdays
                    .AsNoTracking()
                    .Where(md =>
                        marketDayIds.Contains(md.MarketDayId) &&
                        md.IsActive)
                    .ToListAsync(cancellationToken);

            var marketMap =
                markets.ToDictionary(m => m.MarketId);

            var rows =
                (from fmd in attendance
                 join md in days
                    on fmd.MarketDayId equals md.MarketDayId
                 join fm in farmerMarkets
                    on fmd.FarmerMarketId equals fm.FarmerMarketId
                 where string.IsNullOrWhiteSpace(intent.Day)
                    || md.DayName.Equals(
                        intent.Day,
                        StringComparison.OrdinalIgnoreCase)
                 select new
                 {
                     md.DayName,
                     fm.MarketId,
                     fmd.PickupStartTime,
                     fmd.PickupEndTime
                 })
                .ToList();

            var lines =
                rows
                    .Take(10)
                    .Select(x =>
                    {
                        var marketName =
                            marketMap.TryGetValue(
                                x.MarketId,
                                out var market)
                                ? market.MarketName
                                : "Market";

                        return
                            $"{x.DayName} at {marketName}; pickup window {FormatTime(x.PickupStartTime)} to {FormatTime(x.PickupEndTime)}";
                    })
                    .ToList();

            var facts = $"""
Farmer: {farmer.BusinessName}
Requested day: {intent.Day ?? "(not specified)"}
Matching active attendance records: {rows.Count}
Attendance:
{(lines.Count == 0 ? "(none)" : string.Join("\n", lines.Select(x => "- " + x)))}
""";

            return Ground(
                "FarmerAvailability",
                rows.Count == 0
                    ? $"{farmer.BusinessName} does not currently have an active market-day record matching that request."
                    : $"I found {rows.Count} matching availability record(s) for {farmer.BusinessName}.",
                facts,
                metadata: new Dictionary<string, object?>
                {
                    ["farmer"] = farmer.BusinessName
                });
        }

        private async Task<GroundedAssistantAnswer> AnswerProductSearchAsync(
            int? userId,
            GroqIntentResult intent,
            string originalQuestion,
            string responseIntent,
            CancellationToken cancellationToken)
        {
            var normalizedQuery =
                !string.IsNullOrWhiteSpace(
                    intent.NormalizedQuery)
                    ? intent.NormalizedQuery
                    : originalQuestion;

            var search =
                await _smartSearch.SearchAsync(
                    normalizedQuery,
                    userId,
                    cancellationToken);

            if (search.Results.Count == 0)
            {
                var noResultFacts = $"""
Search request: {normalizedQuery}
Matching currently available result count: 0
MarketLink explanation: {search.NoResultsMessage ?? "No matching current/future available inventory was found."}
""";

                return Ground(
                    responseIntent,
                    search.NoResultsMessage
                        ?? "I couldn't find a currently available match for that request.",
                    noResultFacts);
            }

            var top =
                search.Results
                    .Take(6)
                    .ToList();

            var facts = $"""
Search request: {normalizedQuery}
Total matching currently available results: {search.ResultCount}
Top matching results:
{string.Join("\n", top.Select(x =>
    $"- {x.ProductName}; farmer {x.FarmerName}; market {x.MarketName}; price Rs. {x.Price:N2} per {x.UnitName}; available {x.AvailableQuantity:N3}; inventory date {x.InventoryDate:yyyy-MM-dd}"))}
""";

            var actions =
                new List<CustomerAssistantActionViewModel>();

            var first = top.FirstOrDefault();

            if (first != null)
            {
                actions.Add(
                    new CustomerAssistantActionViewModel
                    {
                        Label = "View Product",
                        Controller = "CustomerProducts",
                        Action = "Details",
                        Id = first.FarmerProductId
                    });
            }

            actions.Add(
                new CustomerAssistantActionViewModel
                {
                    Label = "Open Smart Search",
                    Controller = "CustomerSmartSearch",
                    Action = "Index"
                });

            return Ground(
                responseIntent,
                $"I found {search.ResultCount} currently available match(es).",
                facts,
                actions.ToArray(),
                metadata: first == null
                    ? null
                    : new Dictionary<string, object?>
                    {
                        ["farmer"] = first.FarmerName,
                        ["market"] = first.MarketName,
                        ["product"] = first.ProductName
                    });
        }

        private async Task<GroundedAssistantAnswer> AnswerUnknownAsync(
            int? userId,
            GroqIntentResult intent,
            string question,
            CancellationToken cancellationToken)
        {
            // Safe fallback: let the existing intelligent search try the request.
            var search =
                await _smartSearch.SearchAsync(
                    question,
                    userId,
                    cancellationToken);

            if (search.Results.Count > 0)
            {
                return await AnswerProductSearchAsync(
                    userId,
                    intent,
                    question,
                    "ProductSearch",
                    cancellationToken);
            }

            return Ground(
                "Unknown",
                "I can help with MarketLink products, farmers, markets, opening times, stock and pickup slots.",
                $"""
The user's question could not be mapped confidently to a supported MarketLink lookup.
No verified product search result was found.
Supported areas: product discovery, farmer lookup, farmer products, market lookup, market hours, farmer availability, pickup slots and customer favorites.
""");
        }

        private async Task<List<GroqConversationTurn>> GetRecentConversationAsync(
            int? userId,
            int? conversationId,
            CancellationToken cancellationToken)
        {
            if (!userId.HasValue ||
                !conversationId.HasValue)
            {
                return new List<GroqConversationTurn>();
            }

            var ownsConversation =
                await _context.assistantconversations
                    .AsNoTracking()
                    .AnyAsync(
                        c =>
                            c.AssistantConversationId ==
                            conversationId.Value &&
                            c.UserId == userId.Value,
                        cancellationToken);

            if (!ownsConversation)
            {
                return new List<GroqConversationTurn>();
            }

            return await _context.assistantmessages
                .AsNoTracking()
                .Where(m =>
                    m.AssistantConversationId ==
                    conversationId.Value)
                .OrderByDescending(m => m.CreatedAt)
                .Take(10)
                .OrderBy(m => m.CreatedAt)
                .Select(m =>
                    new GroqConversationTurn
                    {
                        Role = m.MessageRole,
                        Text = m.MessageText
                    })
                .ToListAsync(cancellationToken);
        }

        private static GroqIntentResult BuildLocalInterpretation(
            string question,
            IReadOnlyList<GroqConversationTurn> recent,
            List<Farmer> farmers,
            List<Market> markets,
            List<Product> products)
        {
            var lower =
                question.ToLowerInvariant();

            var farmer =
                farmers
                    .OrderByDescending(x => x.BusinessName.Length)
                    .FirstOrDefault(x =>
                        question.Contains(
                            x.BusinessName,
                            StringComparison.OrdinalIgnoreCase))
                    ?.BusinessName
                ?? FindPriorEntity(
                    recent,
                    farmers.Select(x => x.BusinessName));

            var market =
                markets
                    .OrderByDescending(x => x.MarketName.Length)
                    .FirstOrDefault(x =>
                        question.Contains(
                            x.MarketName,
                            StringComparison.OrdinalIgnoreCase))
                    ?.MarketName
                ?? FindPriorEntity(
                    recent,
                    markets.Select(x => x.MarketName));

            var product =
                products
                    .OrderByDescending(x => x.ProductName.Length)
                    .FirstOrDefault(x =>
                        question.Contains(
                            x.ProductName,
                            StringComparison.OrdinalIgnoreCase))
                    ?.ProductName
                ?? FindPriorEntity(
                    recent,
                    products.Select(x => x.ProductName));

            var intent = "ProductSearch";

            if (
                lower.Contains("notify me") ||
                lower.Contains("alert me") ||
                lower.Contains("let me know when") ||
                lower.Contains("tell me when") ||
                lower.Contains("back in stock"))
            {
                intent = "EnableRestockAlert";
            }
            else if (
                lower.Contains("stop notifying") ||
                lower.Contains("cancel the alert") ||
                lower.Contains("remove the alert") ||
                lower.Contains("turn off") && lower.Contains("alert"))
            {
                intent = "DisableRestockAlert";
            }
            else if (
                (lower.Contains("favorite") || lower.Contains("favourite") || lower.Contains("save")) &&
                lower.Contains("farmer"))
            {
                intent = "FavoriteFarmer";
            }
            else if (
                (lower.Contains("favorite") || lower.Contains("favourite") || lower.Contains("save")) &&
                (lower.Contains("product") || lower.Contains("item")))
            {
                intent = "FavoriteProduct";
            }
            else if (
                (lower.Contains("favorite") || lower.Contains("favourite") || lower.Contains("save")) &&
                lower.Contains("market"))
            {
                intent = "FavoriteMarket";
            }
            else if (
                lower.Contains("my notifications") ||
                lower.Contains("my alerts") ||
                lower.Contains("show notifications"))
            {
                intent = "ShowNotifications";
            }
            else if (lower.Contains("farmer named") ||
                lower.StartsWith("do you have any farmer") ||
                lower.StartsWith("do you have a farmer") ||
                lower.StartsWith("is there a farmer"))
            {
                intent = "FarmerLookup";
            }
            else if (
                lower.Contains("what does he sell") ||
                lower.Contains("what does she sell") ||
                lower.Contains("what do they sell") ||
                lower.Contains("what does") && lower.Contains("sell") ||
                lower.Contains("products does") ||
                lower.Contains("products from") && !lower.Contains("available"))
            {
                intent = "FarmerProducts";
            }
            else if (
                lower.Contains("open") ||
                lower.Contains("close") ||
                lower.Contains("timing") ||
                lower.Contains("hours") ||
                lower.Contains("what time") && lower.Contains("market"))
            {
                intent = "MarketHours";
            }
            else if (
                lower.Contains("pickup") ||
                lower.Contains("pick up") ||
                lower.Contains("slot") ||
                Regex.IsMatch(lower, @"\baround\s+\d{1,2}"))
            {
                intent = "PickupSlots";
            }
            else if (
                !string.IsNullOrWhiteSpace(farmer) &&
                lower.Contains("available") &&
                ExtractDay(question) != null)
            {
                intent = "FarmerAvailability";
            }
            else if (
                lower.Contains("how much") ||
                lower.Contains("price") ||
                lower.Contains("cost"))
            {
                intent = "ProductPrice";
            }

            return new GroqIntentResult
            {
                Intent = intent,
                Farmer = farmer,
                Market = market,
                Product = product,
                Day = ExtractDay(question),
                Time = ExtractTimeText(question),
                UseFavoriteFarmers =
                    Regex.IsMatch(
                        lower,
                        @"\b(favorite|favourite|saved)\s+farmer"),
                UseFavoriteProducts =
                    Regex.IsMatch(
                        lower,
                        @"\b(favorite|favourite|saved)\s+(product|item)"),
                UseFavoriteMarkets =
                    Regex.IsMatch(
                        lower,
                        @"\b(favorite|favourite|saved)\s+market"),
                NormalizedQuery = question
            };
        }

        private static string? FindPriorEntity(
            IReadOnlyList<GroqConversationTurn> recent,
            IEnumerable<string> names)
        {
            var ordered =
                names
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .OrderByDescending(x => x.Length)
                    .ToList();

            foreach (var turn in recent.Reverse())
            {
                var hit =
                    ordered.FirstOrDefault(name =>
                        turn.Text.Contains(
                            name,
                            StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrWhiteSpace(hit))
                    return hit;
            }

            return null;
        }

        private static Farmer? ResolveFarmer(
            string? requested,
            List<Farmer> farmers)
        {
            if (string.IsNullOrWhiteSpace(requested))
                return null;

            return farmers.FirstOrDefault(f =>
                       f.BusinessName.Equals(
                           requested,
                           StringComparison.OrdinalIgnoreCase))
                   ?? farmers.FirstOrDefault(f =>
                       f.BusinessName.Contains(
                           requested,
                           StringComparison.OrdinalIgnoreCase))
                   ?? farmers.FirstOrDefault(f =>
                       requested.Contains(
                           f.BusinessName,
                           StringComparison.OrdinalIgnoreCase));
        }

        private static Market? ResolveMarket(
            string? requested,
            List<Market> markets)
        {
            if (string.IsNullOrWhiteSpace(requested))
                return null;

            return markets.FirstOrDefault(m =>
                       m.MarketName.Equals(
                           requested,
                           StringComparison.OrdinalIgnoreCase))
                   ?? markets.FirstOrDefault(m =>
                       m.MarketName.Contains(
                           requested,
                           StringComparison.OrdinalIgnoreCase))
                   ?? markets.FirstOrDefault(m =>
                       requested.Contains(
                           m.MarketName,
                           StringComparison.OrdinalIgnoreCase));
        }

        private static string? ExtractDay(string text)
        {
            foreach (var day in Enum.GetNames<DayOfWeek>())
            {
                if (Regex.IsMatch(
                        text,
                        $@"\b{Regex.Escape(day)}\b",
                        RegexOptions.IgnoreCase))
                {
                    return day;
                }
            }

            return null;
        }

        private static string? ExtractTimeText(
            string text)
        {
            var match =
                Regex.Match(
                    text,
                    @"\b(?:around|at)?\s*(\d{1,2})(?::(\d{2}))?\s*(am|pm)?\b",
                    RegexOptions.IgnoreCase);

            if (!match.Success)
                return null;

            return match.Value.Trim();
        }

        private static TimeSpan? ParseTime(
            string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            var match =
                Regex.Match(
                    text,
                    @"(\d{1,2})(?::(\d{2}))?\s*(am|pm)?",
                    RegexOptions.IgnoreCase);

            if (!match.Success ||
                !int.TryParse(
                    match.Groups[1].Value,
                    out var hour))
            {
                return null;
            }

            var minute = 0;

            if (match.Groups[2].Success)
            {
                int.TryParse(
                    match.Groups[2].Value,
                    out minute);
            }

            var meridiem =
                match.Groups[3].Value
                    .ToLowerInvariant();

            if (meridiem == "pm" && hour < 12)
                hour += 12;

            if (meridiem == "am" && hour == 12)
                hour = 0;

            if (hour is < 0 or > 23 ||
                minute is < 0 or > 59)
            {
                return null;
            }

            return new TimeSpan(
                hour,
                minute,
                0);
        }

        private static string FormatTime(
            TimeSpan time)
        {
            return DateTime.Today
                .Add(time)
                .ToString("h:mm tt");
        }

        private static GroundedAssistantAnswer Ground(
            string intent,
            string fallbackText,
            string verifiedFacts,
            params CustomerAssistantActionViewModel[] actions)
        {
            return Ground(
                intent,
                fallbackText,
                verifiedFacts,
                actions,
                null);
        }

        private static GroundedAssistantAnswer Ground(
            string intent,
            string fallbackText,
            string verifiedFacts,
            CustomerAssistantActionViewModel action,
            Dictionary<string, object?>? metadata = null)
        {
            return Ground(
                intent,
                fallbackText,
                verifiedFacts,
                new[] { action },
                metadata);
        }

        private static GroundedAssistantAnswer Ground(
            string intent,
            string fallbackText,
            string verifiedFacts,
            CustomerAssistantActionViewModel[]? actions = null,
            Dictionary<string, object?>? metadata = null)
        {
            var reply =
                new MarketLinkAssistantReply
                {
                    Intent = intent,
                    Text = fallbackText,
                    Actions = actions?.ToList()
                        ?? new List<CustomerAssistantActionViewModel>(),
                    Metadata = metadata
                        ?? new Dictionary<string, object?>()
                };

            return new GroundedAssistantAnswer
            {
                Reply = reply,
                VerifiedFacts = verifiedFacts
            };
        }

        private sealed class GroundedAssistantAnswer
        {
            public MarketLinkAssistantReply Reply { get; set; } = new();
            public string VerifiedFacts { get; set; } = "";
        }
    }
}

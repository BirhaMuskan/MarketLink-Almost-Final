using MarketLink.Models;
using Microsoft.EntityFrameworkCore;

namespace MarketLink.Services
{
    public interface IWasteRiskService
    {
        Task<int> ScanFarmerAsync(
            int farmerId,
            CancellationToken cancellationToken = default);

        Task<int> ScanAllAsync(
            CancellationToken cancellationToken = default);
    }

    public sealed class WasteRiskService : IWasteRiskService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<WasteRiskService> _logger;

        public WasteRiskService(
            ApplicationDbContext context,
            ILogger<WasteRiskService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<int> ScanFarmerAsync(
            int farmerId,
            CancellationToken cancellationToken = default)
        {
            var listingIds = await _context.farmerproducts
                .AsNoTracking()
                .Where(fp =>
                    fp.FarmerId == farmerId &&
                    fp.IsActive &&
                    fp.IsApproved)
                .Select(fp => fp.FarmerProductId)
                .ToListAsync(cancellationToken);

            return await ScanListingsAsync(
                listingIds,
                cancellationToken);
        }

        public async Task<int> ScanAllAsync(
            CancellationToken cancellationToken = default)
        {
            var listingIds = await _context.farmerproducts
                .AsNoTracking()
                .Where(fp =>
                    fp.IsActive &&
                    fp.IsApproved)
                .Select(fp => fp.FarmerProductId)
                .ToListAsync(cancellationToken);

            return await ScanListingsAsync(
                listingIds,
                cancellationToken);
        }

        private async Task<int> ScanListingsAsync(
            List<int> listingIds,
            CancellationToken cancellationToken)
        {
            if (listingIds.Count == 0)
                return 0;

            var today = DateTime.Today;
            var horizon = today.AddDays(14);

            var inventories = await _context.inventories
                .AsNoTracking()
                .Where(i =>
                    listingIds.Contains(i.FarmerProductId) &&
                    i.InventoryDate >= today &&
                    i.InventoryDate <= horizon &&
                    i.IsAvailable)
                .OrderBy(i => i.InventoryDate)
                .ToListAsync(cancellationToken);

            if (inventories.Count == 0)
                return 0;

            var predictions = await _context.demandpredictions
                .AsNoTracking()
                .Where(p =>
                    listingIds.Contains(p.FarmerProductId) &&
                    p.PredictionDate >= today &&
                    p.PredictionDate <= horizon)
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync(cancellationToken);

            if (predictions.Count == 0)
                return 0;

            var listingMap = await _context.farmerproducts
                .AsNoTracking()
                .Where(fp =>
                    listingIds.Contains(fp.FarmerProductId))
                .ToDictionaryAsync(
                    fp => fp.FarmerProductId,
                    cancellationToken);

            var productIds = listingMap
                .Values
                .Select(x => x.ProductId)
                .Distinct()
                .ToList();

            var products = await _context.products
                .AsNoTracking()
                .Where(p =>
                    productIds.Contains(p.ProductId))
                .ToDictionaryAsync(
                    p => p.ProductId,
                    cancellationToken);

            var farmerIds = listingMap
                .Values
                .Select(x => x.FarmerId)
                .Distinct()
                .ToList();

            var farmers = await _context.farmers
                .AsNoTracking()
                .Where(f =>
                    farmerIds.Contains(f.FarmerId))
                .ToDictionaryAsync(
                    f => f.FarmerId,
                    cancellationToken);

            var created = 0;

            foreach (var inventory in inventories)
            {
                var availableStock =
                    inventory.AvailableQuantity;

                if (availableStock <= 0)
                    continue;

                var prediction = predictions
                    .Where(p =>
                        p.FarmerProductId ==
                            inventory.FarmerProductId &&
                        p.FarmerMarketId ==
                            inventory.FarmerMarketId)
                    .OrderBy(p =>
                        Math.Abs(
                            (p.PredictionDate.Date -
                             inventory.InventoryDate.Date).TotalDays))
                    .ThenByDescending(p => p.CreatedAt)
                    .FirstOrDefault();

                if (prediction == null)
                    continue;

                var predictedDemand =
                    Math.Max(
                        prediction.PredictedDemand,
                        prediction.CurrentReservations);

                var excess =
                    availableStock - predictedDemand;

                if (excess <= 0)
                    continue;

                var excessPct =
                    availableStock > 0
                        ? excess / availableStock
                        : 0;

                var risk =
                    GetRiskLevel(
                        excessPct,
                        excess);

                if (risk == null)
                    continue;

                var recommendation =
                    BuildRecommendation(
                        risk,
                        availableStock,
                        predictedDemand,
                        excess);

                var recent = await _context.wasteriskpredictions
                    .Where(w =>
                        w.InventoryId ==
                            inventory.InventoryId &&
                        w.CreatedAt >=
                            DateTime.Now.AddHours(-24))
                    .OrderByDescending(w => w.CreatedAt)
                    .FirstOrDefaultAsync(cancellationToken);

                if (recent != null)
                {
                    recent.AiModelRunId =
                        prediction.AiModelRunId;

                    recent.CurrentStock =
                        decimal.Round(
                            availableStock,
                            3);

                    recent.PredictedDemand =
                        decimal.Round(
                            predictedDemand,
                            3);

                    recent.PredictedExcess =
                        decimal.Round(
                            excess,
                            3);

                    recent.RiskLevel =
                        risk;

                    recent.Recommendation =
                        recommendation;

                    recent.CreatedAt =
                        DateTime.Now;

                    continue;
                }

                _context.wasteriskpredictions.Add(
                    new WasteRiskPrediction
                    {
                        AiModelRunId =
                            prediction.AiModelRunId,
                        InventoryId =
                            inventory.InventoryId,
                        CurrentStock =
                            decimal.Round(
                                availableStock,
                                3),
                        PredictedDemand =
                            decimal.Round(
                                predictedDemand,
                                3),
                        PredictedExcess =
                            decimal.Round(
                                excess,
                                3),
                        RiskLevel =
                            risk,
                        Recommendation =
                            recommendation,
                        CreatedAt =
                            DateTime.Now
                    });

                created++;

                if (risk is "High" or "Medium" &&
                    listingMap.TryGetValue(
                        inventory.FarmerProductId,
                        out var listing)
                    &&
                    farmers.TryGetValue(
                        listing.FarmerId,
                        out var farmer))
                {
                    var productName =
                        products.TryGetValue(
                            listing.ProductId,
                            out var product)
                            ? product.ProductName
                            : "Product";

                    var alreadyNotified =
                        await _context.notifications
                            .AsNoTracking()
                            .AnyAsync(
                                n =>
                                    n.UserId ==
                                        farmer.UserId &&
                                    n.NotificationType ==
                                        "AI_WasteRisk" &&
                                    n.Message.Contains(
                                        productName) &&
                                    n.CreatedAt >=
                                        DateTime.Now.AddHours(-24),
                                cancellationToken);

                    if (!alreadyNotified)
                    {
                        _context.notifications.Add(
                            new Notification
                            {
                                UserId =
                                    farmer.UserId,
                                Title =
                                    $"Potential overstock risk: {productName}",
                                Message =
                                    $"{productName} has about {excess:N1} units above predicted demand. " +
                                    $"Available stock: {availableStock:N1}; predicted demand: {predictedDemand:N1}. " +
                                    $"Risk level: {risk}.",
                                NotificationType =
                                    "AI_WasteRisk",
                                ActionUrl =
                                    "/FarmerWasteRisk",
                                IsRead =
                                    false,
                                CreatedAt =
                                    DateTime.Now
                            });
                    }
                }
            }

            await _context.SaveChangesAsync(
                cancellationToken);

            return created;
        }

        private static string? GetRiskLevel(
            decimal excessPct,
            decimal excess)
        {
            // Transparent first-version rules:
            // High: >= 50% of available stock likely excess AND >= 10 units
            // Medium: >= 25% likely excess AND >= 5 units
            // Low: any meaningful positive excess >= 2 units
            if (excessPct >= 0.50m &&
                excess >= 10m)
            {
                return "High";
            }

            if (excessPct >= 0.25m &&
                excess >= 5m)
            {
                return "Medium";
            }

            if (excess >= 2m)
            {
                return "Low";
            }

            return null;
        }

        private static string BuildRecommendation(
            string risk,
            decimal currentStock,
            decimal predictedDemand,
            decimal predictedExcess)
        {
            return risk switch
            {
                "High" =>
                    $"Potential overstock is high. About {predictedExcess:N1} units may remain if demand follows the current forecast. " +
                    "Consider reducing future preparation, promoting this item, or shifting stock to another suitable market.",

                "Medium" =>
                    $"There is a moderate overstock risk. About {predictedExcess:N1} units are above predicted demand. " +
                    "Review upcoming reservations and consider a smaller replenishment or a promotion.",

                _ =>
                    $"Stock is slightly above predicted demand by about {predictedExcess:N1} units. " +
                    "Monitor reservations before adding more stock."
            };
        }
    }
}

using MarketLink.Models;
using Microsoft.EntityFrameworkCore;

namespace MarketLink.Services
{
    public interface IAnomalyDetectionService
    {
        Task<int> ScanFarmerAsync(
            int farmerId,
            CancellationToken cancellationToken = default);

        Task<int> ScanAllAsync(
            CancellationToken cancellationToken = default);
    }

    public sealed class AnomalyDetectionService : IAnomalyDetectionService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<AnomalyDetectionService> _logger;

        public AnomalyDetectionService(
            ApplicationDbContext context,
            ILogger<AnomalyDetectionService> logger)
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
            var historyStart = today.AddDays(-84); // 12 weeks
            var currentEnd = today.AddDays(7);

            var inventories = await _context.inventories
                .AsNoTracking()
                .Where(i =>
                    listingIds.Contains(i.FarmerProductId) &&
                    i.InventoryDate >= historyStart &&
                    i.InventoryDate <= currentEnd)
                .OrderBy(i => i.InventoryDate)
                .ToListAsync(cancellationToken);

            if (inventories.Count == 0)
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
                .Where(p => productIds.Contains(p.ProductId))
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
                .Where(f => farmerIds.Contains(f.FarmerId))
                .ToDictionaryAsync(
                    f => f.FarmerId,
                    cancellationToken);

            var newCount = 0;

            var groups = inventories
                .GroupBy(i => new
                {
                    i.FarmerProductId,
                    i.FarmerMarketId
                });

            foreach (var group in groups)
            {
                var currentRows = group
                    .Where(i =>
                        i.InventoryDate >= today &&
                        i.InventoryDate <= currentEnd)
                    .OrderBy(i => i.InventoryDate)
                    .ToList();

                if (currentRows.Count == 0)
                    continue;

                var observed = currentRows
                    .Sum(i =>
                        i.SoldQuantity +
                        i.ReservedQuantity);

                // Baseline uses up to 8 most recent historical market-date demand observations.
                var baselineRows = group
                    .Where(i => i.InventoryDate < today)
                    .OrderByDescending(i => i.InventoryDate)
                    .Take(8)
                    .Select(i =>
                        i.SoldQuantity +
                        i.ReservedQuantity)
                    .Where(x => x >= 0)
                    .ToList();

                // Avoid pretending we have a meaningful baseline with too little history.
                if (baselineRows.Count < 3)
                    continue;

                var baseline = baselineRows.Average();

                // Zero baseline needs special handling; require meaningful observed demand.
                decimal ratio;
                if (baseline <= 0.001m)
                {
                    if (observed < 5m)
                        continue;

                    ratio = 5m;
                }
                else
                {
                    ratio = observed / baseline;
                }

                var severity = GetSeverity(ratio, observed, baseline);

                if (severity == null)
                    continue;

                var recentExisting = await _context.anomalydetections
                    .FirstOrDefaultAsync(
                        a =>
                            a.FarmerProductId ==
                                group.Key.FarmerProductId &&
                            a.FarmerMarketId ==
                                group.Key.FarmerMarketId &&
                            !a.IsAcknowledged &&
                            a.DetectedAt >=
                                DateTime.Now.AddHours(-24),
                        cancellationToken);

                var productName =
                    listingMap.TryGetValue(
                        group.Key.FarmerProductId,
                        out var listing)
                    &&
                    products.TryGetValue(
                        listing.ProductId,
                        out var product)
                        ? product.ProductName
                        : "Product";

                var message =
                    BuildMessage(
                        productName,
                        baseline,
                        observed,
                        ratio,
                        severity);

                if (recentExisting != null)
                {
                    recentExisting.BaselineAverage =
                        decimal.Round(baseline, 3);

                    recentExisting.ObservedDemand =
                        decimal.Round(observed, 3);

                    recentExisting.DeviationRatio =
                        decimal.Round(ratio, 3);

                    recentExisting.Severity =
                        severity;

                    recentExisting.Message =
                        message;

                    recentExisting.DetectedAt =
                        DateTime.Now;

                    continue;
                }

                var anomaly =
                    new AnomalyDetection
                    {
                        FarmerProductId =
                            group.Key.FarmerProductId,
                        FarmerMarketId =
                            group.Key.FarmerMarketId,
                        BaselineAverage =
                            decimal.Round(baseline, 3),
                        ObservedDemand =
                            decimal.Round(observed, 3),
                        DeviationRatio =
                            decimal.Round(ratio, 3),
                        Severity =
                            severity,
                        Message =
                            message,
                        DetectedAt =
                            DateTime.Now,
                        IsAcknowledged =
                            false
                    };

                _context.anomalydetections.Add(anomaly);
                newCount++;

                if (listing != null &&
                    farmers.TryGetValue(
                        listing.FarmerId,
                        out var farmer))
                {
                    _context.notifications.Add(
                        new Notification
                        {
                            UserId = farmer.UserId,
                            Title =
                                $"Unusual demand: {productName}",
                            Message =
                                message,
                            NotificationType =
                                "AI_Anomaly",
                            ActionUrl =
                                "/FarmerAnomalies",
                            IsRead =
                                false,
                            CreatedAt =
                                DateTime.Now
                        });
                }
            }

            await _context.SaveChangesAsync(
                cancellationToken);

            return newCount;
        }

        private static string? GetSeverity(
            decimal ratio,
            decimal observed,
            decimal baseline)
        {
            // Require both a relative spike and a meaningful absolute increase.
            var absoluteIncrease =
                observed - baseline;

            if (ratio >= 3.0m &&
                absoluteIncrease >= 10m)
            {
                return "Critical";
            }

            if (ratio >= 2.0m &&
                absoluteIncrease >= 5m)
            {
                return "High";
            }

            if (ratio >= 1.5m &&
                absoluteIncrease >= 3m)
            {
                return "Medium";
            }

            return null;
        }

        private static string BuildMessage(
            string productName,
            decimal baseline,
            decimal observed,
            decimal ratio,
            string severity)
        {
            return
                $"{productName} demand is currently {ratio:N1}× the recent average. " +
                $"Recent baseline: {baseline:N1}; observed demand: {observed:N1}. " +
                $"Severity: {severity}. Consider reviewing available stock and upcoming reservations.";
        }
    }
}

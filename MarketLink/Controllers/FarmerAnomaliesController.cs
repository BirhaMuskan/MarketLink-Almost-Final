using System.Security.Claims;
using MarketLink.Models;
using MarketLink.Services;
using MarketLink.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketLink.Controllers
{
    [Authorize(Roles = "Farmer")]
    public class FarmerAnomaliesController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IAnomalyDetectionService _service;

        public FarmerAnomaliesController(
            ApplicationDbContext context,
            IAnomalyDetectionService service)
        {
            _context = context;
            _service = service;
        }

        [HttpGet]
        public async Task<IActionResult> Index(
            bool showAcknowledged = false,
            CancellationToken cancellationToken = default)
        {
            var farmer = await GetFarmerAsync(
                cancellationToken);

            if (farmer == null)
                return Forbid();

            var listingIds = await _context.farmerproducts
                .AsNoTracking()
                .Where(fp => fp.FarmerId == farmer.FarmerId)
                .Select(fp => fp.FarmerProductId)
                .ToListAsync(cancellationToken);

            var query = _context.anomalydetections
                .AsNoTracking()
                .Where(a =>
                    listingIds.Contains(a.FarmerProductId));

            if (!showAcknowledged)
            {
                query = query.Where(a =>
                    !a.IsAcknowledged);
            }

            var anomalies = await query
                .OrderByDescending(a =>
                    a.DetectedAt)
                .ToListAsync(cancellationToken);

            var productIds = await _context.farmerproducts
                .AsNoTracking()
                .Where(fp =>
                    listingIds.Contains(fp.FarmerProductId))
                .ToDictionaryAsync(
                    fp => fp.FarmerProductId,
                    fp => fp.ProductId,
                    cancellationToken);

            var products = await _context.products
                .AsNoTracking()
                .ToDictionaryAsync(
                    p => p.ProductId,
                    p => p.ProductName,
                    cancellationToken);

            var farmerMarketIds = anomalies
                .Select(a => a.FarmerMarketId)
                .Distinct()
                .ToList();

            var farmerMarkets = await _context.farmermarkets
                .AsNoTracking()
                .Where(fm =>
                    farmerMarketIds.Contains(fm.FarmerMarketId))
                .ToDictionaryAsync(
                    fm => fm.FarmerMarketId,
                    fm => fm.MarketId,
                    cancellationToken);

            var markets = await _context.markets
                .AsNoTracking()
                .ToDictionaryAsync(
                    m => m.MarketId,
                    m => m.MarketName,
                    cancellationToken);

            var items = anomalies.Select(a =>
            {
                var productName = "Product";
                if (productIds.TryGetValue(
                        a.FarmerProductId,
                        out var productId)
                    &&
                    products.TryGetValue(
                        productId,
                        out var resolvedProduct))
                {
                    productName = resolvedProduct;
                }

                var marketName = "Market";
                if (farmerMarkets.TryGetValue(
                        a.FarmerMarketId,
                        out var marketId)
                    &&
                    markets.TryGetValue(
                        marketId,
                        out var resolvedMarket))
                {
                    marketName = resolvedMarket;
                }

                return new FarmerAnomalyItemViewModel
                {
                    AnomalyDetectionId =
                        a.AnomalyDetectionId,
                    ProductName =
                        productName,
                    MarketName =
                        marketName,
                    BaselineAverage =
                        a.BaselineAverage,
                    ObservedDemand =
                        a.ObservedDemand,
                    DeviationRatio =
                        a.DeviationRatio,
                    Severity =
                        a.Severity,
                    Message =
                        a.Message,
                    DetectedAt =
                        a.DetectedAt,
                    IsAcknowledged =
                        a.IsAcknowledged
                };
            }).ToList();

            var open = items
                .Where(x => !x.IsAcknowledged)
                .ToList();

            var model =
                new FarmerAnomalyPageViewModel
                {
                    TotalOpen = open.Count,
                    CriticalCount =
                        open.Count(x =>
                            x.Severity == "Critical"),
                    HighCount =
                        open.Count(x =>
                            x.Severity == "High"),
                    MediumCount =
                        open.Count(x =>
                            x.Severity == "Medium"),
                    LastDetectedAt =
                        items.Count > 0
                            ? items.Max(x => x.DetectedAt)
                            : null,
                    Items = items
                };

            ViewBag.ShowAcknowledged =
                showAcknowledged;

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ScanNow(
            CancellationToken cancellationToken)
        {
            var farmer = await GetFarmerAsync(
                cancellationToken);

            if (farmer == null)
                return Forbid();

            var count =
                await _service.ScanFarmerAsync(
                    farmer.FarmerId,
                    cancellationToken);

            TempData["SuccessMessage"] =
                count == 0
                    ? "Scan complete. No new unusual demand alerts were detected."
                    : $"Scan complete. {count} new demand alert(s) detected.";

            return RedirectToAction(
                nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Acknowledge(
            int id,
            CancellationToken cancellationToken)
        {
            var farmer = await GetFarmerAsync(
                cancellationToken);

            if (farmer == null)
                return Forbid();

            var ownsAlert =
                await (
                    from a in _context.anomalydetections
                    join fp in _context.farmerproducts
                        on a.FarmerProductId equals fp.FarmerProductId
                    where
                        a.AnomalyDetectionId == id &&
                        fp.FarmerId == farmer.FarmerId
                    select a
                ).FirstOrDefaultAsync(cancellationToken);

            if (ownsAlert == null)
                return NotFound();

            ownsAlert.IsAcknowledged = true;

            await _context.SaveChangesAsync(
                cancellationToken);

            TempData["SuccessMessage"] =
                "Demand alert acknowledged.";

            return RedirectToAction(
                nameof(Index));
        }

        private async Task<Farmer?> GetFarmerAsync(
            CancellationToken cancellationToken)
        {
            var claim =
                User.FindFirstValue(
                    ClaimTypes.NameIdentifier)
                ?? User.FindFirstValue("sub");

            if (!int.TryParse(
                    claim,
                    out var userId))
            {
                return null;
            }

            return await _context.farmers
                .FirstOrDefaultAsync(
                    f => f.UserId == userId,
                    cancellationToken);
        }
    }
}

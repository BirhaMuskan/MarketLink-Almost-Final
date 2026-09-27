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
    public class FarmerWasteRiskController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWasteRiskService _service;

        public FarmerWasteRiskController(
            ApplicationDbContext context,
            IWasteRiskService service)
        {
            _context = context;
            _service = service;
        }

        [HttpGet]
        public async Task<IActionResult> Index(
            string? risk = null,
            CancellationToken cancellationToken = default)
        {
            var farmer = await GetFarmerAsync(
                cancellationToken);

            if (farmer == null)
                return Forbid();

            var listingIds = await _context.farmerproducts
                .AsNoTracking()
                .Where(fp =>
                    fp.FarmerId == farmer.FarmerId)
                .Select(fp => fp.FarmerProductId)
                .ToListAsync(cancellationToken);

            var inventoryIds = await _context.inventories
                .AsNoTracking()
                .Where(i =>
                    listingIds.Contains(i.FarmerProductId))
                .Select(i => i.InventoryId)
                .ToListAsync(cancellationToken);

            var query = _context.wasteriskpredictions
                .AsNoTracking()
                .Where(w =>
                    inventoryIds.Contains(w.InventoryId));

            if (!string.IsNullOrWhiteSpace(risk))
            {
                query = query.Where(w =>
                    w.RiskLevel == risk);
            }

            var rows = await query
                .OrderByDescending(w => w.CreatedAt)
                .ToListAsync(cancellationToken);

            var inventoryMap = await _context.inventories
                .AsNoTracking()
                .Where(i =>
                    inventoryIds.Contains(i.InventoryId))
                .ToDictionaryAsync(
                    i => i.InventoryId,
                    cancellationToken);

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

            var productMap = await _context.products
                .AsNoTracking()
                .Where(p =>
                    productIds.Contains(p.ProductId))
                .ToDictionaryAsync(
                    p => p.ProductId,
                    p => p.ProductName,
                    cancellationToken);

            var farmerMarketIds = inventoryMap
                .Values
                .Select(x => x.FarmerMarketId)
                .Distinct()
                .ToList();

            var farmerMarketMap = await _context.farmermarkets
                .AsNoTracking()
                .Where(fm =>
                    farmerMarketIds.Contains(
                        fm.FarmerMarketId))
                .ToDictionaryAsync(
                    fm => fm.FarmerMarketId,
                    fm => fm.MarketId,
                    cancellationToken);

            var marketIds = farmerMarketMap
                .Values
                .Distinct()
                .ToList();

            var marketMap = await _context.markets
                .AsNoTracking()
                .Where(m =>
                    marketIds.Contains(m.MarketId))
                .ToDictionaryAsync(
                    m => m.MarketId,
                    m => m.MarketName,
                    cancellationToken);

            var items = new List<FarmerWasteRiskItemViewModel>();

            foreach (var row in rows)
            {
                if (!inventoryMap.TryGetValue(
                        row.InventoryId,
                        out var inventory))
                {
                    continue;
                }

                var productName = "Product";

                if (listingMap.TryGetValue(
                        inventory.FarmerProductId,
                        out var listing)
                    &&
                    productMap.TryGetValue(
                        listing.ProductId,
                        out var resolvedProduct))
                {
                    productName =
                        resolvedProduct;
                }

                var marketName = "Market";

                if (farmerMarketMap.TryGetValue(
                        inventory.FarmerMarketId,
                        out var marketId)
                    &&
                    marketMap.TryGetValue(
                        marketId,
                        out var resolvedMarket))
                {
                    marketName =
                        resolvedMarket;
                }

                var excessPct =
                    row.CurrentStock > 0
                        ? row.PredictedExcess /
                          row.CurrentStock
                        : 0;

                items.Add(
                    new FarmerWasteRiskItemViewModel
                    {
                        WasteRiskPredictionId =
                            row.WasteRiskPredictionId,
                        ProductName =
                            productName,
                        MarketName =
                            marketName,
                        InventoryDate =
                            inventory.InventoryDate,
                        CurrentStock =
                            row.CurrentStock,
                        PredictedDemand =
                            row.PredictedDemand,
                        PredictedExcess =
                            row.PredictedExcess,
                        ExcessPct =
                            excessPct,
                        RiskLevel =
                            row.RiskLevel,
                        Recommendation =
                            row.Recommendation,
                        CreatedAt =
                            row.CreatedAt
                    });
            }

            var model =
                new FarmerWasteRiskPageViewModel
                {
                    TotalItems =
                        items.Count,
                    HighCount =
                        items.Count(x =>
                            x.RiskLevel == "High"),
                    MediumCount =
                        items.Count(x =>
                            x.RiskLevel == "Medium"),
                    LowCount =
                        items.Count(x =>
                            x.RiskLevel == "Low"),
                    TotalPredictedExcess =
                        items.Sum(x =>
                            x.PredictedExcess),
                    Items =
                        items
                };

            ViewBag.Risk =
                risk;

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
                    ? "Waste-risk scan complete. No new overstock risks were detected."
                    : $"Waste-risk scan complete. {count} new risk item(s) were created.";

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
                    f =>
                        f.UserId == userId,
                    cancellationToken);
        }
    }
}

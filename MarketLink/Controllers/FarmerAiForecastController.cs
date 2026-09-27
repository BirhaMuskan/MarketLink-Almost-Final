using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MarketLink.Models;
using MarketLink.Services;
using MarketLink.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketLink.Controllers
{
    [Authorize(Roles = "Farmer")]
    public class FarmerAiForecastController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IMarketLinkAiService _aiService;

        public FarmerAiForecastController(
            ApplicationDbContext context,
            IMarketLinkAiService aiService)
        {
            _context = context;
            _aiService = aiService;
        }

        [HttpGet]
        public async Task<IActionResult> Index(
            CancellationToken cancellationToken)
        {
            var farmer = await GetCurrentFarmerAsync(cancellationToken);

            if (farmer == null)
                return Forbid();

            return View(
                await BuildPageAsync(
                    farmer.FarmerId,
                    cancellationToken));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Generate(
            int farmerProductId,
            int farmerMarketId,
            DateTime predictionDate,
            CancellationToken cancellationToken)
        {
            var farmer = await GetCurrentFarmerAsync(cancellationToken);

            if (farmer == null)
                return Forbid();

            predictionDate = predictionDate.Date;

            if (predictionDate < DateTime.Today)
            {
                TempData["AiError"] =
                    "Prediction date cannot be in the past.";

                return RedirectToAction(nameof(Index));
            }

            var farmerProduct = await _context.farmerproducts
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    fp =>
                        fp.FarmerProductId == farmerProductId &&
                        fp.FarmerId == farmer.FarmerId &&
                        fp.IsActive,
                    cancellationToken);

            if (farmerProduct == null)
                return NotFound();

            var farmerMarket = await _context.farmermarkets
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    fm =>
                        fm.FarmerMarketId == farmerMarketId &&
                        fm.FarmerId == farmer.FarmerId &&
                        fm.IsActive,
                    cancellationToken);

            if (farmerMarket == null)
                return NotFound();

            var historyStart = predictionDate.AddDays(-84);

            var history = await _context.inventories
                .AsNoTracking()
                .Where(i =>
                    i.FarmerProductId == farmerProductId &&
                    i.FarmerMarketId == farmerMarketId &&
                    i.InventoryDate >= historyStart &&
                    i.InventoryDate < predictionDate)
                .OrderBy(i => i.InventoryDate)
                .Select(i => new AiHistoricalDemandRow
                {
                    Date = i.InventoryDate,
                    UnitPrice = i.UnitPrice,
                    StockQuantity = i.StockQuantity,
                    ReservedQuantity = i.ReservedQuantity,
                    SoldQuantity = i.SoldQuantity
                })
                .ToListAsync(cancellationToken);

            if (history.Count == 0)
            {
                TempData["AiError"] =
                    "No historical inventory/sales rows were found for this product and market. Add historical weekly inventory first.";

                return RedirectToAction(nameof(Index));
            }

            var predictionInventory = await _context.inventories
                .AsNoTracking()
                .Where(i =>
                    i.FarmerProductId == farmerProductId &&
                    i.FarmerMarketId == farmerMarketId &&
                    i.InventoryDate == predictionDate)
                .OrderByDescending(i => i.UpdatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            var currentReservations =
                predictionInventory?.ReservedQuantity ?? 0m;

            var currentStock =
                predictionInventory?.StockQuantity ?? 0m;

            var currentPrice =
                predictionInventory?.UnitPrice ?? farmerProduct.Price;

            var modelRun = new AiModelRun
            {
                ModelType = "DemandForecast",
                ModelName = "MarketLink Free Local Forecast",
                ModelVersion = "1.0",
                TrainingWindowStart = history.Min(x => x.Date),
                TrainingWindowEnd = history.Max(x => x.Date),
                ParametersJson = JsonSerializer.Serialize(new
                {
                    HistoryWeeks = 12,
                    farmerProductId,
                    farmerMarketId,
                    predictionDate
                }),
                DatasetHash = BuildDatasetHash(history),
                Status = "Running",
                StartedAt = DateTime.Now
            };

            _context.aimodelruns.Add(modelRun);
            await _context.SaveChangesAsync(cancellationToken);

            try
            {
                var response = await _aiService.ForecastAsync(
                    new AiForecastRequest
                    {
                        FarmerProductId = farmerProductId,
                        FarmerMarketId = farmerMarketId,
                        PredictionDate = predictionDate,
                        CurrentReservations = currentReservations,
                        CurrentStock = currentStock,
                        CurrentPrice = currentPrice,
                        History = history
                    },
                    cancellationToken);

                modelRun.ModelName = response.Algorithm;
                modelRun.MetricsJson = JsonSerializer.Serialize(new
                {
                    response.Mae,
                    response.Mape,
                    response.HistoricalAverage,
                    response.TrainingRows,
                    response.ConfidenceScore
                });

                modelRun.Status = "Completed";
                modelRun.CompletedAt = DateTime.Now;

                var prediction = new DemandPrediction
                {
                    AiModelRunId = modelRun.AiModelRunId,
                    FarmerProductId = farmerProductId,
                    FarmerMarketId = farmerMarketId,
                    PredictionDate = predictionDate,
                    PredictedDemand = response.PredictedDemand,
                    RecommendedStock = response.RecommendedStock,
                    CurrentReservations = currentReservations,
                    ShortageRisk = response.ShortageRisk,
                    ConfidenceScore = response.ConfidenceScore,
                    CreatedAt = DateTime.Now
                };

                _context.demandpredictions.Add(prediction);

                await _context.SaveChangesAsync(cancellationToken);

                TempData["AiSuccess"] =
                    response.Explanation;

                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                modelRun.Status = "Failed";
                modelRun.MetricsJson = JsonSerializer.Serialize(new
                {
                    Error = ex.Message
                });
                modelRun.CompletedAt = DateTime.Now;

                await _context.SaveChangesAsync(cancellationToken);

                TempData["AiError"] =
                    "Could not reach the local AI service. Start the free Python AI service and try again.";

                return RedirectToAction(nameof(Index));
            }
        }

        private async Task<FarmerAiForecastPageViewModel> BuildPageAsync(
            int farmerId,
            CancellationToken cancellationToken)
        {
            var farmerProducts = await _context.farmerproducts
                .AsNoTracking()
                .Where(fp =>
                    fp.FarmerId == farmerId &&
                    fp.IsActive)
                .ToListAsync(cancellationToken);

            var productIds = farmerProducts
                .Select(fp => fp.ProductId)
                .Distinct()
                .ToList();

            var products = await _context.products
                .AsNoTracking()
                .Where(p => productIds.Contains(p.ProductId))
                .ToDictionaryAsync(
                    p => p.ProductId,
                    cancellationToken);

            var farmerMarkets = await _context.farmermarkets
                .AsNoTracking()
                .Where(fm =>
                    fm.FarmerId == farmerId &&
                    fm.IsActive)
                .ToListAsync(cancellationToken);

            var marketIds = farmerMarkets
                .Select(fm => fm.MarketId)
                .Distinct()
                .ToList();

            var markets = await _context.markets
                .AsNoTracking()
                .Where(m => marketIds.Contains(m.MarketId))
                .ToDictionaryAsync(
                    m => m.MarketId,
                    cancellationToken);

            var farmerProductIds = farmerProducts
                .Select(fp => fp.FarmerProductId)
                .ToList();

            var farmerMarketIds = farmerMarkets
                .Select(fm => fm.FarmerMarketId)
                .ToList();

            var predictions = await _context.demandpredictions
                .AsNoTracking()
                .Where(dp =>
                    farmerProductIds.Contains(dp.FarmerProductId) &&
                    farmerMarketIds.Contains(dp.FarmerMarketId))
                .OrderByDescending(dp => dp.CreatedAt)
                .Take(20)
                .ToListAsync(cancellationToken);

            var runIds = predictions
                .Select(dp => dp.AiModelRunId)
                .Distinct()
                .ToList();

            var runs = await _context.aimodelruns
                .AsNoTracking()
                .Where(r => runIds.Contains(r.AiModelRunId))
                .ToDictionaryAsync(
                    r => r.AiModelRunId,
                    cancellationToken);

            var fpMap = farmerProducts
                .ToDictionary(fp => fp.FarmerProductId);

            var fmMap = farmerMarkets
                .ToDictionary(fm => fm.FarmerMarketId);

            var recent = predictions.Select(dp =>
            {
                string productName = "Product";
                string marketName = "Market";

                if (fpMap.TryGetValue(dp.FarmerProductId, out var fp) &&
                    products.TryGetValue(fp.ProductId, out var product))
                {
                    productName = product.ProductName;
                }

                if (fmMap.TryGetValue(dp.FarmerMarketId, out var fm) &&
                    markets.TryGetValue(fm.MarketId, out var market))
                {
                    marketName = market.MarketName;
                }

                runs.TryGetValue(dp.AiModelRunId, out var run);

                return new FarmerAiForecastResultViewModel
                {
                    DemandPredictionId = dp.DemandPredictionId,
                    ProductName = productName,
                    MarketName = marketName,
                    PredictionDate = dp.PredictionDate,
                    PredictedDemand = dp.PredictedDemand,
                    RecommendedStock = dp.RecommendedStock,
                    CurrentReservations = dp.CurrentReservations,
                    ShortageRisk = dp.ShortageRisk,
                    ConfidenceScore = dp.ConfidenceScore,
                    ModelName = run?.ModelName ?? "",
                    MetricsJson = run?.MetricsJson ?? "",
                    CreatedAt = dp.CreatedAt
                };
            }).ToList();

            return new FarmerAiForecastPageViewModel
            {
                Products = farmerProducts
                    .Select(fp => new FarmerAiForecastSelectionViewModel
                    {
                        Id = fp.FarmerProductId,
                        Name = products.TryGetValue(fp.ProductId, out var p)
                            ? p.ProductName
                            : $"Product #{fp.FarmerProductId}"
                    })
                    .OrderBy(x => x.Name)
                    .ToList(),

                Markets = farmerMarkets
                    .Select(fm => new FarmerAiForecastSelectionViewModel
                    {
                        Id = fm.FarmerMarketId,
                        Name = markets.TryGetValue(fm.MarketId, out var m)
                            ? m.MarketName
                            : $"Market #{fm.FarmerMarketId}"
                    })
                    .OrderBy(x => x.Name)
                    .ToList(),

                PredictionDate = DateTime.Today.AddDays(7),
                RecentPredictions = recent
            };
        }

        private async Task<Farmer?> GetCurrentFarmerAsync(
            CancellationToken cancellationToken)
        {
            var value =
                User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? User.FindFirstValue("sub");

            if (!int.TryParse(value, out var userId))
                return null;

            return await _context.farmers
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    f =>
                        f.UserId == userId &&
                        f.IsApproved &&
                        f.IsActive,
                    cancellationToken);
        }

        private static string BuildDatasetHash(
            IEnumerable<AiHistoricalDemandRow> history)
        {
            var json = JsonSerializer.Serialize(history);
            var bytes = SHA256.HashData(
                Encoding.UTF8.GetBytes(json));

            return Convert.ToHexString(bytes);
        }
    }
}

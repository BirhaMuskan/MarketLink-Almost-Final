using MarketLink.Models;
using MarketLink.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketLink.Controllers
{
    public class MarketsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public MarketsController(ApplicationDbContext context)
        {
            _context = context;
        }

        // =========================================================
        // MARKETS INDEX
        // =========================================================
        public async Task<IActionResult> Index()
        {
            // Active markets
            var markets = await _context.markets
                .Where(m => m.IsActive)
                .OrderBy(m => m.MarketName)
                .ToListAsync();

            // Active market days
            var marketDays = await _context.marketdays
                .Where(md => md.IsActive)
                .OrderBy(md => md.OpeningTime)
                .ToListAsync();

            // Active farmer-market relationships
            var farmerMarkets = await _context.farmermarkets
                .Where(fm => fm.IsActive)
                .ToListAsync();

            var imageList = new[]
            {
                "https://images.unsplash.com/photo-1488459716781-31db52582fe9?auto=format&fit=crop&w=800&q=85",
                "https://images.unsplash.com/photo-1464226184884-fa280b87c399?auto=format&fit=crop&w=800&q=85",
                "https://images.unsplash.com/photo-1542838132-92c53300491e?auto=format&fit=crop&w=800&q=85",
                "https://images.unsplash.com/photo-1471193945509-9ad0617afabf?auto=format&fit=crop&w=800&q=85",
                "https://images.unsplash.com/photo-1506617420156-8e4536971650?auto=format&fit=crop&w=800&q=85",
                "https://images.unsplash.com/photo-1498579397066-22750a3cb424?auto=format&fit=crop&w=800&q=85"
            };

            var model = new MarketsViewModel();

            int imageIndex = 0;

            foreach (var market in markets)
            {
                var days = marketDays
                    .Where(md => md.MarketId == market.MarketId)
                    .ToList();

                var farmerCount = farmerMarkets
                    .Where(fm => fm.MarketId == market.MarketId)
                    .Select(fm => fm.FarmerId)
                    .Distinct()
                    .Count();

                model.Markets.Add(new MarketCardViewModel
                {
                    Market = market,

                    MarketDays = days,

                    FarmerCount = farmerCount,

                    ImageUrl = imageList[imageIndex % imageList.Length]
                });

                imageIndex++;
            }

            return View(model);
        }


        // =========================================================
        // MARKET DETAILS
        // =========================================================
        public async Task<IActionResult> Details(int id)
        {
            var market = await _context.markets
                .FirstOrDefaultAsync(m =>
                    m.MarketId == id &&
                    m.IsActive);

            if (market == null)
            {
                return NotFound();
            }

            ViewBag.MarketId = id;

            return View(market);
        }

        // =========================================================
        // PUBLIC OPENSTREETMAP - ALL ACTIVE MARKETS
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> Map()
        {
            var markets = await _context.markets
                .AsNoTracking()
                .Where(m =>
                    m.IsActive &&
                    m.Latitude.HasValue &&
                    m.Longitude.HasValue)
                .OrderBy(m => m.MarketName)
                .ToListAsync();

            return View(markets);
        }

    }
}
using System.Security.Claims;
using MarketLink.Models;
using MarketLink.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketLink.Controllers
{
    [Authorize(Roles = "Customer")]
    public class CustomerRecommendationsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ICustomerRecommendationService _recommendationService;

        public CustomerRecommendationsController(
            ApplicationDbContext context,
            ICustomerRecommendationService recommendationService)
        {
            _context = context;
            _recommendationService = recommendationService;
        }

        [HttpGet]
        public async Task<IActionResult> Index(
            CancellationToken cancellationToken)
        {
            var customer = await GetCurrentCustomerAsync(
                cancellationToken);

            if (customer == null)
                return Forbid();

            var model = await _recommendationService.GenerateAsync(
                customer,
                cancellationToken);

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Refresh(
            CancellationToken cancellationToken)
        {
            var customer = await GetCurrentCustomerAsync(
                cancellationToken);

            if (customer == null)
                return Forbid();

            await _recommendationService.GenerateAsync(
                customer,
                cancellationToken);

            TempData["RecommendationMessage"] =
                "Your recommendations were refreshed using your latest MarketLink activity.";

            return RedirectToAction(nameof(Index));
        }

        private async Task<Customer?> GetCurrentCustomerAsync(
            CancellationToken cancellationToken)
        {
            var value =
                User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? User.FindFirstValue("sub");

            if (!int.TryParse(value, out var userId))
                return null;

            return await _context.customers
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    c => c.UserId == userId,
                    cancellationToken);
        }
    }
}

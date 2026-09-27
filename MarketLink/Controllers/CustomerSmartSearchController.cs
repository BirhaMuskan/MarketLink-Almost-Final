using System.Security.Claims;
using MarketLink.Services;
using MarketLink.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MarketLink.Controllers
{
    [Authorize(Roles = "Customer")]
    public class CustomerSmartSearchController : Controller
    {
        private readonly IIntelligentSearchService _searchService;

        public CustomerSmartSearchController(
            IIntelligentSearchService searchService)
        {
            _searchService = searchService;
        }

        [HttpGet]
        public IActionResult Index()
        {
            return View(
                new CustomerSmartSearchPageViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Search(
            CustomerSmartSearchPageViewModel input,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(input.Query))
            {
                ModelState.AddModelError(
                    nameof(input.Query),
                    "Tell MarketLink what you are looking for.");

                return View(
                    "Index",
                    new CustomerSmartSearchPageViewModel
                    {
                        Query = input.Query
                    });
            }

            var userId = GetCurrentUserId();

            var model = await _searchService.SearchAsync(
                input.Query,
                userId,
                cancellationToken);

            return View(
                "Index",
                model);
        }

        private int? GetCurrentUserId()
        {
            var value =
                User.FindFirstValue(
                    ClaimTypes.NameIdentifier)
                ?? User.FindFirstValue("sub");

            return int.TryParse(value, out var userId)
                ? userId
                : null;
        }
    }
}

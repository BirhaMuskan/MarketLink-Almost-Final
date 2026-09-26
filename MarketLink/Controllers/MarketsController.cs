using Microsoft.AspNetCore.Mvc;

namespace MarketLink.Controllers
{
    public class MarketsController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
        // Market Details page
        public IActionResult Details(int id)
        {
            ViewBag.MarketId = id;

            return View();
        }
    }
}

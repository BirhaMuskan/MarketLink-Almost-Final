using Microsoft.AspNetCore.Mvc;

namespace MarketLink.Controllers
{
    public class ProductsController : Controller
    {
        // Products main page
        public IActionResult Index()
        {
            return View();
        }

        // Product details page
        public IActionResult Details(int id)
        {
            ViewBag.ProductId = id;

            return View();
        }
    }
}
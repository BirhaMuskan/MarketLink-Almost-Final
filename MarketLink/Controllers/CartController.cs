using Microsoft.AspNetCore.Mvc;

namespace MarketLink.Controllers
{
    public class CartController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}

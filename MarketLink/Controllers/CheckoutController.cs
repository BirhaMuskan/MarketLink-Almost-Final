using Microsoft.AspNetCore.Mvc;

namespace MarketLink.Controllers
{
    public class CheckoutController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}

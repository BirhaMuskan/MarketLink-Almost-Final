using Microsoft.AspNetCore.Mvc;

namespace MarketLink.Controllers
{
    public class OrderController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
        public IActionResult History()
        {
            return View();
        }
    }
}

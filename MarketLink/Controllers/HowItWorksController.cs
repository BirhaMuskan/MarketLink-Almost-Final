using Microsoft.AspNetCore.Mvc;

namespace MarketLink.Controllers
{
    public class HowItWorksController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
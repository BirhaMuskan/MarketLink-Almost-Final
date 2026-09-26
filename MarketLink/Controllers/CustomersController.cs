using Microsoft.AspNetCore.Mvc;

namespace MarketLink.Controllers
{
    public class CustomersController : Controller
    {
        public IActionResult Dashboard()
        {
            return View();
        }
    }
}

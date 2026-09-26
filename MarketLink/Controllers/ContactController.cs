using Microsoft.AspNetCore.Mvc;

namespace MarketLink.Controllers
{
    public class ContactController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}

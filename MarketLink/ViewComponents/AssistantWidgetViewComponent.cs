using Microsoft.AspNetCore.Mvc;

namespace MarketLink.ViewComponents
{
    public class AssistantWidgetViewComponent : ViewComponent
    {
        public IViewComponentResult Invoke()
        {
            var controller =
                ViewContext.RouteData.Values["controller"]?.ToString();

            // The full assistant page already provides the complete chat UI.
            if (string.Equals(
                    controller,
                    "CustomerAssistant",
                    StringComparison.OrdinalIgnoreCase))
            {
                return Content("");
            }

            return View();
        }
    }
}

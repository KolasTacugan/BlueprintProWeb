using Microsoft.AspNetCore.Mvc;

namespace BlueprintProWeb.Controllers
{
    public class MarketplaceController : Controller
    {
        public IActionResult BlueprintMarketplace()
        {
            return View();
        }
    }
}

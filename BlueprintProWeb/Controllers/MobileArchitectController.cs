using Microsoft.AspNetCore.Mvc;

namespace BlueprintProWeb.Controllers
{
    public class MobileArchitectController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}

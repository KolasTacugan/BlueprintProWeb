using Microsoft.AspNetCore.Mvc;

namespace BlueprintProWeb.Controllers.ArchitectSide
{
    public class ArchitectInterface : Controller
    {
        public IActionResult ArchitectDashboard()
        {
            return View();
        }
    }
}

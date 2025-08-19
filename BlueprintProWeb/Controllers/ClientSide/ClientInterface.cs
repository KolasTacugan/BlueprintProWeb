using Microsoft.AspNetCore.Mvc;

namespace BlueprintProWeb.Controllers.ClientSide
{
    public class ClientInterface : Controller
    {
        public IActionResult ClientDashboard()
        {
            return View();
        }
    }
}

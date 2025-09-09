using Microsoft.AspNetCore.Mvc;
using YourAppNamespace.ViewModels;

namespace YourAppNamespace.Controllers
{
    public class SettingsController : Controller
    {
        public IActionResult Settings()
        {
            var model = new SettingsViewModel
            {
                FullName = "Samantha Grace",
                Email = "samantha@email.com",
                Language = "en",
                DarkMode = false
            };

            return View(model);
        }

        [HttpPost]
        public IActionResult Save(SettingsViewModel model)
        {
            if (ModelState.IsValid)
            {
                TempData["Message"] = "Settings saved successfully!";
                return RedirectToAction("Settings");
            }

            return View("Settings", model);
        }
    }
}

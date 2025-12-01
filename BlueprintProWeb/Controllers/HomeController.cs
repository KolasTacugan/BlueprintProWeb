using System.Diagnostics;
using BlueprintProWeb.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlueprintProWeb.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;

        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
        }

        public IActionResult Index()
        {
            return View();
        }
        
        [Authorize]
        public IActionResult Privacy()
        {
            return View();
        }

        // Demo action for loading spinner
        public IActionResult LoadingDemo()
        {
            return View();
        }

        // Test form submission with delay
        [HttpPost]
        public async Task<IActionResult> TestForm(string testInput)
        {
            // Simulate processing delay
            await Task.Delay(2000);
            
            TempData["Message"] = $"Form submitted successfully! Input: {testInput}";
            return RedirectToAction("LoadingDemo");
        }

        // Test AJAX endpoint with delay
        [HttpPost]
        public async Task<IActionResult> TestEndpoint([FromBody] object data)
        {
            // Check if it's a quick test
            var jsonData = data?.ToString();
            if (jsonData != null && jsonData.Contains("quick"))
            {
                // No delay for quick test
                return Json(new { success = true, message = "Quick AJAX call completed", data, responseTime = "< 100ms" });
            }
            
            // Normal delay for regular tests
            await Task.Delay(1500);
            
            return Json(new { success = true, message = "AJAX call completed successfully", data, responseTime = "1500ms" });
        }

        // Quick test endpoint with minimal delay
        [HttpPost]
        public IActionResult QuickTest()
        {
            return Json(new { success = true, message = "Quick test completed", timestamp = DateTime.Now });
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}

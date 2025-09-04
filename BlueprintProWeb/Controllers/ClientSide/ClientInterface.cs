using BlueprintProWeb.Data;
using BlueprintProWeb.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BlueprintProWeb.Controllers.ClientSide
{
    public class ClientInterfaceController : Controller
    {
        private readonly AppDbContext context;
        private readonly UserManager<User> userManager;

        public ClientInterfaceController(AppDbContext _context, UserManager<User> _userManager)
        {
            context = _context;
            userManager = _userManager;
        }

        public IActionResult ClientDashboard()
        {
            return View();
        }

        public IActionResult BlueprintMarketplace()
        {
            var blueprints = context.Blueprints.ToList();
            return View("BlueprintMarketplace", blueprints);
        }

        public async Task<IActionResult> Matches()
        {
            var currentUser = await userManager.GetUserAsync(User);

            if (currentUser == null)
            {
                // If not logged in, redirect to login
                return RedirectToAction("Login", "Account");
            }

            // Get all architects
            var architects = context.Users
                .Where(u => u.user_role == "Architect")
                .AsQueryable();

            // Apply filters only if client has preferences
            if (!string.IsNullOrEmpty(currentUser.user_Style))
                architects = architects.Where(a => a.user_Style == currentUser.user_Style);

            if (!string.IsNullOrEmpty(currentUser.user_Location))
                architects = architects.Where(a => a.user_Location == currentUser.user_Location);

            if (!string.IsNullOrEmpty(currentUser.user_Budget))
                architects = architects.Where(a => a.user_Budget == currentUser.user_Budget);

            // Order by rating (best matches first)
            var matches = architects
                .OrderByDescending(a => a.user_Rating)
                .ToList();

            return View(matches);
        }
    }
}

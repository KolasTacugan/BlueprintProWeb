using BlueprintProWeb.Data;
using BlueprintProWeb.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BlueprintProWeb.Controllers
{
    public class MarketplaceController : Controller
    {
        private readonly AppDbContext context;

        public MarketplaceController(AppDbContext _context)
        {
            context = _context;
        }

        public IActionResult BlueprintMarketplace()
        {
            // Only show blueprints that are for sale and not yet purchased
            var blueprints = context.Blueprints
                .Where(b => b.blueprintIsForSale && b.clentId == null)
                .ToList();

            return View("BlueprintMarketplace", blueprints);
        }
    }
}

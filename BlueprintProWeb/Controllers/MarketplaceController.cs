using BlueprintProWeb.Data;
using BlueprintProWeb.Models;
using BlueprintProWeb.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net;

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
            var blueprints = context.Blueprints.ToList();
            return View("BlueprintMarketplace", blueprints);
        }
        
    }
}

using BlueprintProWeb.Data;
using BlueprintProWeb.Models;
using BlueprintProWeb.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace BlueprintProWeb.Controllers.ArchitectSide
{
    public class ArchitectInterface : Controller
    {
        private readonly AppDbContext context;
        private readonly UserManager<User> _userManager;
        public IWebHostEnvironment WebHostEnvironment;

        public ArchitectInterface(AppDbContext context, IWebHostEnvironment webHostEnvironment, UserManager<User> userManager)
        {
            this.context = context;
            WebHostEnvironment = webHostEnvironment;
            _userManager = userManager;
        }

        public IActionResult ArchitectDashboard()
        {
            return View();
        }

        public IActionResult Blueprints()
        {
            var blueprints = context.Blueprints
                .Where(bp => bp.blueprintIsForSale)
                .ToList();
            return View(blueprints);
        }
        public IActionResult Projects()
        {
            var blueprints = context.Blueprints
                .Where(bp => !bp.blueprintIsForSale)
                .ToList();
            return View(blueprints);
        }
        [HttpGet]
        public async Task<IActionResult> AddBlueprints()
        {
            var clients = await _userManager.Users
                .Where(u => u.user_role == "Client")
                .ToListAsync();

            var vm = new BlueprintViewModel
            {
                Clients = clients.Select(c => new SelectListItem
                {
                    Value = c.Id, // will become Project.user_clientId
                    Text = $"{c.user_fname} {c.user_lname}"
                }).ToList()
            };

            return View(vm);
        }

        [HttpPost]
        public async Task<IActionResult> AddBlueprints(BlueprintViewModel vm)
        {
            string stringFileName = UploadFile(vm);
            var user = await _userManager.GetUserAsync(User);
            var userId = user.Id;

            var blueprint = new Blueprint
            {
                blueprintImage = stringFileName,
                blueprintName = vm.blueprintName,
                blueprintPrice = vm.blueprintPrice,
                blueprintDescription = vm.blueprintDescription,
                blueprintStyle = vm.blueprintStyle,
                blueprintIsForSale = vm.blueprintIsForSale,
                architectId = userId
            };
            context.Blueprints.Add(blueprint);
            await context.SaveChangesAsync();

            return RedirectToAction("Blueprints");
        }

        public IActionResult AddProjectBlueprints()
        {
            return View();
        }
        [HttpPost]
        public async Task<IActionResult> AddProjectBlueprints(BlueprintViewModel vm)
        {
            string stringFileName = UploadFile(vm);
            var user = await _userManager.GetUserAsync(User);
            var userId = user.Id;

            var blueprint = new Blueprint
            {
                blueprintImage = stringFileName,
                blueprintName = vm.blueprintName,
                blueprintPrice = 0,
                blueprintDescription = vm.blueprintDescription,
                blueprintStyle = vm.blueprintStyle,
                blueprintIsForSale = vm.blueprintIsForSale,
                architectId = userId
            };
            context.Blueprints.Add(blueprint);
            await context.SaveChangesAsync();

            var project = new Project
            {
                blueprint_Id = blueprint.blueprintId,              
                project_Title = vm.blueprintName,                  
                user_architectId = userId,                         
                user_clientId = vm.clentId,                              
                project_Status = "Draft",
                project_Budget = vm.blueprintPrice.ToString()
            };
            context.Projects.Add(project);
            await context.SaveChangesAsync();


            return RedirectToAction("Projects");
        }

        private string UploadFile(BlueprintViewModel vm)
        {
            string fileName = null;
            if (vm.BlueprintImage != null)
            {
                string uploadDir = Path.Combine(WebHostEnvironment.WebRootPath, "images");
                fileName = Guid.NewGuid().ToString() + "-" + vm.BlueprintImage.FileName;
                string filePath = Path.Combine(uploadDir, fileName);
                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    vm.BlueprintImage.CopyTo(fileStream);
                }
            }
            return fileName;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult EditBlueprint(BlueprintViewModel vm)
        {
            var blueprint = context.Blueprints.Find(vm.blueprintId);
            if (blueprint == null) return NotFound();

            blueprint.blueprintName = vm.blueprintName;
            blueprint.blueprintPrice = vm.blueprintPrice;
            blueprint.blueprintStyle = vm.blueprintStyle;

            if (vm.BlueprintImage != null)
            {
                if (!string.IsNullOrEmpty(blueprint.blueprintImage))
                {
                    var oldPath = Path.Combine(WebHostEnvironment.WebRootPath, "images", blueprint.blueprintImage);
                    if (System.IO.File.Exists(oldPath)) System.IO.File.Delete(oldPath);
                }

                string newFile = UploadFile(vm);
                blueprint.blueprintImage = newFile;
            }

            context.SaveChanges();
            return RedirectToAction("Blueprints");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteBlueprint(int blueprintId)
        {
            var blueprint = context.Blueprints.Find(blueprintId);
            if (blueprint == null) return NotFound();

            // optional: delete file from disk
            if (!string.IsNullOrEmpty(blueprint.blueprintImage))
            {
                var path = Path.Combine(WebHostEnvironment.WebRootPath, "images", blueprint.blueprintImage);
                if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            }

            context.Blueprints.Remove(blueprint);
            context.SaveChanges();

            return RedirectToAction("Blueprints");
        }

        [HttpPost]
        [Authorize(Roles = "Architect")]
        public async Task<IActionResult> RespondMatch(string matchId, bool approve)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null) return Unauthorized();

            var match = await context.Matches.FindAsync(matchId);
            if (match == null) return NotFound();

            if (match.ArchitectId != currentUser.Id) return Forbid();

            match.MatchStatus = approve ? "Approved" : "Declined";
            await context.SaveChangesAsync();

            return Json(new { success = true, status = match.MatchStatus });
        }

        [Authorize(Roles = "Architect")]
        public IActionResult PendingMatches()
        {
            var currentUserId = _userManager.GetUserId(User);

            var pending = context.Matches
                .Where(m => m.ArchitectId == currentUserId && m.MatchStatus == "Pending")
                .Select(m => new MatchViewModel
                {
                    MatchId = m.MatchId,
                    ClientId = m.ClientId,
                    ClientName = m.Client.user_fname + " " + m.Client.user_lname,
                    MatchDate = m.MatchDate,
                    MatchStatus = m.MatchStatus
                })
                .ToList();

            return View(pending);
        }

    }
}
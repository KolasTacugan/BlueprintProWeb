using BlueprintProWeb.Data;
using BlueprintProWeb.Models;
using BlueprintProWeb.ViewModels;
using iText.Commons.Actions.Contexts;
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

        public async Task<IActionResult> ArchitectDashboard()
        {
            var currentUser = await _userManager.GetUserAsync(User);
            ViewData["UserFirstName"] = currentUser?.user_fname ?? "User";
            return View();
        }

        public async Task<IActionResult> Blueprints()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            var blueprints = context.Blueprints
                .Where(bp => bp.blueprintIsForSale && bp.architectId == user.Id)
                .ToList();

            return View(blueprints);
        }
        public async Task<IActionResult> Projects()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            var blueprints = context.Blueprints
                .Where(bp => !bp.blueprintIsForSale && bp.architectId == user.Id)
                .ToList();

            return View(blueprints);
        }

        [HttpGet]
        public async Task<IActionResult> AddBlueprints()
        {
            var user = await _userManager.GetUserAsync(User); 

            var approvedMatches = await context.Matches
                .Where(m => m.ArchitectId == user.Id && m.MatchStatus == "Approved")
                .Include(m => m.Client) 
                .ToListAsync();

            var clients = approvedMatches.Select(m => new SelectListItem
            {
                Value = m.Client.Id,
                Text = $"{m.Client.user_fname} {m.Client.user_lname}"
            }).ToList();

            var viewModel = new BlueprintViewModel
            {
                Clients = clients
            };

            return View(viewModel);
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

        [HttpPost("ArchitectInterface/RespondMatch")]
        public async Task<IActionResult> RespondMatch([FromBody] MatchResponseDto dto)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null) return Unauthorized();

            var match = await context.Matches.FindAsync(dto.MatchId);
            if (match == null) return NotFound();

            if (match.ArchitectId != currentUser.Id) return Forbid();

            match.MatchStatus = dto.Approve ? "Approved" : "Declined";
            await context.SaveChangesAsync();

            return Json(new { success = true, status = match.MatchStatus });
        }

        public class MatchResponseDto
        {
            public string MatchId { get; set; }
            public bool Approve { get; set; }
        }

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
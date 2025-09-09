using BlueprintProWeb.Data;
using BlueprintProWeb.Models;
using BlueprintProWeb.ViewModels;
using Microsoft.AspNetCore.Mvc;
using System.Net;
using System.Security.Claims;

namespace BlueprintProWeb.Controllers.ArchitectSide
{
    public class ArchitectInterface : Controller
    {
        private readonly AppDbContext context;
        private readonly IWebHostEnvironment WebHostEnvironment;

        public ArchitectInterface(AppDbContext context, IWebHostEnvironment webHostEnvironment)
        {
            this.context = context;
            WebHostEnvironment = webHostEnvironment;
        }

        // Dashboard
        public IActionResult ArchitectDashboard()
        {
            return View();
        }

        // List all blueprints for logged-in architect
        public IActionResult Blueprints()
        {
            string architectId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var blueprints = context.Blueprints
                                    .Where(b => b.ArchitectId == architectId)
                                    .ToList();
            return View(blueprints);
        }

        // GET: Add Blueprint
        public IActionResult AddBlueprints()
        {
            return View();
        }

        // POST: Add Blueprint
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AddBlueprints(BlueprintViewModel vm)
        {
            string stringFileName = UploadFile(vm);
            string architectId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var blueprint = new Blueprint
            {
                blueprintImage = stringFileName,
                blueprintName = vm.blueprintName,
                blueprintPrice = vm.blueprintPrice,
                blueprintDescription = vm.blueprintDescription,
                blueprintStyle = vm.blueprintStyle,
                ArchitectId = architectId,
                Blueprint_CreatedDate = DateTime.UtcNow
            };

            context.Blueprints.Add(blueprint);
            context.SaveChanges();

            return RedirectToAction("Blueprints");
        }

        // Upload helper
        private string UploadFile(BlueprintViewModel vm)
        {
            string fileName = null;
            if (vm.BlueprintImage != null)
            {
                string uploadDir = Path.Combine(WebHostEnvironment.WebRootPath, "images");
                if (!Directory.Exists(uploadDir))
                {
                    Directory.CreateDirectory(uploadDir);
                }

                fileName = Guid.NewGuid().ToString() + "-" + vm.BlueprintImage.FileName;
                string filePath = Path.Combine(uploadDir, fileName);

                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    vm.BlueprintImage.CopyTo(fileStream);
                }
            }
            return fileName;
        }

        // Edit Blueprint
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult EditBlueprint(BlueprintViewModel vm)
        {
            string architectId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var blueprint = context.Blueprints
                                   .FirstOrDefault(b => b.blueprintId == vm.blueprintId && b.ArchitectId == architectId);
            if (blueprint == null) return NotFound();

            blueprint.blueprintName = vm.blueprintName;
            blueprint.blueprintPrice = vm.blueprintPrice;
            blueprint.blueprintStyle = vm.blueprintStyle;
            blueprint.blueprintDescription = vm.blueprintDescription;

            // Replace image only if new one selected
            if (vm.BlueprintImage != null)
            {
                // delete old file
                if (!string.IsNullOrEmpty(blueprint.blueprintImage))
                {
                    var oldPath = Path.Combine(WebHostEnvironment.WebRootPath, "images", blueprint.blueprintImage);
                    if (System.IO.File.Exists(oldPath))
                        System.IO.File.Delete(oldPath);
                }

                string newFile = UploadFile(vm);
                blueprint.blueprintImage = newFile;
            }

            context.SaveChanges();
            return RedirectToAction("Blueprints");
        }

        // Delete Blueprint
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteBlueprint(int blueprintId)
        {
            string architectId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var blueprint = context.Blueprints
                                   .FirstOrDefault(b => b.blueprintId == blueprintId && b.ArchitectId == architectId);
            if (blueprint == null) return NotFound();

            // delete file from disk
            if (!string.IsNullOrEmpty(blueprint.blueprintImage))
            {
                var path = Path.Combine(WebHostEnvironment.WebRootPath, "images", blueprint.blueprintImage);
                if (System.IO.File.Exists(path))
                    System.IO.File.Delete(path);
            }

            context.Blueprints.Remove(blueprint);
            context.SaveChanges();

            return RedirectToAction("Blueprints");
        }
    }
}

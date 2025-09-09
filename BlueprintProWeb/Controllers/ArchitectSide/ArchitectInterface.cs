using BlueprintProWeb.Data;
using BlueprintProWeb.Models;
using BlueprintProWeb.ViewModels;
using Microsoft.AspNetCore.Mvc;
using System.Net;

namespace BlueprintProWeb.Controllers.ArchitectSide
{
    public class ArchitectInterface : Controller
    {
        private readonly AppDbContext context;

        public IWebHostEnvironment WebHostEnvironment;

        public ArchitectInterface(AppDbContext context, IWebHostEnvironment webHostEnvironment)
        {
            this.context = context;
            WebHostEnvironment = webHostEnvironment;
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
        public IActionResult AddBlueprints()
        {
            return View();
        }
        [HttpPost]
        public IActionResult AddBlueprints(BlueprintViewModel vm)
        {
            string stringFileName = UploadFile(vm);
            var blueprint = new Blueprint
            {
                blueprintImage = stringFileName,
                blueprintName = vm.blueprintName,
                blueprintPrice = vm.blueprintPrice,
                blueprintDescription = vm.blueprintDescription,
                blueprintStyle = vm.blueprintStyle,
                blueprintIsForSale = vm.blueprintIsForSale
            };
            context.Blueprints.Add(blueprint);
            context.SaveChanges();

            return RedirectToAction("Blueprints");
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

            // Replace image only if a new one was selected
            if (vm.BlueprintImage != null)
            {
                // optional: delete old file
                if (!string.IsNullOrEmpty(blueprint.blueprintImage))
                {
                    var oldPath = Path.Combine(WebHostEnvironment.WebRootPath, "images", blueprint.blueprintImage);
                    if (System.IO.File.Exists(oldPath)) System.IO.File.Delete(oldPath);
                }

                string newFile = UploadFile(vm); // reuse your existing method
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

    }
}
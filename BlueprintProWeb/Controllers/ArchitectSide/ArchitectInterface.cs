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

        public ArchitectInterface(AppDbContext context, IWebHostEnvironment webHostEnvironment) {
            this.context = context;
            WebHostEnvironment = webHostEnvironment;
        }

        public IActionResult ArchitectDashboard()
        {
            return View();
        }

        public IActionResult Blueprints()
        {
            var blueprints = context.Blueprints.ToList();
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
                blueprintStyle = vm.blueprintStyle
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
                fileName = Guid.NewGuid().ToString()+"-"+vm.BlueprintImage.FileName;
                string filePath = Path.Combine(uploadDir, fileName);
                using (var fileStream = new FileStream(filePath, FileMode.Create)) {
                    vm.BlueprintImage.CopyTo(fileStream);
                }
            }
            return fileName;
        }
    }
}

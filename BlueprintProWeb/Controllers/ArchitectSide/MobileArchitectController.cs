using BlueprintProWeb.Data;
using BlueprintProWeb.Models;
using iText.Commons.Actions.Contexts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace BlueprintProWeb.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class MobileArchitectController : ControllerBase
    {
        private readonly AppDbContext context;
        private readonly UserManager<User> userManager;
        private readonly IWebHostEnvironment env;

        public MobileArchitectController(AppDbContext context, UserManager<User> userManager, IWebHostEnvironment env)
        {
            this.context = context;
            this.userManager = userManager;
            this.env = env;
        }

        [HttpGet("blueprints/{architectId}")]
        public IActionResult GetArchitectBlueprints(string architectId)
        {
            if (string.IsNullOrEmpty(architectId))
                return BadRequest(new { message = "Architect ID is required." });

            var blueprints = context.Blueprints
                .Where(bp => bp.blueprintIsForSale && bp.architectId == architectId)
                .Select(bp => new
                {
                    bp.blueprintId,
                    bp.blueprintName,
                    bp.blueprintPrice,
                    bp.blueprintStyle,
                    blueprintImage = $"{Request.Scheme}://{Request.Host}/images/{bp.blueprintImage}"
                })
                .ToList();

            return Ok(blueprints);
        }

        [HttpPost("AddMarketplaceBlueprint")]
        public async Task<IActionResult> AddMarketplaceBlueprint(
            [FromForm] string BlueprintName,
            [FromForm] string BlueprintPrice,
            [FromForm] string BlueprintDescription,
            [FromForm] string BlueprintStyle,
            [FromForm] string IsForSale,
            [FromForm] string ArchitectId,
            [FromForm] IFormFile BlueprintImage)
        {
            if (string.IsNullOrEmpty(ArchitectId))
                return BadRequest(new { message = "ArchitectId is missing" });

            if (BlueprintImage == null || BlueprintImage.Length == 0)
                return BadRequest(new { message = "No image uploaded" });

            if (!int.TryParse(BlueprintPrice, out int parsedPrice))
                return BadRequest(new { message = "Invalid price format" });

            // 🖼️ Upload and watermark image
            string fileName = UploadMobileFile(BlueprintImage);

            var blueprint = new Blueprint
            {
                blueprintName = BlueprintName,
                blueprintDescription = BlueprintDescription,
                blueprintStyle = BlueprintStyle,
                blueprintImage = fileName,
                blueprintPrice = parsedPrice,
                blueprintIsForSale = IsForSale == "true",
                architectId = ArchitectId
            };

            context.Blueprints.Add(blueprint);
            await context.SaveChangesAsync();

            return Ok(new { message = "Blueprint uploaded successfully" });
        }

        private string UploadMobileFile(IFormFile file, string? oldFileName = null)
        {
            string fileName = null;
            if (file != null)
            {
                string uploadDir = Path.Combine(env.WebRootPath, "images");
                Directory.CreateDirectory(uploadDir);

                // 🧠 Extract extension properly
                string originalExt = Path.GetExtension(file.FileName);
                if (string.IsNullOrEmpty(originalExt) || originalExt.Equals(".tmp", StringComparison.OrdinalIgnoreCase))
                {
                    originalExt = ".jpg";
                }

                fileName = Guid.NewGuid().ToString() + originalExt;
                string filePath = Path.Combine(uploadDir, fileName);

                if (!string.IsNullOrEmpty(oldFileName))
                {
                    var oldPath = Path.Combine(uploadDir, oldFileName);
                    if (System.IO.File.Exists(oldPath))
                    {
                        try { System.IO.File.Delete(oldPath); } catch { }
                    }
                }

                using var memoryStream = new MemoryStream();
                file.CopyTo(memoryStream);
                memoryStream.Position = 0;

                using (var image = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(memoryStream))
                {
                    // Optional watermark
                    string watermarkPath = Path.Combine(env.WebRootPath, "images", "BPP-watermark.png");
                    if (System.IO.File.Exists(watermarkPath))
                    {
                        using var watermarkImg = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(watermarkPath);
                        watermarkImg.Mutate(w => w.Resize(image.Width, image.Height));
                        float opacity = 0.28f;
                        image.Mutate(ctx => ctx.DrawImage(watermarkImg, new Point(0, 0), opacity));
                    }

                    image.Save(filePath);  // ✅ now ImageSharp knows the extension
                }
            }
            return fileName;
        }

        [HttpGet("getProjects/{architectId}")]
        public async Task<IActionResult> GetProjects(string architectId)
        {
            var projects = await context.Projects
                .Where(p => p.user_architectId == architectId)
                .Include(p => p.Blueprint)
                .Include(p => p.Client)
                .Select(p => new
                {
                    p.project_Id,
                    p.project_Title,
                    p.project_Budget,
                    p.project_Status,
                    p.blueprint_Id,
                    blueprintImage = p.Blueprint.blueprintImage != null
                        ? $"{Request.Scheme}://{Request.Host}/images/{p.Blueprint.blueprintImage}"
                        : null,
                    clientName = p.Client.user_fname + " " + p.Client.user_lname
                })
                .ToListAsync();

            return Ok(projects);
        }

        [HttpGet("clientsForProject/{architectId}")]
        public async Task<IActionResult> GetClientsForProject(string architectId)
        {
            if (string.IsNullOrEmpty(architectId))
                return BadRequest(new { message = "Architect ID is required" });

            var approvedMatches = await context.Matches
                .Where(m => m.ArchitectId == architectId && m.MatchStatus == "Approved")
                .Include(m => m.Client)
                .ToListAsync();

            var clients = approvedMatches.Select(m => new
            {
                clientId = m.Client.Id,
                clientName = $"{m.Client.user_fname} {m.Client.user_lname}"
            });

            return Ok(clients);
        }

        [HttpPost("addProjectBlueprint")]
        public async Task<IActionResult> AddProjectBlueprint(
            [FromForm] string architectId,                   // ✅ added architectId
            [FromForm] string blueprintName,
            [FromForm] string blueprintPrice,
            [FromForm] string blueprintDescription,
            [FromForm] string clientId,                      // ✅ fixed spelling
            [FromForm] DateTime projectTrack_dueDate,
            [FromForm] IFormFile BlueprintImage
        )
        {
            if (string.IsNullOrEmpty(architectId))
                return BadRequest(new { message = "Architect ID is required" });

            if (BlueprintImage == null || BlueprintImage.Length == 0)
                return BadRequest(new { message = "No blueprint image uploaded" });

            string fileName = UploadProjectFile(BlueprintImage);

            var blueprint = new Blueprint
            {
                blueprintName = blueprintName,
                blueprintDescription = blueprintDescription,
                blueprintPrice = int.Parse(blueprintPrice),
                blueprintImage = fileName,
                blueprintIsForSale = false,
                architectId = architectId   // ✅ use architectId directly
            };

            context.Blueprints.Add(blueprint);
            await context.SaveChangesAsync();

            var project = new Project
            {
                blueprint_Id = blueprint.blueprintId,
                project_Title = blueprintName,
                user_architectId = architectId,   // ✅ use architectId directly
                user_clientId = clientId,
                project_Status = "Ongoing",
                project_Budget = blueprintPrice
            };

            context.Projects.Add(project);
            await context.SaveChangesAsync();

            var tracker = new ProjectTracker
            {
                project_Id = project.project_Id,
                project_Title = project.project_Title,
                blueprint_Description = blueprintDescription,
                projectTrack_dueDate = projectTrack_dueDate,
                projectTrack_currentFileName = BlueprintImage.FileName,
                projectTrack_currentFilePath = "/images/" + fileName,
                projectTrack_currentRevision = 1
            };

            context.ProjectTrackers.Add(tracker);
            await context.SaveChangesAsync();

            return Ok(new { message = "Project blueprint uploaded successfully" });
        }

        private string UploadProjectFile(IFormFile file)
        {
            string fileName = null;
            if (file != null)
            {
                string uploadDir = Path.Combine(env.WebRootPath, "images");
                Directory.CreateDirectory(uploadDir);

                string originalExt = Path.GetExtension(file.FileName);
                if (string.IsNullOrEmpty(originalExt) || originalExt.Equals(".tmp", StringComparison.OrdinalIgnoreCase))
                {
                    originalExt = ".jpg";
                }

                fileName = Guid.NewGuid().ToString() + originalExt;
                string filePath = Path.Combine(uploadDir, fileName);

                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    file.CopyTo(fileStream);
                }
            }
            return fileName;
        }
    }
}

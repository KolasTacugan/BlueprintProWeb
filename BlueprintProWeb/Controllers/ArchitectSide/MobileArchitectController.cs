using BlueprintProWeb.Data;
using BlueprintProWeb.Models;
using iText.Commons.Actions.Contexts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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

        [AllowAnonymous]
        [HttpPost("AddMarketplaceBlueprint")]
        public async Task<IActionResult> AddMarketplaceBlueprint(
        [FromForm] string BlueprintName,
        [FromForm] string BlueprintPrice, // 👈 changed from int to string
        [FromForm] string BlueprintDescription,
        [FromForm] string BlueprintStyle,
        [FromForm] string IsForSale,
        [FromForm] IFormFile BlueprintImage)
        {
            try
            {
                // ✅ Validate the uploaded image
                if (BlueprintImage == null || BlueprintImage.Length == 0)
                {
                    return BadRequest(new { message = "No image uploaded" });
                }

                // ✅ Parse price safely
                if (!int.TryParse(BlueprintPrice, out int parsedPrice))
                {
                    return BadRequest(new { message = "Invalid price format" });
                }

                // ✅ Make sure env is injected properly
                var uploadDir = Path.Combine(env.WebRootPath, "uploads", "market");
                if (!Directory.Exists(uploadDir))
                    Directory.CreateDirectory(uploadDir);

                // ✅ Generate unique file name to avoid overwrites
                var fileName = $"{Guid.NewGuid()}_{Path.GetFileName(BlueprintImage.FileName)}";
                var filePath = Path.Combine(uploadDir, fileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await BlueprintImage.CopyToAsync(stream);
                }

                // ✅ Save record in the DB
                var blueprint = new Blueprint
                {
                    blueprintName = BlueprintName,
                    blueprintDescription = BlueprintDescription,
                    blueprintStyle = BlueprintStyle,
                    blueprintImage = $"/uploads/market/{fileName}",
                    blueprintPrice = parsedPrice,
                    blueprintIsForSale = IsForSale == "true",
                    // ArchitectId if needed
                };

                context.Blueprints.Add(blueprint);
                await context.SaveChangesAsync();

                return Ok(new { message = "Blueprint uploaded successfully" });
            }
            catch (Exception ex)
            {
                // ❗ Return full error to help debug
                return StatusCode(500, new { message = "Upload failed", error = ex.ToString() });
            }
        }

    }
}

﻿using BlueprintProWeb.Data;
using BlueprintProWeb.Hubs;
using BlueprintProWeb.Models;
using iText.Commons.Actions.Contexts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using static BlueprintProWeb.Controllers.MobileClientController;

namespace BlueprintProWeb.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class MobileArchitectController : ControllerBase
    {
        private readonly AppDbContext context;
        private readonly UserManager<User> userManager;
        private readonly IWebHostEnvironment env;
        private readonly IHubContext<ChatHub> _hubContext;

        public MobileArchitectController(AppDbContext context, UserManager<User> userManager, IWebHostEnvironment env, IHubContext<ChatHub> hubContext)
        {
            this.context = context;
            this.userManager = userManager;
            this.env = env;
            _hubContext = hubContext;
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

        [HttpGet("Architect/Messages/All")]
        [AllowAnonymous]
        public async Task<IActionResult> GetAllMessagesForArchitect([FromQuery] string architectId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(architectId))
                    return BadRequest(new { success = false, message = "ArchitectId is required." });

                var conversations = await context.Messages
                    .Where(m => m.ArchitectId == architectId || m.ClientId == architectId)
                    .GroupBy(m => m.ClientId)
                    .Select(g => new
                    {
                        ClientId = g.Key,
                        ClientName = context.Users
                            .Where(u => u.Id == g.Key)
                            .Select(u => u.user_fname + " " + u.user_lname)
                            .FirstOrDefault(),
                        LastMessage = g.OrderByDescending(m => m.MessageDate)
                            .Select(m => m.MessageBody)
                            .FirstOrDefault(),
                        LastMessageTime = g.Max(m => m.MessageDate),
                        ProfileUrl = context.Users
                            .Where(u => u.Id == g.Key)
                            .Select(u => u.user_profilePhoto)
                            .FirstOrDefault(),
                        UnreadCount = g.Count(x => x.ClientId == g.Key && !x.IsRead)
                    })
                    .OrderByDescending(x => x.LastMessageTime)
                    .ToListAsync();

                return Ok(new { success = true, messages = conversations });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }


        [HttpGet("ArchitectMatches")]
        [AllowAnonymous]
        public async Task<IActionResult> GetAllMatchesForArchitect([FromQuery] string architectId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(architectId))
                    return BadRequest(new { success = false, message = "ArchitectId is required." });

                var matches = await context.Matches
                    .Where(m => m.ArchitectId == architectId)
                    .Include(m => m.Client)
                    .Select(m => new
                    {
                        MatchId = m.MatchId,
                        ClientId = m.Client.Id,
                        ClientName = m.Client.user_fname + " " + m.Client.user_lname,
                        ClientLocation = m.Client.user_Location,
                        ClientStyle = m.Client.user_Style,
                        ClientBudget = m.Client.user_Budget,
                        ClientPhoto = m.Client.user_profilePhoto,
                        MatchStatus = "Matched"
                    })
                    .ToListAsync();

                return Ok(new { success = true, matches });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        [HttpGet("Architect/Messages")]
        [AllowAnonymous] // or [Authorize] if you add token auth later
        public async Task<IActionResult> GetMessagesForArchitect([FromQuery] string architectId, [FromQuery] string clientId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(architectId))
                    return BadRequest(new { success = false, message = "ClientId and ArchitectId are required." });

                // ✅ Fetch all messages between architect and client
                var messages = await context.Messages
                    .Where(m =>
                        (m.ClientId == clientId && m.ArchitectId == architectId) ||
                        (m.ClientId == architectId && m.ArchitectId == clientId))
                    .OrderBy(m => m.MessageDate)
                    .Select(m => new
                    {
                        m.MessageId,
                        m.ClientId,
                        m.ArchitectId,
                        m.SenderId,
                        m.MessageBody,
                        m.MessageDate,
                        m.IsRead,
                        m.AttachmentUrl
                    })
                    .ToListAsync();

                // ✅ Mark unread messages as read for this architect
                var unreadMessages = await context.Messages
                    .Where(m =>
                        m.ArchitectId == architectId &&
                        m.ClientId == clientId &&
                        m.SenderId == clientId &&  // only mark those sent by the client
                        !m.IsRead)
                    .ToListAsync();

                if (unreadMessages.Any())
                {
                    unreadMessages.ForEach(m => m.IsRead = true);
                    await context.SaveChangesAsync();
                }

                return Ok(new { success = true, messages });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }
        [HttpPost("Architect/SendMessage")]
        [AllowAnonymous] // or [Authorize] if you use auth later
        public async Task<IActionResult> SendMessageFromArchitect([FromBody] SendMessageRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.ClientId) ||
                    string.IsNullOrWhiteSpace(request.ArchitectId) ||
                    string.IsNullOrWhiteSpace(request.MessageBody))
                {
                    return BadRequest(new { success = false, message = "ClientId, ArchitectId, and MessageBody are required." });
                }

                // ✅ Create message object where architect is the sender
                var message = new Message
                {
                    MessageId = Guid.NewGuid(),
                    ClientId = request.ClientId,
                    ArchitectId = request.ArchitectId,
                    SenderId = request.SenderId ?? request.ArchitectId, // default to architect
                    MessageBody = request.MessageBody,
                    MessageDate = DateTime.UtcNow,
                    IsRead = false // unread for the client
                };

                context.Messages.Add(message);
                await context.SaveChangesAsync();

                // ✅ Notify the client via SignalR
                await _hubContext.Clients.User(request.ClientId).SendAsync("ReceiveMessage", new
                {
                    SenderId = message.SenderId,
                    MessageBody = message.MessageBody,
                    MessageDate = message.MessageDate.ToString("g")
                });

                return Ok(new { success = true, message = "Message sent successfully." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }


    }
}

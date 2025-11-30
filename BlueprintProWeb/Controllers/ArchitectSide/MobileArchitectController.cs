using BlueprintProWeb.Data;
using BlueprintProWeb.Hubs;
using BlueprintProWeb.Models;
using BlueprintProWeb.ViewModels;
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
                .Where(p => p.user_architectId == architectId
                    && (p.project_Status == "Ongoing" || p.project_Status == "Finished"))
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

        // ✅ 1. Get all pending match requests for this architect
        [HttpGet("matchRequests/{architectId}")]
        public async Task<IActionResult> GetPendingMatches(string architectId)
        {
            var matches = await context.Matches
                .Include(m => m.Client)
                .Where(m => m.ArchitectId == architectId && m.MatchStatus == "Pending")
                .Select(m => new
                {
                    matchId = m.MatchId,
                    clientName = $"{m.Client.user_fname} {m.Client.user_lname}",
                    matchDate = m.MatchDate,
                    matchStatus = m.MatchStatus
                })
                .ToListAsync();

            if (matches == null || !matches.Any())
                return NotFound();

            return Ok(matches);
        }

        // ✅ 2. Respond (Accept/Decline) a match request
        [HttpPost("respondMatch")]
        public async Task<IActionResult> RespondMatch([FromQuery] string matchId, [FromQuery] bool approve)
        {
            if (string.IsNullOrEmpty(matchId))
                return BadRequest("MatchId is required.");

            var match = await context.Matches.FirstOrDefaultAsync(m => m.MatchId == matchId);
            if (match == null)
                return NotFound("Match not found.");

            match.MatchStatus = approve ? "Approved" : "Declined";
            await context.SaveChangesAsync();

            return Ok();
        }
        [HttpGet("Architect/Messages/All")]
        [AllowAnonymous]
        public async Task<IActionResult> GetAllMessagesForArchitect([FromQuery] string architectId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(architectId))
                    return BadRequest(new { success = false, message = "ArchitectId is required." });

                var baseUrl = $"{Request.Scheme}://{Request.Host}";

                var rawConversations = await context.Messages
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
                        LastMessageTimeUtc = g.Max(m => m.MessageDate),
                        ProfileUrl = context.Users
                            .Where(u => u.Id == g.Key)
                            .Select(u => u.user_profilePhoto)
                            .FirstOrDefault(),
                        UnreadCount = g.Count(x => x.ClientId == g.Key && !x.IsRead)
                    })
                    .ToListAsync();

                var phTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Singapore Standard Time");

                var conversations = rawConversations
                    .Select(c => new
                    {
                        c.ClientId,
                        c.ClientName,
                        c.LastMessage,
                        LastMessageTime = TimeZoneInfo.ConvertTimeFromUtc(c.LastMessageTimeUtc, phTimeZone),

                        // FIXED PROFILE URL
                        ProfileUrl = string.IsNullOrEmpty(c.ProfileUrl)
                            ? null
                            : $"{baseUrl}/images/profiles/{Path.GetFileName(c.ProfileUrl)}",

                        c.UnreadCount
                    })
                    .OrderByDescending(x => x.LastMessageTime)
                    .ToList();

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

                var baseUrl = $"{Request.Scheme}://{Request.Host}";

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

                        // ✔ FIXED PROFILE PHOTO URL
                        ClientPhoto = string.IsNullOrEmpty(m.Client.user_profilePhoto)
                            ? null
                            : $"{baseUrl}/images/profiles/{Path.GetFileName(m.Client.user_profilePhoto)}",

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

                // Step 1: Fetch raw UTC messages
                var messagesRaw = await context.Messages
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

                // Step 2: Convert to Philippine time
                var phTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Singapore Standard Time");
                var messages = messagesRaw.Select(m => new
                {
                    m.MessageId,
                    m.ClientId,
                    m.ArchitectId,
                    m.SenderId,
                    m.MessageBody,
                    MessageDate = TimeZoneInfo.ConvertTimeFromUtc(m.MessageDate, phTimeZone),
                    m.IsRead,
                    m.AttachmentUrl
                }).ToList();

                // Step 3: Mark unread client-sent messages as read
                var unreadMessages = await context.Messages
                    .Where(m =>
                        m.ArchitectId == architectId &&
                        m.ClientId == clientId &&
                        m.SenderId == clientId &&  // mark only those from client
                        !m.IsRead)
                    .ToListAsync();

                if (unreadMessages.Any())
                {
                    unreadMessages.ForEach(m => m.IsRead = true);
                    await context.SaveChangesAsync();
                }

                // Step 4: Return PH-time adjusted messages
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

        [HttpGet("getProjectTracker/{blueprintId}")]
        public async Task<IActionResult> GetProjectTracker(int blueprintId)
        {
            var tracker = await context.ProjectTrackers
                .Include(pt => pt.Project)
                    .ThenInclude(p => p.Architect)
                .Include(pt => pt.Compliance)
                .FirstOrDefaultAsync(pt => pt.Project.blueprint_Id == blueprintId);

            if (tracker == null)
                return NotFound();

            // ✅ Manually fetch the files using project_Id
            var projectFiles = await context.ProjectFiles
                .Where(f => f.project_Id == tracker.project_Id)
                .OrderByDescending(f => f.projectFile_Version)
                .ToListAsync();

            var response = new
            {
                projectTrack_Id = tracker.projectTrack_Id,
                project_Id = tracker.project_Id,
                currentFileName = tracker.projectTrack_currentFileName,
                currentFilePath = tracker.projectTrack_currentFilePath,
                currentRevision = tracker.projectTrack_currentRevision,
                status = tracker.projectTrack_Status,

                architectName = tracker.Project?.Architect == null
                    ? null
                    : $"{tracker.Project.Architect.user_fname} {tracker.Project.Architect.user_lname}",

                isRated = tracker.Project?.project_clientHasRated ?? false,

                // ✅ Revision history — manually loaded files
                revisionHistory = projectFiles.Select(f => new
                {
                    projectFile_Id = f.projectFile_Id,
                    project_Id = f.project_Id,
                    projectFile_fileName = f.projectFile_fileName,
                    projectFile_Path = f.projectFile_Path,
                    projectFile_Version = f.projectFile_Version,
                    projectFile_uploadedDate = f.projectFile_uploadedDate
                }).ToList(),

                compliance = tracker.Compliance == null ? null : new
                {
                    compliance_Id = tracker.Compliance.compliance_Id,
                    compliance_Zoning = tracker.Compliance.compliance_Zoning,
                    compliance_Others = tracker.Compliance.compliance_Others
                },

                finalizationNotes = tracker.projectTrack_FinalizationNotes,
                projectTrackerStatus = tracker.projectTrack_Status
            };

            return Ok(response);
        }

        [HttpPost("UploadProjectFile")]
        public async Task<IActionResult> UploadProjectFile(
            [FromForm] string projectId,
            [FromForm] IFormFile file
        )
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { success = false, message = "No file uploaded." });

            var project = await context.Projects.FindAsync(projectId);
            if (project == null)
                return NotFound(new { success = false, message = "Project not found." });

            var tracker = await context.ProjectTrackers.FirstOrDefaultAsync(t => t.project_Id == projectId);
            if (tracker == null)
                return NotFound(new { success = false, message = "Project tracker not found." });

            // Save uploaded file
            var uploadsFolder = Path.Combine(env.WebRootPath, "uploads");
            if (!Directory.Exists(uploadsFolder))
                Directory.CreateDirectory(uploadsFolder);

            var uniqueFileName = Guid.NewGuid().ToString() + Path.GetExtension(file.FileName);
            var filePath = Path.Combine(uploadsFolder, uniqueFileName);
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // Archive current file
            if (!string.IsNullOrEmpty(tracker.projectTrack_currentFilePath))
            {
                var oldFile = new ProjectFile
                {
                    project_Id = projectId,
                    projectFile_fileName = tracker.projectTrack_currentFileName,
                    projectFile_Path = tracker.projectTrack_currentFilePath,
                    projectFile_Version = tracker.projectTrack_currentRevision,
                    projectFile_uploadedDate = DateTime.UtcNow
                };
                context.ProjectFiles.Add(oldFile);
            }

            // Update tracker to new file
            tracker.projectTrack_currentFileName = file.FileName;
            tracker.projectTrack_currentFilePath = "/uploads/" + uniqueFileName;
            tracker.projectTrack_currentRevision += 1;

            await context.SaveChangesAsync();

            // Send client notification
            var notif = new Notification
            {
                user_Id = project.user_clientId,
                notification_Title = "New Revision Uploaded",
                notification_Message = $"A new revision has been uploaded for your project '{project.project_Title}'.",
                notification_Date = DateTime.Now,
                notification_isRead = false
            };
            context.Notifications.Add(notif);
            await context.SaveChangesAsync();

            return Ok(new { success = true, message = "New revision uploaded successfully." });
        }

        [HttpPost("UploadComplianceFile")]
        public async Task<IActionResult> UploadComplianceFile(
            [FromForm] int projectTrackId,
            [FromForm] string fileType,
            [FromForm] IFormFile file)
        {
            try
            {
                if (file == null || file.Length == 0)
                    return BadRequest(new { success = false, message = "File cannot be empty." });

                var tracker = await context.ProjectTrackers
                    .Include(pt => pt.Compliance)
                    .Include(pt => pt.Project)
                    .FirstOrDefaultAsync(pt => pt.projectTrack_Id == projectTrackId);

                if (tracker == null)
                    return NotFound(new { success = false, message = "ProjectTracker not found." });

                if (tracker.Compliance == null)
                {
                    tracker.Compliance = new Compliance
                    {
                        projectTrack_Id = tracker.projectTrack_Id,
                        compliance_Zoning = "",
                        compliance_Others = ""
                    };
                    context.Compliances.Add(tracker.Compliance);
                }

                var uploadsFolder = Path.Combine(env.WebRootPath, "uploads", "compliance");
                if (!Directory.Exists(uploadsFolder))
                    Directory.CreateDirectory(uploadsFolder);

                var fileName = $"{fileType}_{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
                var filePath = Path.Combine(uploadsFolder, fileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                switch (fileType.ToLower())
                {
                    case "zoning": tracker.Compliance.compliance_Zoning = fileName; break;
                    case "others": tracker.Compliance.compliance_Others = fileName; break;
                    default: return BadRequest(new { success = false, message = "Invalid file type." });
                }

                await context.SaveChangesAsync();

                if (tracker.Project != null)
                {
                    var notif = new Notification
                    {
                        user_Id = tracker.Project.user_clientId,
                        notification_Title = "Compliance File Uploaded",
                        notification_Message = $"A new {fileType} compliance file has been uploaded for your project '{tracker.Project.project_Title}'.",
                        notification_Date = DateTime.Now,
                        notification_isRead = false
                    };

                    context.Notifications.Add(notif);
                    await context.SaveChangesAsync();
                }

                return Ok(new
                {
                    success = true,
                    message = $"{fileType} file uploaded successfully.",
                    fileName
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = $"Server error: {ex.Message}" });
            }
        }

        [HttpPost("SaveFinalizationNotes")]
        public async Task<IActionResult> SaveFinalizationNotes(
            [FromForm] int projectTrackId,
            [FromForm] string notes)
        {
            var tracker = await context.ProjectTrackers.FirstOrDefaultAsync(pt => pt.projectTrack_Id == projectTrackId);
            if (tracker == null)
                return Ok(new { success = false, message = "ProjectTracker not found." });

            tracker.projectTrack_FinalizationNotes = notes;
            await context.SaveChangesAsync();

            return Ok(new { success = true, message = "Finalization notes saved successfully." });
        }

        [HttpPost("FinalizeProject")]
        public async Task<IActionResult> FinalizeProject([FromForm] string projectId)
        {
            var project = await context.Projects.FirstOrDefaultAsync(p => p.project_Id == projectId);
            if (project == null)
                return Ok(new { success = false, message = "Project not found." });

            project.project_Status = "Finished";
            project.project_endDate = DateTime.Now;
            await context.SaveChangesAsync();

            if (!string.IsNullOrEmpty(project.user_clientId))
            {
                var notif = new Notification
                {
                    user_Id = project.user_clientId,
                    notification_Title = "Project Completed",
                    notification_Message = $"Your project '{project.project_Title}' has been marked as finished by architect {project.Architect?.user_fname} {project.Architect?.user_lname}.",
                    notification_Date = DateTime.Now,
                    notification_isRead = false
                };
                context.Notifications.Add(notif);
                await context.SaveChangesAsync();
            }

            var redirectUrl = Url.Action("ProjectTracker", "ArchitectInterface", new { id = project.blueprint_Id });

            return Ok(new
            {
                success = true,
                message = "✅ Project finalized successfully!",
                redirectUrl
            });
        }

        [HttpPost("updateProjectStatus")]
        public async Task<IActionResult> UpdateProjectStatus([FromForm] string projectId, [FromForm] string status)
        {
            var tracker = await context.ProjectTrackers
                .Include(t => t.Project)
                .FirstOrDefaultAsync(t => t.project_Id == projectId);

            if (tracker == null)
                return Ok(new { success = false, message = "Tracker not found." });

            tracker.projectTrack_Status = status;
            await context.SaveChangesAsync();

            if (tracker.Project != null)
            {
                var notif = new Notification
                {
                    user_Id = tracker.Project.user_clientId,
                    notification_Title = "Project Phase Updated",
                    notification_Message = $"Your project '{tracker.Project.project_Title}' is now in the {status} phase.",
                    notification_Date = DateTime.Now,
                    notification_isRead = false
                };

                context.Notifications.Add(notif);
                await context.SaveChangesAsync();
            }

            return Ok(new { success = true });
        }

        // 📱 Edit Blueprint (Mobile)
        [HttpPost("EditBlueprint")]
        public async Task<IActionResult> EditBlueprint([FromForm] MobileEditBlueprintViewModel vm)
        {
            var blueprint = context.Blueprints.Find(vm.blueprintId);
            if (blueprint == null)
                return NotFound();

            if (!string.IsNullOrWhiteSpace(vm.blueprintName))
                blueprint.blueprintName = vm.blueprintName;

            if (vm.blueprintPrice.HasValue)
                blueprint.blueprintPrice = vm.blueprintPrice.Value;

            if (!string.IsNullOrWhiteSpace(vm.blueprintStyle))
                blueprint.blueprintStyle = vm.blueprintStyle;

            if (!string.IsNullOrWhiteSpace(vm.blueprintDescription))
            {
                blueprint.blueprintDescription = vm.blueprintDescription;
            }

            if (vm.BlueprintImage != null)
            {
                string oldFileName = blueprint.blueprintImage;
                string newFile = UploadMobileFile(vm.BlueprintImage, oldFileName);
                blueprint.blueprintImage = newFile;
            }

            context.SaveChanges();
            return Ok(new { success = true, message = "Blueprint updated successfully." });
        }

        [HttpDelete("DeleteBlueprint/{blueprintId}")]
        public async Task<IActionResult> DeleteBlueprint(int blueprintId)
        {
            var blueprint = await context.Blueprints.FindAsync(blueprintId);
            if (blueprint == null)
                return NotFound(new { success = false, message = "Blueprint not found." });

            if (!string.IsNullOrEmpty(blueprint.blueprintImage))
            {
                var path = Path.Combine(env.WebRootPath, "images", blueprint.blueprintImage);
                if (System.IO.File.Exists(path))
                    System.IO.File.Delete(path);
            }

            context.Blueprints.Remove(blueprint);
            await context.SaveChangesAsync();
            return Ok(new { success = true, message = "Blueprint deleted successfully." });
        }

        [HttpGet("DeletedProjects")]
        public async Task<IActionResult> MobileDeletedProjects([FromQuery] string architectId)
        {
            if (string.IsNullOrEmpty(architectId))
                return BadRequest("ArchitectId is required.");

            var deleted = await context.Projects
                .Where(p => p.user_architectId == architectId && p.project_Status == "Deleted")
                .Include(p => p.Client)
                .ToListAsync();

            var result = deleted.Select(p => new
            {
                p.project_Id,
                p.project_Title,
                clientName = p.Client != null ? $"{p.Client.user_fname} {p.Client.user_lname}" : "N/A",
                deletedDate = p.project_endDate?.ToString() ?? DateTime.UtcNow.ToString()
            });

            return Ok(result);
        }

        [HttpPost("DeleteProject")]
        public async Task<IActionResult> MobileDeleteProject([FromForm] string id)
        {
            var project = await context.Projects
                .FirstOrDefaultAsync(p => p.project_Id == id);

            if (project == null)
                return NotFound();

            project.project_Status = "Deleted";
            await context.SaveChangesAsync();

            var notif = new Notification
            {
                user_Id = project.user_clientId,
                notification_Title = "Project Deleted",
                notification_Message = $"Your project '{project.project_Title}' has been removed by the architect.",
                notification_Date = DateTime.Now,
                notification_isRead = false
            };

            context.Notifications.Add(notif);
            await context.SaveChangesAsync();

            return Ok(new { success = true, message = "Project deleted successfully." });
        }

        [HttpPost("RestoreProject")]
        public async Task<IActionResult> MobileRestoreProject([FromForm] string id)
        {
            var project = await context.Projects
                .FirstOrDefaultAsync(p => p.project_Id == id);

            if (project == null)
                return NotFound();

            project.project_Status = "Ongoing";
            await context.SaveChangesAsync();

            var notif = new Notification
            {
                user_Id = project.user_clientId,
                notification_Title = "Project Restored",
                notification_Message = $"Your project '{project.project_Title}' has been restored.",
                notification_Date = DateTime.Now,
                notification_isRead = false
            };

            context.Notifications.Add(notif);
            await context.SaveChangesAsync();

            return Ok(new { success = true, message = "Project restored successfully." });
        }

        [HttpPost("PermanentlyDeleteProject")]
        public async Task<IActionResult> MobilePermanentlyDeleteProject([FromForm] string id)
        {
            var project = await context.Projects
                .Include(p => p.Blueprint)
                .FirstOrDefaultAsync(p => p.project_Id == id);

            if (project == null)
                return NotFound();

            // Load tracker & children
            var tracker = await context.ProjectTrackers
                .Include(t => t.Compliance)
                .Include(t => t.ProjectFiles)
                .FirstOrDefaultAsync(t => t.project_Id == id);

            if (tracker != null)
            {
                if (tracker.Compliance != null)
                    context.Compliances.Remove(tracker.Compliance);

                if (tracker.ProjectFiles.Any())
                    context.ProjectFiles.RemoveRange(tracker.ProjectFiles);

                context.ProjectTrackers.Remove(tracker);
            }

            // delete blueprint?
            var blueprint = await context.Blueprints
                .FirstOrDefaultAsync(b => b.blueprintId == project.blueprint_Id);

            if (blueprint != null)
                context.Blueprints.Remove(blueprint);

            context.Projects.Remove(project);
            await context.SaveChangesAsync();

            return Ok(new { success = true, message = "Project permanently deleted." });
        }
    }
}

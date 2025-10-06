using BlueprintProWeb.Data;
using BlueprintProWeb.Hubs;
using BlueprintProWeb.Models;
using BlueprintProWeb.ViewModels;
using iText.Commons.Actions.Contexts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using System.Net;
using static iText.StyledXmlParser.Jsoup.Select.Evaluator;

namespace BlueprintProWeb.Controllers.ArchitectSide
{
    public class ArchitectInterface : Controller
    {
        private readonly AppDbContext context;
        private readonly UserManager<User> _userManager;
        public IWebHostEnvironment WebHostEnvironment;
        private readonly IHubContext<ChatHub> _hubContext;
        public ArchitectInterface(AppDbContext context, IWebHostEnvironment webHostEnvironment, UserManager<User> userManager, IHubContext<ChatHub> hubContext)
        {
            this.context = context;
            WebHostEnvironment = webHostEnvironment;
            _userManager = userManager;
            _hubContext = hubContext;
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
                project_Status = "Ongoing",
                project_Budget = vm.blueprintPrice.ToString()
            };
            context.Projects.Add(project);
            await context.SaveChangesAsync();

            var projectTracker = new ProjectTracker
            {
                project_Id = project.project_Id,
                project_Title = project.project_Title,
                blueprint_Description = vm.blueprintDescription,
                projectTrack_dueDate = vm.projectTrack_dueDate,
                projectTrack_currentFileName = vm.BlueprintImage?.FileName,
                projectTrack_currentFilePath = "/images/" + stringFileName,
                projectTrack_currentRevision = 1
            };

            context.ProjectTrackers.Add(projectTracker);
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

        [HttpGet]
        public async Task<IActionResult> GetClientProfile(string clientId)
        {
            if (string.IsNullOrEmpty(clientId))
                return BadRequest(new { success = false, message = "ClientId required" });

            var client = await context.Users.FindAsync(clientId);

            if (client == null)
                return NotFound(new { success = false, message = "Client not found" });

            var fullName = $"{client.user_fname} {client.user_lname}";

            return Json(new
            {
                success = true,
                name = fullName,
                email = client.Email,
                phone = client.PhoneNumber
            });
        }

        // GET: ArchitectInterface/Messages/{clientId}

        // GET: ArchitectInterface/Messages
        [HttpGet]
        public async Task<IActionResult> Messages(string clientId)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null) return Unauthorized();

            // 1. Load matches for this architect
            var matches = await context.Matches
                .Where(m => m.ArchitectId == currentUser.Id)
                .Select(m => new MatchViewModel
                {
                    MatchId = m.MatchId.ToString(),
                    ClientId = m.ClientId,
                    ArchitectId = m.ArchitectId,
                    ClientName = m.Client.user_fname + " " + m.Client.user_lname,
                    ClientEmail = m.Client.Email,
                    ClientPhone = m.Client.PhoneNumber,
                    ArchitectName = currentUser.user_fname + " " + currentUser.user_lname,
                    MatchStatus = m.MatchStatus,
                    MatchDate = m.MatchDate
                    //ArchitectStyle = m.ArchitectStyle,
                    //ArchitectLocation = m.ArchitectLocation,
                    //ArchitectBudget = m.ArchitectBudget
                })
                .ToListAsync();

            // 2. Load conversations (one per client)
            var conversations = await context.Messages
                .Where(m => m.ArchitectId == currentUser.Id || m.ClientId == currentUser.Id)
                .GroupBy(m => m.ClientId)
                .Select(g => new ChatViewModel
                {
                    ClientId = g.Key,
                    ClientName = g.First().Client.user_fname + " " + g.First().Client.user_lname,
                    ClientProfileUrl = null, // placeholder until you add profile pic
                    LastMessageTime = g.Max(x => x.MessageDate),
                    Messages = new List<MessageViewModel>() // only load in ActiveChat
                })
                .ToListAsync();

            // 3. Load ActiveChat (if clientId is provided)
            ChatViewModel? activeChat = null;
            if (!string.IsNullOrEmpty(clientId))
            {
                var messages = await context.Messages
                    .Where(m =>
                        (m.ClientId == clientId && m.ArchitectId == currentUser.Id) ||
                        (m.ClientId == currentUser.Id && m.ArchitectId == clientId))
                    .OrderBy(m => m.MessageDate)
                    .Select(m => new MessageViewModel
                    {
                        MessageId = m.MessageId.ToString(),
                        ClientId = m.ClientId,
                        ArchitectId = m.ArchitectId,
                        SenderId = m.SenderId,
                        MessageBody = m.MessageBody,
                        MessageDate = m.MessageDate,
                        IsRead = m.IsRead,
                        IsDeleted = m.IsDeleted,
                        AttachmentUrl = m.AttachmentUrl,
                        SenderName = m.Sender.user_fname + " " + m.Sender.user_lname,
                        SenderProfilePhoto = null, // placeholder
                        IsOwnMessage = (m.SenderId == currentUser.Id)
                    })
                    .ToListAsync();

                activeChat = new ChatViewModel
                {
                    ClientId = clientId,
                    ClientName = messages.FirstOrDefault()?.SenderName ?? "Unknown",
                    ClientProfileUrl = null, // placeholder
                    LastMessageTime = messages.LastOrDefault()?.MessageDate ?? DateTime.UtcNow,
                    Messages = messages
                };
            }

            // 4. Build page model
            var vm = new ChatPageViewModel
            {
                Matches = matches,
                Conversations = conversations.OrderByDescending(c => c.LastMessageTime).ToList(),
                ActiveChat = activeChat
            };

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendMessage(string clientId, string messageBody)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null) return Unauthorized();

            if (string.IsNullOrWhiteSpace(messageBody))
                return RedirectToAction("Messages", new { clientId });

            var message = new Message
            {
                MessageId = Guid.NewGuid(),
                ClientId = clientId,
                ArchitectId = currentUser.Id,
                SenderId = currentUser.Id,
                MessageBody = messageBody,
                MessageDate = DateTime.UtcNow,
                IsRead = false
            };

            context.Messages.Add(message);
            await context.SaveChangesAsync();

            // Notify all connected clients in real-time
            await _hubContext.Clients.User(clientId).SendAsync("ReceiveMessage", new
            {
                SenderId = currentUser.Id,
                SenderName = currentUser.user_fname + " " + currentUser.user_lname,
                MessageBody = messageBody,
                MessageDate = DateTime.UtcNow.ToString("g")
            });

            return RedirectToAction("Messages", new { clientId });
        }

        [HttpPost]
        public async Task<IActionResult> UpdateProjectStatus(string projectId, string status)
        {
            var tracker = await context.ProjectTrackers
                .FirstOrDefaultAsync(t => t.project_Id == projectId);

            if (tracker == null) return NotFound();

            tracker.projectTrack_Status = status;
            await context.SaveChangesAsync();

            return Json(new { success = true });
        }

        [HttpGet]
        public IActionResult ProjectTracker(int id) 
        {
            var project = context.Projects.FirstOrDefault(p => p.blueprint_Id == id);
            if (project == null) return NotFound();

            var tracker = context.ProjectTrackers
                .Include(t => t.Compliance)  
                .FirstOrDefault(t => t.project_Id == project.project_Id);
            if (tracker == null) return NotFound();

            var history = context.ProjectFiles
                .Where(f => f.project_Id == project.project_Id)
                .OrderByDescending(f => f.projectFile_Version)
                .ToList();

            var vm = new ProjectTrackerViewModel
            {
                projectTrack_Id = tracker.projectTrack_Id,
                project_Id = project.project_Id,
                CurrentFileName = tracker.projectTrack_currentFileName,
                CurrentFilePath = tracker.projectTrack_currentFilePath,
                CurrentRevision = tracker.projectTrack_currentRevision,
                Status = tracker.projectTrack_Status,
                RevisionHistory = history,
                Compliance = tracker.Compliance,
                FinalizationNotes = tracker.projectTrack_FinalizationNotes,
                ProjectStatus = project.project_Status
            };

            return View(vm);
        }

        [HttpPost]
        public async Task<IActionResult> UploadProjectFile(string projectId, IFormFile file)
        {
            if (file == null || file.Length == 0)
                return RedirectToAction("ProjectTracker", new { id = projectId });

            var project = await context.Projects.FindAsync(projectId);
            if (project == null) return NotFound();

            var tracker = context.ProjectTrackers.FirstOrDefault(t => t.project_Id == projectId);
            if (tracker == null) return NotFound();

            // Save file
            var uploadsFolder = Path.Combine(WebHostEnvironment.WebRootPath, "uploads");
            if (!Directory.Exists(uploadsFolder))
                Directory.CreateDirectory(uploadsFolder);

            var uniqueFileName = Guid.NewGuid().ToString() + Path.GetExtension(file.FileName);
            var filePath = Path.Combine(uploadsFolder, uniqueFileName);
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // Before overwriting current file → archive it into ProjectFiles
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

            // Update tracker with the new "current"
            tracker.projectTrack_currentFileName = file.FileName;
            tracker.projectTrack_currentFilePath = "/uploads/" + uniqueFileName;
            tracker.projectTrack_currentRevision += 1;

            await context.SaveChangesAsync();

            return RedirectToAction("ProjectTracker", new { id = project.blueprint_Id });
        }

        [HttpPost]
        public async Task<IActionResult> UploadComplianceFile(int projectTrackId, string fileType, IFormFile file)
        {
            try
            {
                if (file == null || file.Length == 0)
                {
                    return Json(new { success = false, message = "File cannot be empty." });
                }

                var tracker = await context.ProjectTrackers
                    .Include(pt => pt.Compliance)
                    .FirstOrDefaultAsync(pt => pt.projectTrack_Id == projectTrackId);

                if (tracker == null)
                    return Json(new { success = false, message = "ProjectTracker not found." });

                if (tracker.Compliance == null)
                {
                    tracker.Compliance = new Compliance
                    {
                        projectTrack_Id = tracker.projectTrack_Id,
                        compliance_Structural = "",
                        compliance_Electrical = "",
                        compliance_Sanitary = "",
                        compliance_Zoning = "",
                        compliance_Others = ""
                    };
                    context.Compliances.Add(tracker.Compliance);
                }

                var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "compliance");
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
                    case "structural": tracker.Compliance.compliance_Structural = fileName; break;
                    case "electrical": tracker.Compliance.compliance_Electrical = fileName; break;
                    case "sanitary": tracker.Compliance.compliance_Sanitary = fileName; break;
                    case "zoning": tracker.Compliance.compliance_Zoning = fileName; break;
                    case "others": tracker.Compliance.compliance_Others = fileName; break;
                    default: return Json(new { success = false, message = "Invalid file type." });
                }

                await context.SaveChangesAsync();

                return Json(new
                {
                    success = true,
                    message = $"{fileType} file uploaded successfully.",
                    fileName
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Server error: {ex.Message}" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> SaveFinalizationNotes(int projectTrackId, string notes)
        {
            var tracker = await context.ProjectTrackers.FirstOrDefaultAsync(pt => pt.projectTrack_Id == projectTrackId);
            if (tracker == null)
                return Json(new { success = false, message = "ProjectTracker not found." });

            tracker.projectTrack_FinalizationNotes = notes;
            await context.SaveChangesAsync();

            return Json(new { success = true, message = "Finalization notes saved successfully." });
        }

        [HttpPost]
        [Route("ArchitectInterface/FinalizeProject")]
        public async Task<IActionResult> FinalizeProject(string projectId)
        {
            var project = await context.Projects
                .FirstOrDefaultAsync(p => p.project_Id == projectId);

            if (project == null)
                return Json(new { success = false, message = "Project not found." });

            project.project_Status = "Finished";
            project.project_endDate = DateTime.Now;

            await context.SaveChangesAsync();

            // ✅ Return redirect to ProjectTracker view for this project
            var redirectUrl = Url.Action("ProjectTracker", "ArchitectInterface", new { id = project.blueprint_Id });

            return Json(new
            {
                success = true,
                message = "✅ Project finalized successfully!",
                redirectUrl
            });
        }
    }
}
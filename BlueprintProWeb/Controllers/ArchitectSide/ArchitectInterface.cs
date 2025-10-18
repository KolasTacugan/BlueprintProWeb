using BlueprintProWeb.Data;
using BlueprintProWeb.Hubs;
using BlueprintProWeb.Models;
using BlueprintProWeb.Services;
using BlueprintProWeb.Settings;
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
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using Stripe;
using Stripe.Checkout;
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
        private readonly StripeSettings _stripeSettings;
        private readonly ImageService _imageService;

        public ArchitectInterface(ImageService imageService, AppDbContext context, IWebHostEnvironment webHostEnvironment, UserManager<User> userManager, IHubContext<ChatHub> hubContext, IOptions<StripeSettings> stripeSettings)
        {
            this.context = context;
            WebHostEnvironment = webHostEnvironment;
            _userManager = userManager;
            _hubContext = hubContext;
            _stripeSettings = stripeSettings.Value;
            _imageService = imageService;

        }

        public async Task<IActionResult> ArchitectDashboard()
        {
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null)
                return RedirectToAction("Login", "Account");

            ViewData["UserFirstName"] = currentUser.user_fname ?? "User";

            // Get statistics for the current architect
            var userId = currentUser.Id;

            // Total Matches - count all matches where this user is the architect
            var totalMatches = await context.Matches
                .CountAsync(m => m.ArchitectId == userId);

            // Total Uploads - count blueprints uploaded by this architect
            var totalUploads = await context.Blueprints
                .CountAsync(bp => bp.architectId == userId);

            // Total Projects - count projects where this user is the architect
            var totalProjects = await context.Projects
                .CountAsync(p => p.user_architectId == userId);

            // Recent matches (last 5)
            var recentMatches = await context.Matches
                .Where(m => m.ArchitectId == userId)
                .Include(m => m.Client)
                .OrderByDescending(m => m.MatchDate)
                .Take(5)
                .Select(m => new ClientMatchSummary
                {
                    ClientName = $"{m.Client.user_fname} {m.Client.user_lname}",
                    ClientNeeds = m.Client.user_Style ?? "General Architecture",
                    Status = m.MatchStatus,
                    MatchDate = m.MatchDate
                })
                .ToListAsync();

            // Recent uploads (last 5)
            var recentUploads = await context.Blueprints
                .Where(bp => bp.architectId == userId)
                .OrderByDescending(bp => bp.blueprintId) // Using blueprintId as proxy for upload order
                .Take(5)
                .Select(bp => new BlueprintUpload
                {
                    BlueprintName = bp.blueprintName,
                    UploadDate = DateTime.UtcNow, // You might want to add an UploadDate field to Blueprint model
                    Price = bp.blueprintPrice,
                    IsForSale = bp.blueprintIsForSale
                })
                .ToListAsync();

            // Current/most recent project - get the raw data first
            var currentProjectRaw = await context.Projects
                .Where(p => p.user_architectId == userId)
                .Include(p => p.Client)
                .OrderByDescending(p => p.project_startDate)
                .FirstOrDefaultAsync();

            // Calculate project overview after data is retrieved
            ProjectOverview? currentProject = null;
            if (currentProjectRaw != null)
            {
                currentProject = new ProjectOverview
                {
                    ProjectTitle = currentProjectRaw.project_Title,
                    Status = currentProjectRaw.project_Status,
                    ProgressPercentage = CalculateProjectProgress(currentProjectRaw.project_Status),
                    StartDate = currentProjectRaw.project_startDate,
                    ArchitectName = $"{currentProjectRaw.Client.user_fname} {currentProjectRaw.Client.user_lname}" // Client name for architect view
                };
            }

            var dashboardViewModel = new ArchitectDashboardViewModel
            {
                TotalMatches = totalMatches,
                TotalUploads = totalUploads,
                TotalProjects = totalProjects,
                RecentMatches = recentMatches,
                RecentUploads = recentUploads,
                CurrentProject = currentProject
            };

            return View(dashboardViewModel);
        }

        private int CalculateProjectProgress(string status)
        {
            return status?.ToLower() switch
            {
                "pending" => 0,
                "planning" => 25,
                "designing" => 50,
                "development" => 75,
                "ongoing" => 60,
                "completed" => 100,
                "testing" => 90,
                "finished" => 100,
                _ => 0
            };
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

            var projects = await context.Projects
                .Where(p => p.user_architectId == user.Id)
                .Include(p => p.Blueprint)
                .Include(p => p.Client)
                .ToListAsync();

            return View(projects);
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
                blueprintIsForSale = true,
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
            string stringFileName = UploadProjectFile(vm);
            var user = await _userManager.GetUserAsync(User);
            var userId = user.Id;

            var blueprint = new Blueprint
            {
                blueprintImage = stringFileName,
                blueprintName = vm.blueprintName,
                blueprintPrice = 0,
                blueprintDescription = vm.blueprintDescription,
                blueprintStyle = vm.blueprintStyle,
                blueprintIsForSale = false,
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

        private string UploadProjectFile(BlueprintViewModel vm)
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


        private string UploadFile(BlueprintViewModel vm, string? oldFileName = null)
        {
            string fileName = null;
            if (vm.BlueprintImage != null)
            {
                string uploadDir = Path.Combine(WebHostEnvironment.WebRootPath, "images");
                Directory.CreateDirectory(uploadDir);

                // 🆕 Generate new filename
                fileName = Guid.NewGuid().ToString() + "-" + vm.BlueprintImage.FileName;
                string filePath = Path.Combine(uploadDir, fileName);

                // 🧽 Delete old file if editing
                if (!string.IsNullOrEmpty(oldFileName))
                {
                    var oldPath = Path.Combine(uploadDir, oldFileName);
                    if (System.IO.File.Exists(oldPath))
                    {
                        try { System.IO.File.Delete(oldPath); } catch { /* ignore locks */ }
                    }
                }

                // 🧠 Load image in memory and process watermark BEFORE writing to disk
                using var memoryStream = new MemoryStream();
                vm.BlueprintImage.CopyTo(memoryStream);
                memoryStream.Position = 0;

                using (var image = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(memoryStream))
                {
                    // Optional: load watermark image
                    string watermarkPath = Path.Combine(WebHostEnvironment.WebRootPath, "images", "BPP-watermark.png");
                    if (System.IO.File.Exists(watermarkPath))
                    {
                        using var watermarkImg = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(watermarkPath);
                        watermarkImg.Mutate(w => w.Resize(image.Width, image.Height));
                        float opacity = 0.28f;

                        image.Mutate(ctx => ctx.DrawImage(
                            watermarkImg,
                            new Point(0, 0),
                            opacity
                        ));
                    }

                    // Save final watermarked image
                    image.Save(filePath);
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
                string oldFileName = blueprint.blueprintImage;
                string newFile = UploadFile(vm, oldFileName);
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

            // Build full profile photo URL
            string profilePhoto = string.IsNullOrEmpty(client.user_profilePhoto)
                ? "/images/profile.jpg"
                : Url.Content(client.user_profilePhoto);

            return Json(new
            {
                success = true,
                name = $"{client.user_fname} {client.user_lname}",
                email = client.Email,
                phone = client.PhoneNumber,
                profilePhoto = profilePhoto
            });
        }


        // GET: ArchitectInterface/Messages
        [HttpGet]
        public async Task<IActionResult> Messages(string clientId)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null)
                return Unauthorized();

            // ✅ 1. Load all matches for this architect
            var matches = await context.Matches
                .Where(m => m.ArchitectId == currentUser.Id)
                .Include(m => m.Client)
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
                    MatchDate = m.MatchDate,
                    ClientProfileUrl = string.IsNullOrEmpty(m.Client.user_profilePhoto)
                        ? "/images/default-profile.png"
                        : m.Client.user_profilePhoto
                })
                .ToListAsync();

            // ✅ 2. Load conversations
            var conversations = await context.Messages
                .Where(m => m.ArchitectId == currentUser.Id || m.ClientId == currentUser.Id)
                .Include(m => m.Client)
                .Include(m => m.Sender)
                .GroupBy(m => m.ClientId)
                .Select(g => new ChatViewModel
                {
                    ClientId = g.Key,
                    ClientName = g.First().Client.user_fname + " " + g.First().Client.user_lname,
                    LastMessageTime = g.Max(x => x.MessageDate),
                    Messages = new List<MessageViewModel>(),
                    UnreadCount = g.Count(x => x.SenderId != currentUser.Id && !x.IsRead),
                    ClientProfileUrl = string.IsNullOrEmpty(g.First().Client.user_profilePhoto)
                        ? "/images/default-profile.png"
                        : g.First().Client.user_profilePhoto
                })
                .ToListAsync();

            // ✅ 3. Load Active Chat
            ChatViewModel? activeChat = null;
            if (!string.IsNullOrEmpty(clientId))
            {
                var messages = await context.Messages
                    .Where(m =>
                        (m.ClientId == clientId && m.ArchitectId == currentUser.Id) ||
                        (m.ClientId == currentUser.Id && m.ArchitectId == clientId))
                    .Include(m => m.Sender)
                    .OrderBy(m => m.MessageDate)
                    .ToListAsync();

                // Mark unread messages as read
                var unreadMessages = messages
                    .Where(m => m.SenderId != currentUser.Id && !m.IsRead)
                    .ToList();

                foreach (var msg in unreadMessages)
                    msg.IsRead = true;

                if (unreadMessages.Any())
                    await context.SaveChangesAsync();

                // ✅ Build message view models (with profile photos)
                var vmMessages = messages.Select(m => new MessageViewModel
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
                    SenderName = m.Sender != null
                        ? m.Sender.user_fname + " " + m.Sender.user_lname
                        : "Unknown",
                    SenderProfilePhoto = m.Sender != null && !string.IsNullOrEmpty(m.Sender.user_profilePhoto)
                        ? m.Sender.user_profilePhoto
                        : "/images/default-profile.png",
                    IsOwnMessage = (m.SenderId == currentUser.Id)
                }).ToList();

                // ✅ Use match photo as chat header
                var matchPhoto = matches.FirstOrDefault(m => m.ClientId == clientId)?.ClientProfileUrl
                                 ?? "/images/default-profile.png";

                activeChat = new ChatViewModel
                {
                    ClientId = clientId,
                    ClientName = matches.FirstOrDefault(m => m.ClientId == clientId)?.ClientName ?? "Unknown",
                    LastMessageTime = vmMessages.LastOrDefault()?.MessageDate ?? DateTime.UtcNow,
                    Messages = vmMessages,
                    ClientProfileUrl = matchPhoto
                };
            }

            // ✅ 4. Return the combined view model
            var vm = new ChatPageViewModel
            {
                Matches = matches,
                Conversations = conversations.OrderByDescending(c => c.LastMessageTime).ToList(),
                ActiveChat = activeChat
            };

            return View(vm);
        }

        // ✅ POST: Send a message
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendMessage(string clientId, string messageBody)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null)
                return Unauthorized();

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

            // ✅ Optional real-time update via SignalR
            await _hubContext.Clients.User(clientId).SendAsync("ReceiveMessage", new
            {
                SenderId = currentUser.Id,
                SenderName = currentUser.user_fname + " " + currentUser.user_lname,
                MessageBody = messageBody,
                MessageDate = DateTime.UtcNow.ToString("g"),
                SenderProfilePhoto = string.IsNullOrEmpty(currentUser.user_profilePhoto)
                    ? "/images/default-profile.png"
                    : currentUser.user_profilePhoto
            });

            return RedirectToAction("Messages", new { clientId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult CreateCheckoutSession()
        {
            try
            {
                Stripe.StripeConfiguration.ApiKey = _stripeSettings.SecretKey;

                var options = new SessionCreateOptions
                {
                    PaymentMethodTypes = new List<string> { "card" },
                    LineItems = new List<SessionLineItemOptions>
                {
                    new SessionLineItemOptions
                    {
                        PriceData = new SessionLineItemPriceDataOptions
                        {
                            UnitAmount = 18000, // ₱180 * 100
                            Currency = "php",
                            ProductData = new SessionLineItemPriceDataProductDataOptions
                            {
                                Name = "Pro Subscription Plan (Monthly)"
                            }
                        },
                        Quantity = 1
                    }
                },
                    Mode = "payment",
                    SuccessUrl = $"{Request.Scheme}://{Request.Host}/ArchitectInterface/ActivateProPlan?session_id={{CHECKOUT_SESSION_ID}}&redirect=edit",
                    CancelUrl = $"{Request.Scheme}://{Request.Host}/ArchitectInterface/Cancel"
                };

                var service = new SessionService();
                var session = service.Create(options);

                return Json(new { url = session.Url });
            }
            catch (StripeException ex)
            {
                return Json(new { success = false, message = $"Stripe error: {ex.Message}" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> ActivateProPlan(string session_id, string redirect)
        {
            if (string.IsNullOrEmpty(session_id))
                return RedirectToAction("Profile", "ArchitectInterface");

            try
            {
                StripeConfiguration.ApiKey = _stripeSettings.SecretKey;

                var sessionService = new SessionService();
                var session = sessionService.Get(session_id);

                if (session == null || session.PaymentStatus != "paid")
                {
                    TempData["ErrorMessage"] = "Payment verification failed.";
                    return RedirectToAction("Profile", "ArchitectInterface");
                }
            }
            catch
            {
                TempData["ErrorMessage"] = "Unable to verify payment.";
                return RedirectToAction("Profile", "ArchitectInterface");
            }

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
                return RedirectToAction("Login", "Account");

            user.IsPro = true;
            user.SubscriptionPlan = "Pro";
            user.SubscriptionStartDate = DateTime.UtcNow;
            user.SubscriptionEndDate = DateTime.UtcNow.AddMonths(1);
            await _userManager.UpdateAsync(user);

            TempData["SuccessMessage"] = "Your Pro Plan is now active!";

            // ✅ If we came from checkout, go to Edit page instead of Profile
            if (redirect == "edit")
                return RedirectToAction("Profile", "Account");

            // Default fallback
            return RedirectToAction("Profile", "Account");
        }



        [Authorize]
        public IActionResult Cancel()
        {
            TempData["ErrorMessage"] = "Payment was canceled. You have not been charged.";
            return RedirectToAction("ArchitectDashboard");
        }


        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DowngradeToFreePlan()
        {
            try
            {
                var user = await _userManager.GetUserAsync(User);

                if (user == null)
                {
                    Response.StatusCode = 401; // Explicit unauthorized response
                    return Json(new { success = false, message = "User not found or session expired." });
                }

                user.IsPro = false;
                await _userManager.UpdateAsync(user);

                return Json(new { success = true, message = "Successfully downgraded to Free Plan." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error downgrading plan: {ex}");
                Response.StatusCode = 500;
                return Json(new { success = false, message = "An unexpected error occurred while downgrading." });
            }
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
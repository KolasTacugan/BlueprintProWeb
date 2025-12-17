using BlueprintProWeb.Data;
using BlueprintProWeb.Hubs;
using BlueprintProWeb.Models;
using BlueprintProWeb.Services;
using BlueprintProWeb.Settings;
using BlueprintProWeb.ViewModels;
using iText.Commons.Actions.Contexts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Embeddings;
using Stripe;
using Stripe.Checkout;
using System.Globalization;
using System.Text.Json;

namespace BlueprintProWeb.Controllers.ClientSide
{
    public class ClientInterfaceController : Controller
    {
        private readonly AppDbContext context;
        private readonly UserManager<User> userManager;
        private readonly OpenAIClient _openAi;
        private readonly EmbeddingClient _embeddingClient;
        private readonly IHubContext<ChatHub> _hubContext;
        private readonly StripeSettings _stripeSettings;

        public ClientInterfaceController(
           AppDbContext _context,
           UserManager<User> _userManager,
           OpenAIClient openAi,
           EmbeddingClient embeddingClient,
           IHubContext<ChatHub> hubContext,
           IOptions<StripeSettings> stripeSettings)
        {
            context = _context;
            userManager = _userManager;
            _openAi = openAi;
            _embeddingClient = embeddingClient;
            _hubContext = hubContext;
            _stripeSettings = stripeSettings.Value;
        }


        public async Task<IActionResult> ClientDashboard()
        {
            var currentUser = await userManager.GetUserAsync(User);
            if (currentUser == null)
                return RedirectToAction("Login", "Account");

            ViewData["UserFirstName"] = currentUser.user_fname ?? "User";

            // Get statistics for the current client
            var userId = currentUser.Id;

            // Total Matches - count all matches where this user is the client
            var totalMatches = await context.Matches
                .CountAsync(m => m.ClientId == userId && m.MatchStatus == "Approved");

            // Total Purchases - count blueprints purchased by this client
            var totalPurchases = await context.Blueprints
                .CountAsync(bp => bp.clentId == userId && !bp.blueprintIsForSale);

            // Total Projects - count projects where this user is the client
            var totalProjects = await context.Projects
                .CountAsync(p => p.user_clientId == userId);

            // Recent matches (last 5)
            var recentMatches = await context.Matches
                .Where(m => m.ClientId == userId && m.MatchStatus == "Approved")
                .Include(m => m.Architect)
                .OrderByDescending(m => m.MatchDate)
                .Take(5)
                .Select(m => new MatchSummary
                {
                    ArchitectId = m.ArchitectId, // Added for messaging functionality
                    ArchitectName = $"{m.Architect.user_fname} {m.Architect.user_lname}",
                    ArchitectSpecialty = m.Architect.user_Style ?? "General Architecture",
                    Status = m.MatchStatus,
                    MatchDate = m.MatchDate
                })
                .ToListAsync();

            // Recent purchases (last 5)
            var recentPurchases = await context.Blueprints
                .Where(bp => bp.clentId == userId && !bp.blueprintIsForSale)
                .OrderByDescending(bp => bp.blueprintId) // Using blueprintId as proxy for purchase order
                .Take(5)
                .Select(bp => new BlueprintPurchase
                {
                    BlueprintId = bp.blueprintId, // Added for project tracker linking
                    BlueprintName = bp.blueprintName,
                    PurchaseDate = DateTime.UtcNow, // You might want to add a PurchaseDate field to Blueprint model
                    Price = bp.blueprintPrice
                })
                .ToListAsync();

            var allProjectsRaw = await context.Projects
                .Where(p => p.user_clientId == userId)
                .Include(p => p.Architect)
                .OrderByDescending(p => p.project_startDate)
                .ToListAsync();

            var projects = new List<ProjectOverview>();

            foreach (var p in allProjectsRaw)
            {
                var tracker = await context.ProjectTrackers
                    .FirstOrDefaultAsync(pt => pt.project_Id == p.project_Id);

                var actualStatus = tracker?.projectTrack_Status ?? p.project_Status;

                projects.Add(new ProjectOverview
                {
                    ProjectTitle = p.project_Title,
                    Status = actualStatus,
                    ProgressPercentage = CalculateProjectProgressFromTracker(actualStatus, p.project_Status),
                    StartDate = p.project_startDate,
                    ArchitectName = $"{p.Architect.user_fname} {p.Architect.user_lname}"
                });
            }

            var dashboardViewModel = new ClientDashboardViewModel
            {
                TotalMatches = totalMatches,
                TotalPurchases = totalPurchases,
                TotalProjects = totalProjects,
                RecentMatches = recentMatches,
                RecentPurchases = recentPurchases,
                Projects = projects // <==== NEW
            };


            return View(dashboardViewModel);
        }

        private int CalculateProjectProgressFromTracker(string trackerStatus, string projectStatus)
        {
            // If project is finished, show 100%
            if (projectStatus?.ToLower() == "finished")
                return 100;
                
            // Calculate progress based on actual ProjectTracker status
            return trackerStatus?.ToLower() switch
            {
                "review" => 33,           // Review phase = 33%
                "compliance" => 66,       // Compliance phase = 66%
                "finalization" => 90,     // Finalization phase = 90%
                _ => CalculateProjectProgress(projectStatus) // Fallback to old method
            };
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
                _ => 0
            };
        }

        public IActionResult BlueprintMarketplace()
        {
            ViewBag.StripePublishableKey = _stripeSettings.PublishableKey;

            // Fetch only blueprints that are marked as for sale
            var availableBlueprints = context.Blueprints
                .Where(bp => bp.blueprintIsForSale)
                .ToList();

            return View("BlueprintMarketplace", availableBlueprints);
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddToCart([FromBody] CartRequest model)
        {
            var user = await userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            var cart = context.Carts
                .Include(c => c.Items)
                .FirstOrDefault(c => c.UserId == user.Id);

            if (cart == null)
            {
                cart = new Cart { UserId = user.Id, Items = new List<CartItem>() };
                context.Carts.Add(cart);
            }

            var existingItem = cart.Items.FirstOrDefault(i => i.BlueprintId == model.BlueprintId);
            if (existingItem != null)
                existingItem.Quantity += model.Quantity;
            else
                cart.Items.Add(new CartItem { BlueprintId = model.BlueprintId, Quantity = model.Quantity });

            await context.SaveChangesAsync();
            return Json(new { success = true });
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> GetCart()
        {
            var user = await userManager.GetUserAsync(User);
            if (user == null)
                return Unauthorized();

            var cart = context.Carts
                .Where(c => c.UserId == user.Id)
                .Select(c => new CartViewModel
                {
                    CartId = c.CartId,
                    Items = c.Items.Select(i => new CartItemViewModel
                    {
                        CartItemId = i.CartItemId,
                        BlueprintId = i.BlueprintId,
                        Name = i.Blueprint.blueprintName,
                        Image = i.Blueprint.blueprintImage,
                        Price = i.Blueprint.blueprintPrice,
                        Quantity = i.Quantity
                    }).ToList()
                })
                .FirstOrDefault();

            return Json(cart ?? new CartViewModel { CartId = 0, Items = new List<CartItemViewModel>() });
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> RemoveFromCart([FromBody] int blueprintId)
        {
            var user = await userManager.GetUserAsync(User);
            if (user == null)
                return Unauthorized();

            var cart = await context.Carts
                .Include(c => c.Items)
                .FirstOrDefaultAsync(c => c.UserId == user.Id);

            if (cart == null)
                return NotFound(new { success = false, message = "Cart not found" });

            var item = cart.Items.FirstOrDefault(i => i.BlueprintId == blueprintId);
            if (item == null)
                return NotFound(new { success = false, message = "Item not found" });

            cart.Items.Remove(item);
            context.CartItems.Remove(item); // make sure this matches your actual table name
            await context.SaveChangesAsync();

            return Ok(new { success = true });
        }

        public class CartRequest
        {
            public int BlueprintId { get; set; }
            public int Quantity { get; set; }
        }

        // -------------------- PAYMENT --------------------
        [HttpPost]
        public IActionResult CreateCheckoutSession([FromBody] List<CartItemViewModel> cart)
        {
            if (cart == null || !cart.Any())
                return BadRequest("Cart is empty or not received");

            if (cart.Any(c => c.Price <= 0))
                return BadRequest("One or more items have invalid price.");

            var lineItems = cart.Select(item => new SessionLineItemOptions
            {
                PriceData = new SessionLineItemPriceDataOptions
                {
                    UnitAmount = (long)(item.Price * 100), // already validated
                    Currency = "php",
                    ProductData = new SessionLineItemPriceDataProductDataOptions
                    {
                        Name = item.Name ?? "Blueprint"
                    }
                },
                Quantity = item.Quantity > 0 ? item.Quantity : 1
            }).ToList();

            var options = new SessionCreateOptions
            {
                PaymentMethodTypes = new List<string> { "card" },
                LineItems = lineItems,
                Mode = "payment",
                SuccessUrl = $"{Request.Scheme}://{Request.Host}/ClientInterface/BlueprintMarketplace?session_id={{CHECKOUT_SESSION_ID}}",
                CancelUrl = $"{Request.Scheme}://{Request.Host}/ClientInterface/Cancel"
            };

            Stripe.StripeConfiguration.ApiKey = _stripeSettings.SecretKey;

            var service = new SessionService();
            var session = service.Create(options);

            return Json(new { id = session.Id });
        }




        public IActionResult Success() => View();
        public IActionResult Cancel() => View();



        [HttpPost]
        [Authorize]
        public async Task<IActionResult> CompletePurchase([FromBody] List<int> blueprintIds)
        {
            var user = await userManager.GetUserAsync(User);
            if (user == null)
                return Unauthorized();

            if (blueprintIds == null || !blueprintIds.Any())
                return BadRequest(new { success = false, message = "No blueprints selected for purchase." });

            // Get all purchased blueprints
            var purchasedBlueprints = await context.Blueprints
                .Where(bp => blueprintIds.Contains(bp.blueprintId))
                .ToListAsync();

            if (!purchasedBlueprints.Any())
                return NotFound(new { success = false, message = "No matching blueprints found." });

            foreach (var bp in purchasedBlueprints)
            {
                // Mark blueprint as sold
                bp.blueprintIsForSale = false;

                // Record buyer (client)
                bp.clentId = user.Id;

                // AUTOMATIC MATCHING
                // Ensure the blueprint has an architect assigned
                if (!string.IsNullOrEmpty(bp.architectId))
                {
                    // Create Match record
                    var match = new Match
                    {
                        ClientId = user.Id,
                        ArchitectId = bp.architectId,
                        MatchStatus = "Approved", // You can change to "Pending"
                        MatchDate = DateTime.UtcNow
                    };

                    context.Matches.Add(match);
                }
            }

            // Clear user's cart after successful purchase
            var cart = await context.Carts
                .Include(c => c.Items)
                .FirstOrDefaultAsync(c => c.UserId == user.Id);

            if (cart != null && cart.Items.Any())
                cart.Items.Clear();

            await context.SaveChangesAsync();

            return Json(new { success = true, message = "Purchase completed successfully." });
        }



        public async Task<IActionResult> Projects()
        {
            var user = await userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            var projects = await context.Projects
                .Where(p => p.user_clientId == user.Id
                            && (p.project_Status == "Ongoing" || p.project_Status == "Finished"))
                .Include(p => p.Blueprint)
                .Include(p => p.Architect)
                .ToListAsync();

            return View(projects);
        }

        // 🔹 Get Client's Matches
        [HttpGet]
        public async Task<IActionResult> Matches(string? query)
        {
            var currentUser = await userManager.GetUserAsync(User);
            if (currentUser == null)
                return RedirectToAction("Login", "Account");

            var chatClient = _openAi.GetChatClient("gpt-5-mini");

            // 🔹 Step 0: Check if request is architecture-related
            var scopeCheckMessages = new List<ChatMessage>
{
                new SystemChatMessage(
                    @"You are classifying client messages for an architecture matching system.

                    Classify the message into ONE of the following categories:
                    - ARCHITECTURE_RELATED: Any message related to architectural design, building design, interiors, construction, renovation, styles, or architectural capabilities — even if vague or incomplete.
                    - NOT_ARCHITECTURE_RELATED: Clearly unrelated topics (games, programming, food, weather, etc.)

                    Important rules:
                    - Vague or short messages are STILL architecture-related if they mention styles, design, architects, buildings, or services.
                    - Missing budget, location, or project details does NOT make it out of scope.
                    - Capability statements like ""can do modern designs"" ARE architecture-related.

                    Respond ONLY with:
                    ARCHITECTURE_RELATED or NOT_ARCHITECTURE_RELATED"
                    ),
                    new UserChatMessage(query ?? "")
                };


            var scopeResult = await chatClient.CompleteChatAsync(scopeCheckMessages);

            bool isArchitectureRelated =
                scopeResult.Value.Content[0].Text.Trim()
                    .Equals("ARCHITECTURE_RELATED", StringComparison.OrdinalIgnoreCase);

            bool outOfScope = !isArchitectureRelated;

            // Step 1: Default query
            string searchQuery = string.IsNullOrWhiteSpace(query)
                ? $"Style: {currentUser.user_Style}, Location: {currentUser.user_Location}, Budget: {currentUser.user_Budget}"
                : query;

            // 🔍 Check if client request lacks key details
            bool lacksDetails =
                !searchQuery.Contains("budget", StringComparison.OrdinalIgnoreCase) ||
                !searchQuery.Contains("location", StringComparison.OrdinalIgnoreCase) ||
                !searchQuery.Contains("style", StringComparison.OrdinalIgnoreCase);

            

            // Step 2: Expand query
            var messages = new List<ChatMessage>
    {
        new SystemChatMessage("You rewrite client needs into a clear architectural request."),
        new UserChatMessage($"User request: {searchQuery}. Expand into 2–3 descriptive sentences.")
    };

            var response = await chatClient.CompleteChatAsync(messages);
            var expansion = response.Value.Content[0].Text;
            var finalQuery = $"{searchQuery}. Expanded: {expansion}";

            // Step 3: Generate embedding
            var embeddingResponse = await _embeddingClient.GenerateEmbeddingAsync(finalQuery);
            var queryVector = embeddingResponse.Value.ToFloats().ToArray();

            // Step 4: Compare with architects
            var architects = context.Users
                .Where(u => u.user_role == "Architect" && !string.IsNullOrEmpty(u.PortfolioEmbedding))
                .ToList();

            var ranked = architects
                .Select(a =>
                {
                    var vecA = ParseEmbedding(a.PortfolioEmbedding);
                    if (vecA == null || vecA.Length != queryVector.Length) return null;

                    var score = CosineSimilarity(queryVector, vecA);
                    if (a.IsPro) score += 0.05;

                    return new MatchViewModel
                    {
                        MatchId = null,
                        ClientId = currentUser.Id,
                        ClientName = $"{currentUser.user_fname} {currentUser.user_lname}",

                        ArchitectId = a.Id,
                        ArchitectName = $"{a.user_fname} {a.user_lname}",
                        ArchitectStyle = a.user_Style,
                        ArchitectLocation = a.user_Location,
                        ArchitectBudget = a.user_Budget,

                        ProfilePhoto = string.IsNullOrEmpty(a.user_profilePhoto)
                            ? Url.Content("~/images/profile.jpg")
                            : Url.Content(a.user_profilePhoto),

                        MatchStatus = a.IsPro ? "AI + Portfolio Match (Pro)" : "AI + Portfolio Match",
                        MatchDate = DateTime.UtcNow,

                        SimilarityScore = score,
                        SimilarityPercentage = Math.Round(score * 100, 1),

                        TotalRatings = a.user_Rating ?? 0.0,
                        RatingCount = context.Projects.Count(p =>
                            p.user_architectId == a.Id &&
                            p.project_clientHasRated),

                        RealMatchStatus = context.Matches
                            .Where(m => m.ClientId == currentUser.Id && m.ArchitectId == a.Id)
                            .Select(m => m.MatchStatus)
                            .FirstOrDefault(),

                        MatchExplanation = null
                    };
                })
                .Where(x => x != null)
                .OrderByDescending(x => x!.SimilarityScore)
                .Take(25)
                .ToList();

            // Compute average ratings
            foreach (var vm in ranked)
            {
                vm.AverageRating = vm.RatingCount > 0
                    ? Math.Round(vm.TotalRatings / vm.RatingCount, 1)
                    : 0.0;
            }

            // Strong match & feedback
            bool hasStrongMatch = ranked.Count > 0 && ranked[0].SimilarityPercentage >= 35;
            bool showFeedback = !hasStrongMatch;

            // Return JSON for AJAX
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return Json(new
                {
                    matches = ranked,
                    showFeedback,
                    lacksDetails,
                    outOfScope
                });
            }

            // For normal page load
            ViewBag.HasStrongMatch = hasStrongMatch;
            ViewBag.OutOfScope = outOfScope;
            ViewBag.LacksDetails = lacksDetails;

            return View(ranked);
        }



        private double CosineSimilarity(float[] v1, float[] v2)
            {
                if (v1.Length != v2.Length) return 0;

                double dot = 0, mag1 = 0, mag2 = 0;
                for (int i = 0; i < v1.Length; i++)
                {
                    dot += v1[i] * v2[i];
                    mag1 += v1[i] * v1[i];
                    mag2 += v2[i] * v2[i];
                }
                return dot / (Math.Sqrt(mag1) * Math.Sqrt(mag2));
            }

            private float[] ParseEmbedding(string embeddingString)
            {
                return embeddingString
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s =>
                    {
                        if (float.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                            return value;
                        return 0f; // fallback if parsing fails
                    })
                    .ToArray();
            }

        [HttpGet]
        public async Task<IActionResult> Messages(string architectId)
        {
            var currentUser = await userManager.GetUserAsync(User);
            if (currentUser == null)
                return Unauthorized();

            // Timezone (Philippines = UTC+8)
            var phTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Singapore Standard Time");

            // ✅ 1. Load all matches for this client
            var matches = await context.Matches
                .Where(m => m.ClientId == currentUser.Id && m.MatchStatus == "Approved")
                .Include(m => m.Architect)
                .Select(m => new MatchViewModel
                {
                    MatchId = m.MatchId.ToString(),
                    ClientId = m.ClientId,
                    ArchitectId = m.ArchitectId,
                    ArchitectName = m.Architect.user_fname + " " + m.Architect.user_lname,
                    ArchitectEmail = m.Architect.Email,
                    ArchitectPhone = m.Architect.PhoneNumber,
                    MatchStatus = m.MatchStatus,
                    MatchDate = m.MatchDate,
                    ArchitectProfileUrl = string.IsNullOrEmpty(m.Architect.user_profilePhoto)
                        ? "/images/profile.jpg"
                        : m.Architect.user_profilePhoto
                })
                .ToListAsync();

            // ✅ 2. Load conversations (group by Architect)
            var conversations = await context.Messages
                .Where(m => m.ClientId == currentUser.Id || m.ArchitectId == currentUser.Id)
                .Include(m => m.Architect)
                .Include(m => m.Sender)
                .GroupBy(m => m.ArchitectId)
                .Select(g => new ChatViewModel
                {
                    ArchitectId = g.Key,
                    ArchitectName = g.First().Architect.user_fname + " " + g.First().Architect.user_lname,
                    LastMessageTime = TimeZoneInfo.ConvertTimeFromUtc(g.Max(x => x.MessageDate), phTimeZone),
                    Messages = new List<MessageViewModel>(),
                    UnreadCount = g.Count(x => x.SenderId != currentUser.Id && !x.IsRead),
                    ArchitectProfileUrl = string.IsNullOrEmpty(g.First().Architect.user_profilePhoto)
                        ? "/images/profile.jpg"
                        : g.First().Architect.user_profilePhoto
                })
                .ToListAsync();

            // ✅ 3. Load active chat (if selected)
            ChatViewModel? activeChat = null;
            if (!string.IsNullOrEmpty(architectId))
            {
                var messages = await context.Messages
                    .Where(m =>
                        (m.ClientId == currentUser.Id && m.ArchitectId == architectId) ||
                        (m.ClientId == architectId && m.ArchitectId == currentUser.Id))
                    .Include(m => m.Sender)
                    .OrderBy(m => m.MessageDate)
                    .ToListAsync();

                // mark unread messages as read
                var unreadMessages = messages
                    .Where(m => m.SenderId != currentUser.Id && !m.IsRead)
                    .ToList();

                foreach (var msg in unreadMessages)
                    msg.IsRead = true;

                if (unreadMessages.Any())
                    await context.SaveChangesAsync();

                // convert messages to PH time
                var vmMessages = messages.Select(m => new MessageViewModel
                {
                    MessageId = m.MessageId.ToString(),
                    ClientId = m.ClientId,
                    ArchitectId = m.ArchitectId,
                    SenderId = m.SenderId,
                    MessageBody = m.MessageBody,
                    MessageDate = TimeZoneInfo.ConvertTimeFromUtc(m.MessageDate, phTimeZone),
                    IsRead = m.IsRead,
                    IsDeleted = m.IsDeleted,
                    AttachmentUrl = m.AttachmentUrl,
                    SenderName = m.Sender != null
                        ? m.Sender.user_fname + " " + m.Sender.user_lname
                        : "Unknown",
                    SenderProfilePhoto = m.Sender != null && !string.IsNullOrEmpty(m.Sender.user_profilePhoto)
                        ? m.Sender.user_profilePhoto
                        : "/images/profile.jpg",
                    IsOwnMessage = (m.SenderId == currentUser.Id)
                }).ToList();

                var matchInfo = matches.FirstOrDefault(m => m.ArchitectId == architectId);

                activeChat = new ChatViewModel
                {
                    ArchitectId = architectId,
                    ArchitectName = matchInfo?.ArchitectName ?? "Unknown",
                    LastMessageTime = vmMessages.LastOrDefault()?.MessageDate
                        ?? TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, phTimeZone),
                    Messages = vmMessages,
                    ArchitectProfileUrl = matchInfo?.ArchitectProfileUrl ?? "/images/profile.jpg"
                };
            }

            // ✅ 4. Combine everything
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
        public async Task<IActionResult> SendMessage(string architectId, string messageBody)
        {
            var currentUser = await userManager.GetUserAsync(User);
            if (currentUser == null)
                return Unauthorized();

            if (string.IsNullOrWhiteSpace(messageBody))
                return RedirectToAction("Messages", new { architectId });

            // Timezone (Philippines)
            var phTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Singapore Standard Time");

            // store in UTC
            var message = new Message
            {
                MessageId = Guid.NewGuid(),
                ClientId = currentUser.Id,
                ArchitectId = architectId,
                SenderId = currentUser.Id,
                MessageBody = messageBody,
                MessageDate = DateTime.UtcNow,
                IsRead = false
            };

            context.Messages.Add(message);
            await context.SaveChangesAsync();

            // ✅ SignalR broadcast (show PH time in chat)
            await _hubContext.Clients.User(architectId).SendAsync("ReceiveMessage", new
            {
                senderId = currentUser.Id,
                senderName = currentUser.user_fname + " " + currentUser.user_lname,
                messageBody = messageBody,
                messageDate = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, phTimeZone).ToString("g"),

                SenderProfilePhoto = string.IsNullOrEmpty(currentUser.user_profilePhoto)
                    ? "/images/profile.jpg"
                    : currentUser.user_profilePhoto
                        .Replace("~", "")
                        .Replace("wwwroot", "")

            });

            await _hubContext.Clients.User(architectId)
            .SendAsync("ReceiveMessageUpdate");

            return RedirectToAction("Messages", new { architectId });
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
                ProjectStatus = project.project_Status,
                IsRated = project.project_clientHasRated,
                ArchitectName = $"{context.Users.FirstOrDefault(u => u.Id == project.user_architectId)?.user_fname} " +
                $"{context.Users.FirstOrDefault(u => u.Id == project.user_architectId)?.user_lname}"

            };

            return View(vm);
        }

        [HttpGet]
        public async Task<IActionResult> GetArchitectProfile(string id)
        {
            var architect = await userManager.FindByIdAsync(id);
            if (architect == null) return NotFound();

            var credentialsPath = string.IsNullOrEmpty(architect.user_CredentialsFile)
                ? null
                : Url.Content($"~/credentials/{architect.user_CredentialsFile}");

            return Json(new
            {
                fullName = $"{architect.user_fname} {architect.user_lname}",
                email = architect.Email,
                phone = architect.PhoneNumber,
                photo = string.IsNullOrEmpty(architect.user_profilePhoto)
                    ? Url.Content("~/images/profile.jpg")
                    : Url.Content(architect.user_profilePhoto),
                license = architect.user_licenseNo,
                style = architect.user_Style,
                specialization = architect.user_Specialization,
                location = architect.user_Location,
                credentialsFile = credentialsPath
            });
        }

        [HttpPost]
        public IActionResult SubmitRating(string projectId, int rating)
        {
            // Find the project
            var project = context.Projects.FirstOrDefault(p => p.project_Id == projectId);
            if (project == null) return NotFound();

            // Find the architect user
            var architect = context.Users.FirstOrDefault(u => u.Id == project.user_architectId);
            if (architect == null) return NotFound();
            
            architect.user_Rating = (architect.user_Rating ?? 0) + rating;

            // Mark the project as rated
            project.project_clientHasRated = true;

            // Save
            context.SaveChanges();

            return Json(new { success = true });
        }

        [HttpGet]
        public async Task<IActionResult> Notifications()
        {
            var currentUser = await userManager.GetUserAsync(User);
            if (currentUser == null)
                return RedirectToAction("Login", "Account");

            var notifications = await context.Notifications
                .Where(n => n.user_Id == currentUser.Id)
                .OrderByDescending(n => n.notification_Date)
                .ToListAsync();

            return View(notifications);
        }

        [HttpPost]
        public async Task<IActionResult> MarkAsRead(int id)
        {
            var notif = await context.Notifications.FindAsync(id);
            if (notif != null)
            {
                notif.notification_isRead = true;
                await context.SaveChangesAsync();
            }
            return Ok();
        }

        [HttpGet]
        public async Task<IActionResult> GetUnreadNotificationsCount()
        {
            var currentUser = await userManager.GetUserAsync(User);
            if (currentUser == null)
                return Json(0);

            var count = await context.Notifications
                .CountAsync(n => n.user_Id == currentUser.Id && !n.notification_isRead);

            return Json(count);
        }

        [HttpGet]
        public async Task<IActionResult> GetUnreadMessagesCount()
        {
            var currentUser = await userManager.GetUserAsync(User);
            if (currentUser == null)
                return Json(0);

            var count = await context.Messages.CountAsync(m =>
                ((m.ClientId == currentUser.Id) || (m.ArchitectId == currentUser.Id)) &&
                 m.SenderId != currentUser.Id &&
                 !m.IsRead &&
                 !m.IsDeleted
            );

            return Json(count);
        }

        [HttpGet]
        public async Task<IActionResult> GetMyMatches()
        {
            var currentUser = await userManager.GetUserAsync(User);
            if (currentUser == null)
                return Json(new List<object>());

            var matches = await context.Matches
                .Where(m => m.ClientId == currentUser.Id)
                .Include(m => m.Architect)
                .OrderByDescending(m => m.MatchDate)
                .Take(10)
                .Select(m => new
                {
                    matchId = m.MatchId,
                    architectId = m.ArchitectId,
                    architectName = $"{m.Architect.user_fname} {m.Architect.user_lname}",
                    architectProfileUrl = string.IsNullOrEmpty(m.Architect.user_profilePhoto)
                        ? "/images/profile.jpg"
                        : m.Architect.user_profilePhoto,
                    matchStatus = m.MatchStatus,
                    matchDate = m.MatchDate.ToString("MMM dd, yyyy")
                })
                .ToListAsync();

            return Json(matches);
        }

        [HttpPost]
        public async Task<IActionResult> RequestMatch(string architectId)
        {
            var currentUser = await userManager.GetUserAsync(User);
            if (currentUser == null)
                return Json(new { success = false, message = "Not logged in." });

            // Check if already matched
            var existing = await context.Matches
                .FirstOrDefaultAsync(m => m.ClientId == currentUser.Id && m.ArchitectId == architectId);

            if (existing != null)
                return Json(new { success = false, message = "Match request already sent." });

            var match = new Match
            {
                ClientId = currentUser.Id,
                ArchitectId = architectId,
                MatchStatus = "Pending",
                MatchDate = DateTime.UtcNow
            };

            context.Matches.Add(match);
            await context.SaveChangesAsync();

            // 🔹 Create notification for the architect
            var architect = await context.Users.FindAsync(architectId);
            if (architect != null)
            {
                var notif = new Notification
                {
                    user_Id = architect.Id,
                    notification_Title = "New Match Request",
                    notification_Message = $"{currentUser.user_fname} {currentUser.user_lname} wants to match with you.",
                    notification_Date = DateTime.Now,
                    notification_isRead = false
                };

                context.Notifications.Add(notif);
                await context.SaveChangesAsync();

                await _hubContext.Clients
                    .User(architect.Id)
                    .SendAsync("ReceiveNotification", new
                    {
                        title = notif.notification_Title,
                        message = notif.notification_Message,
                        date = notif.notification_Date.ToString("g")
                    });
            }

            return Json(new { success = true, message = "✅ Match request sent successfully." });
        }

        [HttpGet]
        public async Task<IActionResult> GetPurchasedBlueprints()
        {
            var user = await userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            var purchased = await context.Blueprints
                .Where(bp => bp.clentId == user.Id && !bp.blueprintIsForSale)
                .Select(bp => new {
                    id = bp.blueprintId,
                    name = bp.blueprintName,
                    image = bp.blueprintImage,
                    price = bp.blueprintPrice,
                    style = bp.blueprintStyle
                })
                .ToListAsync();

            return Json(purchased);
        }

        [HttpPost]
        public async Task<IActionResult> ExplainMatch([FromBody] ExplainMatchRequest request)
        {
            var architect = await context.Users.FindAsync(request.ArchitectId);
            if (architect == null) return NotFound();

            var explanation = await GenerateMatchExplanation(
                request.Query,
                architect,
                request.LacksDetails
               );

            return Json(new { explanation });
        }

        public class ExplainMatchRequest
        {
            public string ArchitectId { get; set; }
            public string Query { get; set; }
            public bool LacksDetails { get; set; }
        }



        private async Task<string> GenerateMatchExplanation(
    string clientQuery,
    User architect,
    bool lacksDetails)
        {
            var chatClient = _openAi.GetChatClient("gpt-5-mini");

            // ✅ Build the user prompt FIRST
            var userPrompt = $@"
                Client request:
                {clientQuery}

                Reference material (internal):
                {architect.PortfolioText}

                Explain why this recommendation fits the client's request.
                ";

            // ✅ Conditionally ask for clarification guidance
            if (lacksDetails)
            {
                userPrompt += @"

                    Also, if the client's request is missing important details,
                    gently suggest what information would help clarify the project
                    (such as budget range, project scale, style preference, or location).";
            }

            var messages = new List<ChatMessage>
    {
        new SystemChatMessage(
                @"You are a professional architectural matching assistant.

                Your task is to explain WHY the suggested architect could be suitable for the client's needs,
                even if the architect’s primary style or specialty does not exactly match the request.

                Guidelines:
                - Speak directly to the client using ""you"" and ""your"".
                - Focus on transferable skills, adaptable design principles, comparable project experience, or flexible approaches.
                - If styles differ, explain how the architect’s experience can still support the client’s goals.
                - Base reasoning only on the provided reference material.
                - Never mention portfolios, internal systems, matching, scores, or AI.
                - Do not list credentials.
                - Do not describe the architect personally.
                - Keep the tone reassuring and professional.
                - Maximum of 2 short sentences."
             ),

                new UserChatMessage(userPrompt)
    };

            var response = await chatClient.CompleteChatAsync(messages);
            return response.Value.Content[0].Text.Trim();
        }

    }
}

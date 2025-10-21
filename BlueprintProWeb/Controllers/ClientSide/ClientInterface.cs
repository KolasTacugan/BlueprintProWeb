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
                .CountAsync(m => m.ClientId == userId);

            // Total Purchases - count blueprints purchased by this client
            var totalPurchases = await context.Blueprints
                .CountAsync(bp => bp.clentId == userId && !bp.blueprintIsForSale);

            // Total Projects - count projects where this user is the client
            var totalProjects = await context.Projects
                .CountAsync(p => p.user_clientId == userId);

            // Recent matches (last 5)
            var recentMatches = await context.Matches
                .Where(m => m.ClientId == userId)
                .Include(m => m.Architect)
                .OrderByDescending(m => m.MatchDate)
                .Take(5)
                .Select(m => new MatchSummary
                {
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
                    BlueprintName = bp.blueprintName,
                    PurchaseDate = DateTime.UtcNow, // You might want to add a PurchaseDate field to Blueprint model
                    Price = bp.blueprintPrice
                })
                .ToListAsync();

            // Current/most recent project - get the raw data first
            var currentProjectRaw = await context.Projects
                .Where(p => p.user_clientId == userId)
                .Include(p => p.Architect)
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
                    ProgressPercentage = CalculateProjectProgress(currentProjectRaw.project_Status), // Now safe to call
                    StartDate = currentProjectRaw.project_startDate,
                    ArchitectName = $"{currentProjectRaw.Architect.user_fname} {currentProjectRaw.Architect.user_lname}"
                };
            }

            var dashboardViewModel = new ClientDashboardViewModel
            {
                TotalMatches = totalMatches,
                TotalPurchases = totalPurchases,
                TotalProjects = totalProjects,
                RecentMatches = recentMatches,
                RecentPurchases = recentPurchases,
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
                // ✅ Mark as sold
                bp.blueprintIsForSale = false;

                // ✅ Record buyer (if your model has clientId)
                bp.clentId = user.Id;

                // Optionally log or handle transfer ownership timestamp, etc.
            }

            // ✅ Clear user's cart after successful purchase
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
                .Where(p => p.user_clientId == user.Id) 
                .Include(p => p.Blueprint)
                .Include(p => p.Architect)
                .ToListAsync();

            return View(projects);
        }

        // 🔹 AI Matching (Profile + Query)
        [HttpGet]
        public async Task<IActionResult> Matches(string? query)
        {
            var count = context.Users.Count(u => u.user_role == "Architect" && !string.IsNullOrEmpty(u.PortfolioEmbedding));
            Console.WriteLine($"Architects with embeddings: {count}");

            var currentUser = await userManager.GetUserAsync(User);
            if (currentUser == null)
            {
                return RedirectToAction("Login", "Account");
            }

            // Step 1: Default query is user's profile if empty
            string searchQuery = query;
            if (string.IsNullOrWhiteSpace(searchQuery))
            {
                searchQuery = $"Style: {currentUser.user_Style}, Location: {currentUser.user_Location}, Budget: {currentUser.user_Budget}";
            }

            // Step 2: Expand into natural descriptive request
            // Step 2: Expand query with GPT
            var chatClient = _openAi.GetChatClient("gpt-5-mini");
            var messages = new List<ChatMessage>
                {
                    new SystemChatMessage("You are an assistant that rewrites client needs into a natural request for an architect."),
                    new UserChatMessage($"User request: {searchQuery}. Expand into 2–3 descriptive sentences.")
                };

            var response = await chatClient.CompleteChatAsync(messages);
            var expansion = response.Value.Content[0].Text;
            var finalQuery = $"{searchQuery}. Expanded: {expansion}";

            // Step 3: Embedding
            var embeddingResponse = await _embeddingClient.GenerateEmbeddingAsync(finalQuery);
            var queryVector = embeddingResponse.Value.ToFloats().ToArray();


            // Step 4: Compare with portfolio embeddings
            var architects = context.Users
                .Where(u => u.user_role == "Architect" && !string.IsNullOrEmpty(u.PortfolioEmbedding))
                .ToList();

            var ranked = architects
             .Select(a =>
             {
                 var vecA = ParseEmbedding(a.PortfolioEmbedding);

                 if (vecA == null || vecA.Length == 0 || vecA.Length != queryVector.Length)
                     return (Architect: a, Score: double.MinValue);

                 var score = CosineSimilarity(queryVector, vecA);

                 // ✅ Pro user boost (e.g., +5% boost to score)
                 if (a.IsPro)
                     score += 0.05;  // you can tune this from 0.03–0.1 depending on how strong you want the bias

                 return (Architect: a, Score: score);
             })
             .Where(x => x.Score != double.MinValue)
             .OrderByDescending(x => x.Score)
             .Take(10)
             .Select(x => new MatchViewModel
             {
                 MatchId = null,
                 ClientId = currentUser.Id,
                 ClientName = $"{currentUser.user_fname} {currentUser.user_lname}",
                 ArchitectId = x.Architect.Id,
                 ArchitectName = $"{x.Architect.user_fname} {x.Architect.user_lname}",
                 ArchitectStyle = x.Architect.user_Style,
                 ArchitectLocation = x.Architect.user_Location,
                 ArchitectBudget = x.Architect.user_Budget,
                 ProfilePhoto = string.IsNullOrEmpty(x.Architect.user_profilePhoto)
                    ? Url.Content("~/images/profile.jpg")
                    : Url.Content(x.Architect.user_profilePhoto),
                 MatchStatus = x.Architect.IsPro ? "AI + Portfolio Match (Pro)" : "AI + Portfolio Match",
                 MatchDate = DateTime.UtcNow,
                
                 // 🟦 NEW: Add rating info (no DB structure changed)
                 TotalRatings = x.Architect.user_Rating ?? 0.0,
                 RatingCount = context.Projects.Count(p => p.user_architectId == x.Architect.Id && p.project_clientHasRated == true)
             })
             .ToList();

            foreach (var archi in ranked)
            {
                archi.AverageRating = archi.RatingCount > 0
                    ? Math.Round(archi.TotalRatings / archi.RatingCount, 1)
                    : 0.0;
            }

            // Step 5: Return JSON if AJAX, else View
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                        {
                            return Json(ranked);
                        }

            return View(ranked);

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
            }

            return Json(new { success = true, message = "✅ Match request sent successfully." });
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
                    ArchitectEmail = m.Architect.Email,              // make sure your User entity has Email
                    ArchitectPhone = m.Architect.PhoneNumber,        // make sure your User entity has PhoneNumber
                    MatchStatus = m.MatchStatus,
                    MatchDate = m.MatchDate,
                    ArchitectProfileUrl = string.IsNullOrEmpty(m.Architect.user_profilePhoto)
                        ? "/images/default-profile.png"
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
                    LastMessageTime = g.Max(x => x.MessageDate),
                    Messages = new List<MessageViewModel>(),
                    UnreadCount = g.Count(x => x.SenderId != currentUser.Id && !x.IsRead),
                    ArchitectProfileUrl = string.IsNullOrEmpty(g.First().Architect.user_profilePhoto)
                        ? "/images/default-profile.png"
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

                var matchInfo = matches.FirstOrDefault(m => m.ArchitectId == architectId);

                activeChat = new ChatViewModel
                {
                    ArchitectId = architectId,
                    ArchitectName = matchInfo?.ArchitectName ?? "Unknown",
                    LastMessageTime = vmMessages.LastOrDefault()?.MessageDate ?? DateTime.UtcNow,
                    Messages = vmMessages,
                    ArchitectProfileUrl = matchInfo?.ArchitectProfileUrl ?? "/images/default-profile.png"
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

            // ✅ Optional SignalR broadcast
            await _hubContext.Clients.User(architectId).SendAsync("ReceiveMessage", new
            {
                SenderId = currentUser.Id,
                SenderName = currentUser.user_fname + " " + currentUser.user_lname,
                MessageBody = messageBody,
                MessageDate = DateTime.UtcNow.ToString("g"),
                SenderProfilePhoto = string.IsNullOrEmpty(currentUser.user_profilePhoto)
                    ? "/images/default-profile.png"
                    : currentUser.user_profilePhoto
            });

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
    }
}

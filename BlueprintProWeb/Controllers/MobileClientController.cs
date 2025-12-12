using BlueprintProWeb.Data;
using BlueprintProWeb.Hubs;
using BlueprintProWeb.Models;
using BlueprintProWeb.Settings;
using BlueprintProWeb.ViewModels;
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
using System.Text.Json.Serialization;

namespace BlueprintProWeb.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Produces("application/json")]
    public class MobileClientController : ControllerBase
    {
        private readonly AppDbContext context;
        private readonly UserManager<User> userManager;
        private readonly OpenAIClient _openAi;
        private readonly EmbeddingClient _embeddingClient;
        private readonly IHubContext<ChatHub> _hubContext;
        private readonly StripeSettings _stripeSettings;

        public MobileClientController(
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

        // -------------------- DASHBOARD --------------------
        [HttpGet("dashboard")]
        [Authorize]
        public async Task<IActionResult> GetDashboard()
        {
            try
            {
                var currentUser = await userManager.GetUserAsync(User);
                if (currentUser == null)
                    return Unauthorized(new { success = false, message = "User not authorized." });

                return Ok(new
                {
                    firstName = currentUser.user_fname,
                    lastName = currentUser.user_lname,
                    email = currentUser.Email,
                    role = currentUser.user_role
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        // -------------------- BLUEPRINT MARKETPLACE --------------------
        [HttpGet("marketplace")]
        public IActionResult GetMarketplace()
        {
            try
            {
                var baseUrl = $"{Request.Scheme}://{Request.Host}";

                var availableBlueprints = context.Blueprints
                    .Where(bp => bp.blueprintIsForSale)
                    .Select(bp => new
                    {
                        bp.blueprintId,
                        bp.blueprintName,
                        bp.blueprintPrice,
                        bp.blueprintDescription,
                        bp.blueprintIsForSale,
                        bp.blueprintStyle,

                        // ⭐ If image is stored in /images/filename.jpg:
                        blueprintImage = string.IsNullOrEmpty(bp.blueprintImage)
                            ? null
                            : $"{baseUrl}/images/{Path.GetFileName(bp.blueprintImage)}"
                    })
                    .ToList();

                var response = new
                {
                    StripePublishableKey = _stripeSettings.PublishableKey,
                    Blueprints = availableBlueprints
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }



        // -------------------- CART --------------------
        public class CartRequest
        {
            public int BlueprintId { get; set; }
            public int Quantity { get; set; }
            public string ClientId { get; set; }
        }

        [AllowAnonymous]
        [HttpPost("AddToCart")]
        public async Task<IActionResult> AddToCart([FromBody] CartRequest model)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(model.ClientId))
                    return BadRequest(new { success = false, message = "ClientId is required." });

                // ✅ Load Items and their related Blueprints
                var cart = await context.Carts
                    .Include(c => c.Items)
                        .ThenInclude(i => i.Blueprint)
                    .FirstOrDefaultAsync(c => c.UserId == model.ClientId);

                if (cart == null)
                {
                    cart = new Cart { UserId = model.ClientId, Items = new List<CartItem>() };
                    context.Carts.Add(cart);
                }

                // Check if item exists
                var existingItem = cart.Items.FirstOrDefault(i => i.BlueprintId == model.BlueprintId);
                if (existingItem != null)
                {
                    existingItem.Quantity += model.Quantity;
                }
                else
                {
                    var newItem = new CartItem
                    {
                        BlueprintId = model.BlueprintId,
                        Quantity = model.Quantity
                    };
                    cart.Items.Add(newItem);
                }

                await context.SaveChangesAsync();

                // ✅ Reload the cart with Blueprint data after saving (important!)
                cart = await context.Carts
                    .Include(c => c.Items)
                        .ThenInclude(i => i.Blueprint)
                    .FirstOrDefaultAsync(c => c.CartId == cart.CartId);

                return Ok(new
                {
                    success = true,
                    message = "Item added to cart successfully.",
                    cart = new
                    {
                        cart.CartId,
                        Items = cart.Items.Select(i => new
                        {
                            i.CartItemId,
                            i.BlueprintId,
                            blueprintName = i.Blueprint?.blueprintName,   // Safe navigation
                            blueprintImage = i.Blueprint?.blueprintImage,
                            blueprintPrice = i.Blueprint?.blueprintPrice,
                            i.Quantity
                        })
                    }
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        [AllowAnonymous]
        [HttpGet("GetCart")]
        public async Task<IActionResult> GetCart([FromQuery] string clientId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(clientId))
                    return BadRequest(new { success = false, message = "ClientId is required." });

                var baseUrl = $"{Request.Scheme}://{Request.Host}";

                var cart = await context.Carts
                    .Include(c => c.Items)
                        .ThenInclude(i => i.Blueprint)
                    .Where(c => c.UserId == clientId)
                    .Select(c => new
                    {
                        c.CartId,
                        Items = c.Items.Select(i => new
                        {
                            i.CartItemId,
                            i.BlueprintId,
                            blueprintName = i.Blueprint.blueprintName,
                            blueprintImage = string.IsNullOrEmpty(i.Blueprint.blueprintImage)
                                ? null
                                : $"{baseUrl}/images/{Path.GetFileName(i.Blueprint.blueprintImage)}",
                            blueprintPrice = i.Blueprint.blueprintPrice,
                            i.Quantity
                        }).ToList()
                    })
                    .FirstOrDefaultAsync();

                if (cart == null)
                    return Ok(new { CartId = 0, Items = new List<object>() });

                return Ok(cart.Items);
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        
        [AllowAnonymous]
        [HttpPost("RemoveFromCart")]
        public async Task<IActionResult> RemoveFromCart([FromBody] RemoveCartRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.ClientId))
                    return BadRequest(new { success = false, message = "ClientId is required." });

                var cart = await context.Carts
                    .Include(c => c.Items)
                    .FirstOrDefaultAsync(c => c.UserId == request.ClientId);

                if (cart == null)
                    return NotFound(new { success = false, message = "Cart not found." });

                var item = cart.Items.FirstOrDefault(i => i.BlueprintId == request.BlueprintId);
                if (item == null)
                    return NotFound(new { success = false, message = "Item not found in cart." });

                // Remove from both collections
                cart.Items.Remove(item);
                context.CartItems.Remove(item);
                await context.SaveChangesAsync();

                return Ok(new { success = true, message = "Item removed successfully." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        public class RemoveCartRequest
        {
            public string ClientId { get; set; }
            public int BlueprintId { get; set; }
        }


        // -------------------- PAYMENT --------------------
        [HttpPost("CreateCheckoutSession")]
        public IActionResult CreateCheckoutSession([FromBody] List<CartItemViewModel> cart)
        {
            try
            {
                if (cart == null || !cart.Any())
                    return BadRequest(new { success = false, message = "Cart is empty." });

                var lineItems = cart.Select(item => new SessionLineItemOptions
                {
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        UnitAmount = (long)(item.Price * 100),
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
                    SuccessUrl = "blueprintpro://payment-success",
                    CancelUrl = "blueprintpro://payment-cancel"
                };

                StripeConfiguration.ApiKey = _stripeSettings.SecretKey;
                var service = new SessionService();
                var session = service.Create(options);

                return Ok(new
                {
                    sessionId = session.Id,
                    paymentUrl = session.Url,
                    totalAmount = cart.Sum(i => i.Price * i.Quantity),
                    currency = "PHP"
                });

            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        [AllowAnonymous]
        [HttpPost("CompletePurchase")]
        public async Task<IActionResult> CompletePurchase([FromBody] CompletePurchaseRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.ClientId))
                    return BadRequest(new { success = false, message = "ClientId is required." });

                if (request.BlueprintIds == null || !request.BlueprintIds.Any())
                    return BadRequest(new { success = false, message = "No blueprints selected." });

                var purchasedBlueprints = await context.Blueprints
                    .Where(bp => request.BlueprintIds.Contains(bp.blueprintId))
                    .ToListAsync();

                foreach (var bp in purchasedBlueprints)
                {
                    bp.blueprintIsForSale = false;
                    bp.clentId = request.ClientId;
                }

                var cart = await context.Carts
                    .Include(c => c.Items)
                    .FirstOrDefaultAsync(c => c.UserId == request.ClientId);

                if (cart != null && cart.Items.Any())
                    cart.Items.Clear();

                await context.SaveChangesAsync();

                return Ok(new { success = true, message = "Purchase completed successfully." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }
    

    public class CompletePurchaseRequest
    {
        public string ClientId { get; set; } = "";
        public List<int> BlueprintIds { get; set; } = new();
    }



        // -------------------- PROJECTS --------------------
        [HttpGet("Projects")]
        [Authorize]
        public async Task<IActionResult> GetProjects()
        {
            try
            {
                var user = await userManager.GetUserAsync(User);
                if (user == null) return Unauthorized(new { success = false, message = "User not authorized." });

                var projects = await context.Projects
                    .Where(p => p.user_clientId == user.Id)
                    .Include(p => p.Blueprint)
                    .Include(p => p.Architect)
                    .Select(p => new
                    {
                        p.project_Id,
                        BlueprintName = p.Blueprint.blueprintName,
                        ArchitectName = p.Architect.user_fname + " " + p.Architect.user_lname,
                        p.project_Status
                    })
                    .ToListAsync();

                return Ok(projects);
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        // -------------------- MATCHING --------------------
        
        [HttpGet("Matches")]
        [AllowAnonymous] // or require auth if you prefer
        public async Task<IActionResult> GetMatches([FromQuery] string clientId, [FromQuery] string? query)
        {
            try
            {
                string searchQuery = string.IsNullOrWhiteSpace(query) ? "General search" : query;

                // Expand query with GPT
                var chatClient = _openAi.GetChatClient("gpt-5-mini");
                var messages = new List<ChatMessage>
                {
                    new SystemChatMessage("You are an assistant that rewrites client needs into a natural request for an architect."),
                    new UserChatMessage($"User request: {searchQuery}. Expand into 2–3 descriptive sentences.")
                };
                var gptResponse = await chatClient.CompleteChatAsync(messages);
                var expansion = gptResponse.Value.Content[0].Text;
                var finalQuery = $"{searchQuery}. Expanded: {expansion}";

                // Generate embeddings
                var embeddingResponse = await _embeddingClient.GenerateEmbeddingAsync(finalQuery);
                var queryVector = embeddingResponse.Value.ToFloats().ToArray();

                // Filter to architects with embeddings
                var architects = await context.Users
                    .Where(u => u.user_role == "Architect" && !string.IsNullOrEmpty(u.PortfolioEmbedding))
                    .ToListAsync();

                // Rank by cosine similarity
                var ranked = architects
                    .Select(a =>
                    {
                        var vecA = ParseEmbedding(a.PortfolioEmbedding);
                        if (vecA == null || vecA.Length != queryVector.Length)
                            return (Architect: a, Score: double.MinValue);

                        var score = CosineSimilarity(queryVector, vecA);
                        if (a.IsPro) score += 0.05;
                        return (Architect: a, Score: score);
                    })
                    .Where(x => x.Score != double.MinValue)
                    .OrderByDescending(x => x.Score)
                    .Take(10)
                    .ToList();

                // Map to DTO
                var matchDtos = ranked.Select(x => new MatchDto
                {
                    MatchId = Guid.NewGuid().ToString(),
                    ArchitectId = x.Architect.Id,
                    ArchitectName = $"{x.Architect.user_fname} {x.Architect.user_lname}",
                    ArchitectStyle = x.Architect.user_Style,
                    ArchitectLocation = x.Architect.user_Location,
                    ArchitectBudget = x.Architect.user_Budget,
                    MatchStatus = x.Architect.IsPro ? "AI + Portfolio Match (Pro)" : "AI + Portfolio Match",
                    RealMatchStatus = string.IsNullOrEmpty(clientId)
                        ? null
                        : context.Matches
                            .Where(m => m.ClientId == clientId && m.ArchitectId == x.Architect.Id)
                            .Select(m => m.MatchStatus)
                            .FirstOrDefault(),
                    MatchDate = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss"),
                    SimilarityScore = x.Score,
                    SimilarityPercentage = Math.Round(x.Score * 100, 1)
                }).ToList();

                return Ok(matchDtos);
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }


        // -------------------- SEND MATCH REQUEST --------------------
        public class MatchRequest
        {
            public string ArchitectId { get; set; }
            public string ClientId { get; set; }
        }

        [AllowAnonymous]
        [HttpPost("RequestMatch")]
        public async Task<IActionResult> RequestMatch([FromBody] MatchRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.ClientId))
                return BadRequest(new { success = false, message = "ClientId is required." });

            if (string.IsNullOrWhiteSpace(request.ArchitectId))
                return BadRequest(new { success = false, message = "ArchitectId is required." });

            var existing = await context.Matches
                .FirstOrDefaultAsync(m => m.ClientId == request.ClientId && m.ArchitectId == request.ArchitectId);

            if (existing != null)
                return BadRequest(new { success = false, message = "Match request already sent." });

            var match = new Match
            {
                ClientId = request.ClientId, 
                ArchitectId = request.ArchitectId
            };

            context.Matches.Add(match);
            await context.SaveChangesAsync();

            // 🔹 Create notification for the architect (MISSING IN YOUR VERSION)
            var architect = await context.Users.FindAsync(request.ArchitectId);
            var client = await context.Users.FindAsync(request.ClientId);

            if (architect != null && client != null)
            {
                var notif = new Notification
                {
                    user_Id = architect.Id,
                    notification_Title = "New Match Request",
                    notification_Message = $"{client.user_fname} {client.user_lname} wants to match with you.",
                    notification_Date = DateTime.Now,
                    notification_isRead = false
                };

                context.Notifications.Add(notif);
                await context.SaveChangesAsync();

                // 🔹 Real-time update (SignalR)
                await _hubContext.Clients
                    .User(architect.Id)
                    .SendAsync("ReceiveNotification", new
                    {
                        title = notif.notification_Title,
                        message = notif.notification_Message,
                        date = notif.notification_Date.ToString("g")
                    });
            }

            return Ok(new { success = true, message = "Match request sent successfully." });
        }



        // -------------------- MESSAGING --------------------
        [HttpGet("Messages/All")]
        [AllowAnonymous]
        public async Task<IActionResult> GetAllMessages([FromQuery] string clientId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(clientId))
                    return BadRequest(new { success = false, message = "ClientId is required." });

                var baseUrl = $"{Request.Scheme}://{Request.Host}";

                // Step 1: Fetch messages grouped by ArchitectId
                var conversationsRaw = await context.Messages
                    .Where(m => m.ClientId == clientId || m.ArchitectId == clientId)
                    .GroupBy(m => m.ArchitectId)
                    .Select(g => new
                    {
                        ArchitectId = g.Key,
                        ArchitectName = context.Users
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
                        UnreadCount = g.Count(x => x.ArchitectId == g.Key && !x.IsRead)
                    })
                    .ToListAsync();

                // Step 2: Convert profile URLs and Philippine Time
                var phTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Singapore Standard Time");
                var conversations = conversationsRaw
                    .Select(c => new
                    {
                        c.ArchitectId,
                        c.ArchitectName,
                        c.LastMessage,
                        LastMessageTime = TimeZoneInfo.ConvertTimeFromUtc(c.LastMessageTimeUtc, phTimeZone),
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


        [HttpGet("Messages")]
        [AllowAnonymous] // or [Authorize] if you add token auth later
        public async Task<IActionResult> GetMessages([FromQuery] string clientId, [FromQuery] string architectId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(architectId))
                    return BadRequest(new { success = false, message = "ClientId and ArchitectId are required." });

                // Step 1: Get raw UTC messages from database (EF-safe)
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

                // Step 2: Convert to Philippine Time after fetching
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

                // Step 3: Mark unread messages as read for this client
                var unreadMessages = await context.Messages
                    .Where(m => m.ArchitectId == clientId && m.ClientId == architectId && !m.IsRead)
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


        [HttpGet("AllMatches")]
        [AllowAnonymous]
        public async Task<IActionResult> GetAllMatches([FromQuery] string clientId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(clientId))
                    return BadRequest(new { success = false, message = "ClientId is required." });
                var baseUrl = $"{Request.Scheme}://{Request.Host}";
                var matches = await context.Matches
                    .Where(m => m.ClientId == clientId)
                    .Include(m => m.Architect)
                    .Select(m => new
                    {
                        MatchId = m.MatchId,
                        ArchitectId = m.Architect.Id,
                        ArchitectName = m.Architect.user_fname + " " + m.Architect.user_lname,
                        ArchitectLocation = m.Architect.user_Location,
                        ArchitectStyle = m.Architect.user_Style,
                        ArchitectBudget = m.Architect.user_Budget,
                        ArchitectPhoto = string.IsNullOrEmpty(m.Architect.user_profilePhoto)
                            ? null
                            : $"{baseUrl}/images/profiles/{Path.GetFileName(m.Architect.user_profilePhoto)}",
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


        // ✅ SEND MESSAGE (Client → Architect)
        [HttpPost("SendMessage")]
        [AllowAnonymous]
        public async Task<IActionResult> SendMessage([FromBody] SendMessageRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.ClientId) ||
                    string.IsNullOrWhiteSpace(request.ArchitectId) ||
                    string.IsNullOrWhiteSpace(request.MessageBody))
                {
                    return BadRequest(new { success = false, message = "ClientId, ArchitectId, and MessageBody are required." });
                }

                // 🔍 Fetch client user (for name + profile photo like Web)
                var clientUser = await userManager.FindByIdAsync(request.ClientId);
                if (clientUser == null)
                    return BadRequest(new { success = false, message = "Client not found." });

                // 🌏 PH timezone
                var phTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Singapore Standard Time");
                var phTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, phTimeZone);

                var message = new Message
                {
                    MessageId = Guid.NewGuid(),
                    ClientId = request.ClientId,
                    ArchitectId = request.ArchitectId,
                    SenderId = request.SenderId ?? request.ClientId, // default to client
                    MessageBody = request.MessageBody,
                    MessageDate = DateTime.UtcNow,
                    IsRead = false
                };

                context.Messages.Add(message);
                await context.SaveChangesAsync();

                // 🧩 Profile photo logic (same as web)
                string senderPhoto =
                    string.IsNullOrEmpty(clientUser.user_profilePhoto)
                        ? "/images/profile.jpg"
                        : clientUser.user_profilePhoto
                            .Replace("~", "")
                            .Replace("wwwroot", "");

                // 📡 SignalR (MATCHES WEB VERSION)
                await _hubContext.Clients.User(request.ArchitectId).SendAsync("ReceiveMessage", new
                {
                    senderId = clientUser.Id,
                    senderName = clientUser.user_fname + " " + clientUser.user_lname,
                    messageBody = request.MessageBody,
                    messageDate = phTime.ToString("g"),
                    senderProfilePhoto = senderPhoto
                });

                // 🔔 Same update ping as web version
                await _hubContext.Clients.User(request.ArchitectId)
                    .SendAsync("ReceiveMessageUpdate");

                return Ok(new { success = true, message = "Message sent successfully." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        [HttpPost("SubmitRating")]
        public IActionResult SubmitRating([FromForm] string projectId, [FromForm] int rating)
        {
            if (string.IsNullOrEmpty(projectId))
                return BadRequest(new { success = false, message = "Project ID is required." });

            // Find project
            var project = context.Projects.FirstOrDefault(p => p.project_Id == projectId);
            if (project == null)
                return NotFound(new { success = false, message = "Project not found." });

            // Find architect
            var architect = context.Users.FirstOrDefault(u => u.Id == project.user_architectId);
            if (architect == null)
                return NotFound(new { success = false, message = "Architect not found." });

            // Add rating
            architect.user_Rating = (architect.user_Rating ?? 0) + rating;

            // Mark project as rated
            project.project_clientHasRated = true;

            // Save database
            context.SaveChanges();

            return Ok(new
            {
                success = true,
                message = "Rating submitted successfully."
            });
        }

        [HttpGet("getClientProjects/{clientId}")]
        public async Task<IActionResult> GetClientProjects(string clientId)
        {
            var projects = await context.Projects
                .Where(p => p.user_clientId == clientId &&
                           (p.project_Status == "Ongoing" || p.project_Status == "Finished"))
                .Include(p => p.Blueprint)
                .Include(p => p.Architect)
                .Select(p => new
                {
                    p.project_Id,
                    p.project_Title,
                    p.project_Status,
                    p.project_Budget,
                    p.project_startDate,
                    p.project_endDate,
                    blueprint_Id = p.blueprint_Id,
                    blueprint_Name = p.Blueprint.blueprintName,
                    blueprint_ImageUrl =
                        p.Blueprint.blueprintImage != null
                            ? (
                                p.Blueprint.blueprintImage.StartsWith("/uploads")
                                    ? $"{Request.Scheme}://{Request.Host}{p.Blueprint.blueprintImage}"
                                    : $"{Request.Scheme}://{Request.Host}/images/{p.Blueprint.blueprintImage}"
                              )
                            : null,

                    architectName = p.Architect.user_fname + " " + p.Architect.user_lname,
                    user_architectId = p.user_architectId
                })
                .ToListAsync();

            return Ok(projects);
        }

        // ✅ Request model for sending messages
        public class SendMessageRequest
    {
        public string ClientId { get; set; }
        public string ArchitectId { get; set; }
        public string? SenderId { get; set; } // optional
        public string MessageBody { get; set; }
}

        // -------------------- PROJECT TRACKER --------------------
        [HttpGet("ProjectTracker/{blueprintId}")]
        public async Task<IActionResult> GetProjectTracker(int blueprintId)
        {
            var tracker = await context.ProjectTrackers
                .Include(pt => pt.Project)
                    .ThenInclude(p => p.Architect)
                .Include(pt => pt.Compliance)
                .FirstOrDefaultAsync(pt => pt.Project.blueprint_Id == blueprintId);

            if (tracker == null)
                return NotFound();

            var projectFiles = await context.ProjectFiles
                .Where(f => f.project_Id == tracker.project_Id)
                .OrderByDescending(f => f.projectFile_Version)
                .ToListAsync();

            var response = new
            {
                projectTrack_Id = tracker.projectTrack_Id,
                project_Id = tracker.project_Id,

                // ✔ MATCH ANDROID EXACT FIELD NAMES
                CurrentFileName = tracker.projectTrack_currentFileName,
                CurrentFilePath = tracker.projectTrack_currentFilePath,
                CurrentRevision = tracker.projectTrack_currentRevision,
                Status = tracker.projectTrack_Status,

                ArchitectName = tracker.Project?.Architect == null
                    ? null
                    : $"{tracker.Project.Architect.user_fname} {tracker.Project.Architect.user_lname}",

                IsRated = tracker.Project?.project_clientHasRated ?? false,

                RevisionHistory = projectFiles.Select(f => new
                {
                    FileName = f.projectFile_fileName,
                    Version = f.projectFile_Version,
                    UploadedDate = f.projectFile_uploadedDate,
                    FilePath = f.projectFile_Path
                }).ToList(),

                // ✔ SAFE DTO, NO EF ENTITIES
                Compliance = tracker.Compliance == null ? null : new
                {
                    compliance_Id = tracker.Compliance.compliance_Id,
                    compliance_Zoning = tracker.Compliance.compliance_Zoning,
                    compliance_Others = tracker.Compliance.compliance_Others
                },

                FinalizationNotes = tracker.projectTrack_FinalizationNotes,
                ProjectStatus = tracker.Project.project_Status
            };

            return Ok(response);
        }


        // -------------------- HELPERS --------------------
        private double CosineSimilarity(float[] v1, float[] v2)
        {
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
            if (string.IsNullOrWhiteSpace(embeddingString)) return Array.Empty<float>();
            return embeddingString.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => float.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f)
                .ToArray();
        }

        [HttpGet("GetClientId")]
        [Authorize]
        public async Task<IActionResult> GetClientId()
        {
            var user = await userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            return Ok(new { clientId = user.Id });
        }

        [HttpGet("getArchitectProfile/{id}")]
        public async Task<IActionResult> GetArchitectProfile(string id)
        {
            var architect = await userManager.FindByIdAsync(id);
            if (architect == null)
                return NotFound(new { success = false, message = "Architect not found" });

            var credentialsPath = string.IsNullOrEmpty(architect.user_CredentialsFile)
                ? null
                : Url.Content($"~/credentials/{architect.user_CredentialsFile}");

            return Ok(new
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

        public class MatchDto
        {
            [JsonPropertyName("MatchId")]
            public string? MatchId { get; set; }

            [JsonPropertyName("ClientId")]
            public string? ClientId { get; set; }

            [JsonPropertyName("ClientName")]
            public string? ClientName { get; set; }

            [JsonPropertyName("ArchitectId")]
            public string ArchitectId { get; set; } = string.Empty;

            [JsonPropertyName("ArchitectName")]
            public string ArchitectName { get; set; } = string.Empty;

            [JsonPropertyName("ArchitectStyle")]
            public string? ArchitectStyle { get; set; }

            [JsonPropertyName("ArchitectLocation")]
            public string? ArchitectLocation { get; set; }

            [JsonPropertyName("ArchitectBudget")]
            public string? ArchitectBudget { get; set; }

            [JsonPropertyName("MatchStatus")]
            public string MatchStatus { get; set; } = "AI Match";

            [JsonPropertyName("RealMatchStatus")]
            public string RealMatchStatus { get; set; }

            [JsonPropertyName("MatchDate")]
            public string? MatchDate { get; set; }
            [JsonPropertyName("SimilarityScore")]
            public double SimilarityScore { get; set; }
            [JsonPropertyName("SimilarityPercentage")]
            public double SimilarityPercentage { get; set; }
        }
    }
}

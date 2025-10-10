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
                var availableBlueprints = context.Blueprints
                    .Where(bp => bp.blueprintIsForSale)
                    .Select(bp => new
                    {
                        bp.blueprintId,
                        bp.blueprintName,
                        bp.blueprintImage,
                        bp.blueprintPrice,
                        bp.blueprintIsForSale
                    })
                    .ToList();

                return Ok(availableBlueprints);
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
        }

        [HttpPost("AddToCart")]
        [Authorize]
        public async Task<IActionResult> AddToCart([FromBody] CartRequest model)
        {
            try
            {
                var user = await userManager.GetUserAsync(User);
                if (user == null) return Unauthorized(new { success = false, message = "User not authorized." });

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
                return Ok(new { success = true, message = "Item added to cart." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        [HttpGet("GetCart")]
        [Authorize]
        public async Task<IActionResult> GetCart()
        {
            try
            {
                var user = await userManager.GetUserAsync(User);
                if (user == null) return Unauthorized(new { success = false, message = "User not authorized." });

                var cart = context.Carts
                    .Where(c => c.UserId == user.Id)
                    .Select(c => new
                    {
                        c.CartId,
                        Items = c.Items.Select(i => new
                        {
                            i.CartItemId,
                            i.BlueprintId,
                            i.Blueprint.blueprintName,
                            i.Blueprint.blueprintImage,
                            i.Blueprint.blueprintPrice,
                            i.Quantity
                        })
                    })
                    .FirstOrDefault();

                return Ok(cart != null
                     ? cart
                     : new { CartId = 0, Items = new List<object>() });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
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

                return Ok(new { id = session.Id });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        [HttpPost("CompletePurchase")]
        [Authorize]
        public async Task<IActionResult> CompletePurchase([FromBody] List<int> blueprintIds)
        {
            try
            {
                var user = await userManager.GetUserAsync(User);
                if (user == null) return Unauthorized(new { success = false, message = "User not authorized." });

                if (blueprintIds == null || !blueprintIds.Any())
                    return BadRequest(new { success = false, message = "No blueprints selected." });

                var purchasedBlueprints = await context.Blueprints
                    .Where(bp => blueprintIds.Contains(bp.blueprintId))
                    .ToListAsync();

                foreach (var bp in purchasedBlueprints)
                {
                    bp.blueprintIsForSale = false;
                    bp.clentId = user.Id;
                }

                var cart = await context.Carts
                    .Include(c => c.Items)
                    .FirstOrDefaultAsync(c => c.UserId == user.Id);

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
        // NOTE: allow anonymous so mobile can call without cookie-based login.
        // If you prefer authorization, change [AllowAnonymous] to [Authorize] and
        // implement token/JWT authentication in mobile client.
        [HttpGet("Matches")]
        [AllowAnonymous]
        public async Task<IActionResult> GetMatches([FromQuery] string? query)
        {
            try
            {
                // If you prefer to require a user, uncomment this block and return Unauthorized if null.
                // var currentUser = await userManager.GetUserAsync(User);
                // if (currentUser == null) return Unauthorized(new { success = false, message = "User not authorized." });

                // For anonymous use: build a generic searchQuery from query or use a safe default
                string searchQuery = string.IsNullOrWhiteSpace(query)
                    ? "General search"
                    : query;

                // Expand query using GPT (fail-safe: don't block if OpenAI fails)
                string expansion = "";
                try
                {
                    var chatClient = _openAi.GetChatClient("gpt-5-mini");
                    var messages = new List<ChatMessage>
                    {
                        new SystemChatMessage("You are an assistant that rewrites client needs into a natural request for an architect."),
                        new UserChatMessage($"User request: {searchQuery}. Expand into 2–3 descriptive sentences.")
                    };
                    var response = await chatClient.CompleteChatAsync(messages);
                    expansion = response.Value.Content[0].Text;
                }
                catch
                {
                    // ignore embedding expansion failure — fallback to searchQuery
                    expansion = "";
                }

                // Generate embeddings (wrap in try/catch; if it fails, fallback to simple matching)
                float[] queryVector = Array.Empty<float>();
                try
                {
                    var embeddingResponse = await _embeddingClient.GenerateEmbeddingAsync($"{searchQuery}. Expanded: {expansion}");
                    queryVector = embeddingResponse.Value.ToFloats().ToArray();
                }
                catch
                {
                    queryVector = Array.Empty<float>();
                }

                // Get architects (ensure PortfolioEmbedding is non-null)
                var architects = await context.Users
                    .Where(u => u.user_role == "Architect")
                    .ToListAsync();

                // If embeddings exist, do similarity ranking, else return newest/available architects
                var ranked = new List<(User Architect, double Score)>();
                if (queryVector.Length > 0)
                {
                    ranked = architects
                        .Where(a => !string.IsNullOrEmpty(a.PortfolioEmbedding))
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
                }
                else
                {
                    // fallback: take latest 10 architects
                    ranked = architects
                        .OrderByDescending(a => a.Id) // or other ordering
                        .Take(10)
                        .Select(a => (Architect: a, Score: 0.0))
                        .ToList();
                }

                var matchDtos = ranked.Select(x => new MatchDto
                {
                    MatchId = Guid.NewGuid().ToString(),
                    ClientId = null,
                    ClientName = null,
                    ArchitectId = x.Architect.Id,
                    ArchitectName = $"{x.Architect.user_fname} {x.Architect.user_lname}",
                    ArchitectStyle = x.Architect.user_Style,
                    ArchitectLocation = x.Architect.user_Location,
                    ArchitectBudget = x.Architect.user_Budget,
                    MatchStatus = "AI Match",
                    MatchDate = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss")
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
            public string ArchitectId { get; set; } = string.Empty;
        }

        [HttpPost("RequestMatch")]
        [AllowAnonymous] // You can change to [Authorize] if you set up token auth for mobile
        public async Task<IActionResult> RequestMatch([FromBody] MatchRequest request)
        {
            try
            {
                // If you require authentication, get currentUser via userManager.GetUserAsync(User)
                // and return Unauthorized if null. For now we allow anonymous so that mobile can test.
                // If you allow anonymous, you must validate request.ArchitectId, etc.

                if (string.IsNullOrWhiteSpace(request?.ArchitectId))
                    return BadRequest(new { success = false, message = "ArchitectId is required." });

                // If using anonymous flow: try to resolve a test client or return an informative message.
                // Here we simply create a Match with a placeholder ClientId if no authenticated user exists.
                var currentUser = await userManager.GetUserAsync(User);
                var clientId = currentUser?.Id ?? "anonymous-client";

                var existing = await context.Matches
                    .FirstOrDefaultAsync(m => m.ClientId == clientId && m.ArchitectId == request.ArchitectId);

                if (existing != null)
                    return BadRequest(new { success = false, message = "Match request already sent." });

                var match = new Match
                {
                    MatchId = Guid.NewGuid().ToString(),
                    ClientId = clientId,
                    ArchitectId = request.ArchitectId,
                    MatchStatus = "Pending",
                    MatchDate = DateTime.UtcNow
                };

                context.Matches.Add(match);
                await context.SaveChangesAsync();

                return Ok(new { success = true, message = "Match request sent successfully." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        // -------------------- MESSAGING --------------------
        [HttpGet("Messages/{architectId}")]
        [AllowAnonymous] // change to [Authorize] if you want to restrict
        public async Task<IActionResult> GetMessages(string architectId)
        {
            try
            {
                var currentUser = await userManager.GetUserAsync(User);
                // if currentUser == null and you require auth, return Unauthorized(...)
                var clientId = currentUser?.Id ?? architectId; // fallback behavior

                var messages = await context.Messages
                    .Where(m =>
                        (m.ClientId == clientId && m.ArchitectId == architectId) ||
                        (m.ClientId == architectId && m.ArchitectId == clientId))
                    .OrderBy(m => m.MessageDate)
                    .Select(m => new
                    {
                        m.MessageId,
                        m.MessageBody,
                        m.MessageDate,
                        m.SenderId,
                        m.IsRead
                    })
                    .ToListAsync();

                return Ok(messages);
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        [HttpPost("SendMessage")]
        [AllowAnonymous]
        public async Task<IActionResult> SendMessage([FromBody] MessageViewModel model)
        {
            try
            {
                var currentUser = await userManager.GetUserAsync(User);
                var clientId = currentUser?.Id;

                if (string.IsNullOrWhiteSpace(model?.MessageBody))
                    return BadRequest(new { success = false, message = "Message cannot be empty." });

                var message = new Message
                {
                    MessageId = Guid.NewGuid(),
                    ClientId = clientId ?? model.SenderId,
                    ArchitectId = model.ArchitectId,
                    SenderId = model.SenderId ?? clientId,
                    MessageBody = model.MessageBody,
                    MessageDate = DateTime.UtcNow,
                    IsRead = false
                };
                context.Messages.Add(message);
                await context.SaveChangesAsync();

                if (!string.IsNullOrEmpty(model.ArchitectId))
                {
                    await _hubContext.Clients.User(model.ArchitectId).SendAsync("ReceiveMessage", new
                    {
                        SenderId = message.SenderId,
                        SenderName = $"{message.SenderId}",
                        message.MessageBody,
                        MessageDate = DateTime.UtcNow.ToString("g")
                    });
                }

                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        // -------------------- PROJECT TRACKER --------------------
        [HttpGet("ProjectTracker/{id}")]
        [AllowAnonymous]
        public IActionResult GetProjectTracker(int id)
        {
            try
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
                    .Select(f => new
                    {
                        f.projectFile_fileName,
                        f.projectFile_Version,
                        f.projectFile_uploadedDate
                    })
                    .ToList();

                return Ok(new
                {
                    tracker.projectTrack_Id,
                    tracker.project_Id,
                    tracker.projectTrack_currentFileName,
                    tracker.projectTrack_currentFilePath,
                    tracker.projectTrack_currentRevision,
                    tracker.projectTrack_Status,
                    tracker.projectTrack_FinalizationNotes,
                    Compliance = tracker.Compliance,
                    RevisionHistory = history
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
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

            [JsonPropertyName("MatchDate")]
            public string? MatchDate { get; set; }
        }
    }
}

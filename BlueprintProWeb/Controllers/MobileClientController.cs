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
using static BlueprintProWeb.Controllers.ClientSide.ClientInterfaceController;

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

                        blueprintImage = string.IsNullOrEmpty(bp.blueprintImage)
                            ? null
                            : $"{baseUrl}/images/{Path.GetFileName(bp.blueprintImage.Replace('\\', '/').TrimStart('~').TrimStart('/'))}"
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

                if (!purchasedBlueprints.Any())
                    return NotFound(new { success = false, message = "No matching blueprints found." });

                foreach (var bp in purchasedBlueprints)
                {
                    // Mark blueprint as sold
                    bp.blueprintIsForSale = false;

                    // Assign buyer
                    bp.clentId = request.ClientId;

                    // AUTOMATIC MATCHING
                    if (!string.IsNullOrEmpty(bp.architectId))
                    {
                        var match = new Match
                        {
                            ClientId = request.ClientId,
                            ArchitectId = bp.architectId,
                            MatchStatus = "Approved",
                            MatchDate = DateTime.UtcNow
                        };

                        context.Matches.Add(match);
                    }
                }

                // Clear user cart
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

        [AllowAnonymous]
        [HttpGet("purchased-blueprints/{userId}")]
        public async Task<IActionResult> GetPurchasedBlueprints(string userId)
        {
            if (string.IsNullOrEmpty(userId))
                return BadRequest("UserId is required.");

            var purchased = await context.Blueprints
                .Where(bp =>
                    bp.clentId == userId &&
                    bp.blueprintIsForSale == false
                )
                .Join(
                    context.Users,
                    bp => bp.architectId,
                    u => u.Id,
                    (bp, architect) => new
                    {
                        blueprintId = bp.blueprintId,
                        blueprintName = bp.blueprintName,
                        architectName = architect.user_fname + " " + architect.user_lname
                    }
                )
                .ToListAsync();

            return Ok(purchased);
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
        [AllowAnonymous]
        public async Task<IActionResult> GetMatches(
            [FromQuery] string? clientId,
            [FromQuery] string? query,
            [FromQuery] string? clarifications)
        {
            try
            {
                // 1. No query → return empty early
                if (string.IsNullOrWhiteSpace(query))
                {
                    return Ok(new
                    {
                        matches = new List<object>(),
                        totalArchitects = 0,
                        outOfScope = false,
                        showFeedback = true,
                        needsClarification = false
                    });
                }

                var chatClient = _openAi.GetChatClient("gpt-4o-mini");

                // 2. Scope check
                var scopeMessages = new List<ChatMessage>
                {
                    new SystemChatMessage(
                        @"You are classifying client messages for an architecture matching system.
                        Classify the message into ONE of the following categories:
                        - ARCHITECTURE_RELATED
                        - NOT_ARCHITECTURE_RELATED
                        Rules:
                        - Vague architectural requests are still ARCHITECTURE_RELATED
                        - Missing details does NOT make it out of scope
                        Respond ONLY with the category."),
                    new UserChatMessage(query)
                };

                var scopeResult = await chatClient.CompleteChatAsync(scopeMessages);
                bool outOfScope = !scopeResult.Value.Content[0].Text
                    .Trim()
                    .Equals("ARCHITECTURE_RELATED", StringComparison.OrdinalIgnoreCase);

                // 3. Clarification check — only if in scope and no clarifications already provided
                if (!outOfScope && string.IsNullOrWhiteSpace(clarifications))
                {
                    var clarityMessages = new List<ChatMessage>
                    {
                        new SystemChatMessage(
                            @"You evaluate whether an architecture client's query needs clarifying questions before architect matching.
                            First, identify every architectural detail already mentioned in the query — style, building type, features, intended use, scale, structural elements, aesthetics, or construction intent.
                            A query is SUFFICIENT if it provides enough context to identify a clear architectural direction. It does not need to cover every dimension — if the client has already stated 2 or more specific architectural details, lean toward SUFFICIENT.
                            A query is NEEDS_CLARIFICATION only if it is genuinely vague or missing critical architectural context that would meaningfully affect matching.
                            Respond ONLY with: SUFFICIENT or NEEDS_CLARIFICATION"),
                        new UserChatMessage(query)
                    };

                    var clarityResult = await chatClient.CompleteChatAsync(clarityMessages);
                    bool needsClarification = clarityResult.Value.Content[0].Text
                        .Trim()
                        .Equals("NEEDS_CLARIFICATION", StringComparison.OrdinalIgnoreCase);

                    if (needsClarification)
                    {
                        var questionMessages = new List<ChatMessage>
                        {
                            new SystemChatMessage(
                                @"You generate architecture-specific clarifying questions to help match clients with architects.

                                    Step 1 — Read the client's query carefully and determine which of these FIVE architectural dimensions are already stated (explicitly or implicitly):
                                      1. Design style (e.g. modern, minimalist, brutalist, tropical, classical, industrial)
                                      2. Building type (e.g. house, office, school, apartment, clinic, retail, warehouse)
                                      3. Spatial or structural features (e.g. open plan, high ceilings, passive ventilation, mezzanine, courtyard)
                                      4. Intended use or purpose (e.g. family home, co-working space, community center, worship space)
                                      5. Scale or size (e.g. small, large, compact, multi-storey, single-storey, 200 sqm)

                                    Step 2 — Identify only the dimensions that are completely absent from the query. A dimension is present even if mentioned briefly or loosely — do not ask about it.

                                    Step 3 — Generate questions using these rules:
                                      - If 1 dimension is missing: generate exactly 1 question
                                      - If 2 dimensions are missing: generate exactly 2 questions
                                      - If 3 or more dimensions are missing: generate exactly 3 questions (maximum)
                                      - If 0 dimensions are missing or the query is detailed enough: respond with exactly the word SUFFICIENT and nothing else

                                    Each question must:
                                      - Be about one of the five dimensions listed above that is absent from the query
                                      - Feel like a natural, specific follow-up to the client's exact words — not a generic checklist item
                                      - Have exactly 3 to 4 short option labels drawn from real architectural concepts relevant to what the client described

                                    NEVER ask about: budget, cost, price, timeline, schedule, deadlines, location, city, country, or anything non-architectural.
                                    NEVER generate a question for a dimension already present in the query.

                                    Respond ONLY with valid JSON (no markdown, no extra text) or the single word SUFFICIENT. JSON format:
                                    [{""question"":""..."",""options"":[""..."",""..."",""...""]},...]"),
                            new UserChatMessage(query)
                        };

                        var questionResult = await chatClient.CompleteChatAsync(questionMessages);
                        string rawJson = questionResult.Value.Content[0].Text.Trim();

                        int jsonStart = rawJson.IndexOf('[');
                        int jsonEnd = rawJson.LastIndexOf(']');
                        if (jsonStart >= 0 && jsonEnd > jsonStart)
                            rawJson = rawJson.Substring(jsonStart, jsonEnd - jsonStart + 1);

                        List<MobileClarifyingQuestion>? questions = null;
                        try
                        {
                            questions = System.Text.Json.JsonSerializer.Deserialize<List<MobileClarifyingQuestion>>(
                                rawJson,
                                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        }
                        catch { /* fall through to matching on parse failure */ }

                        if (questions != null && questions.Count > 0)
                        {
                            return Ok(new
                            {
                                needsClarification = true,
                                originalQuery = query,
                                questions
                            });
                        }
                    }
                }

                // 4. Compose final query — append clarifications if provided
                string finalQuery = string.IsNullOrWhiteSpace(clarifications)
                    ? query
                    : $"{query}. {clarifications}";

                // 5. Generate embedding
                var embeddingResponse = await _embeddingClient.GenerateEmbeddingAsync(finalQuery);
                var queryVector = embeddingResponse.Value.ToFloats().ToArray();

                // 6. Fetch architects
                var architects = await context.Users
                    .Where(u => u.user_role == "Architect" && !string.IsNullOrEmpty(u.PortfolioEmbedding))
                    .AsNoTracking()
                    .ToListAsync();

                int totalArchitects = architects.Count;

                // 7. Score on cosine similarity
                var ranked = architects
                    .Select(a =>
                    {
                        var vecA = ParseEmbedding(a.PortfolioEmbedding);
                        if (vecA == null || vecA.Length != queryVector.Length) return null;

                        double score = CosineSimilarity(queryVector, vecA);
                        double percentage = Math.Round(score * 100, 1);

                        if (percentage < 35) return null;

                        return new MatchDto
                        {
                            MatchId = null,
                            ClientId = clientId,
                            ArchitectId = a.Id,
                            ArchitectName = $"{a.user_fname} {a.user_lname}",
                            ArchitectStyle = a.user_Style,
                            ArchitectLocation = a.user_Location,
                            ArchitectBudget = a.user_Budget,
                            MatchStatus = "AI + Portfolio Match",
                            RealMatchStatus = string.IsNullOrEmpty(clientId)
                                ? null
                                : context.Matches
                                    .Where(m => m.ClientId == clientId && m.ArchitectId == a.Id)
                                    .Select(m => m.MatchStatus)
                                    .FirstOrDefault(),
                            MatchDate = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss"),
                            SimilarityScore = score,
                            SimilarityPercentage = percentage,
                            MatchExplanation = null
                        };
                    })
                    .Where(x => x != null)
                    .OrderByDescending(x => x!.SimilarityScore)
                    .ToList();

                return Ok(new
                {
                    matches = ranked,
                    totalArchitects = totalArchitects,
                    outOfScope,
                    showFeedback = ranked.Count == 0,
                    needsClarification = false
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        [HttpPost("ExplainMatch")]
        public async Task<IActionResult> ExplainMatch([FromBody] ExplainMatchRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.ArchitectId) || string.IsNullOrWhiteSpace(request.Query))
                return BadRequest(new { explanation = "Missing required fields." });

            var architect = await context.Users.FindAsync(request.ArchitectId);
            if (architect == null)
                return NotFound(new { explanation = "Architect not found." });

            if (string.IsNullOrWhiteSpace(architect.PortfolioText))
                return Ok(new { explanation = "This architect hasn't added credential details yet." });

            var explanation = await GenerateMatchExplanation(request.Query, architect);
            return Ok(new { explanation });
        }

        // ExplainMatchRequest — LacksDetails removed
        public class ExplainMatchRequest
        {
            public string ArchitectId { get; set; }
            public string Query { get; set; }
        }

        private async Task<string> GenerateMatchExplanation(string clientQuery, User architect)
        {
            var chatClient = _openAi.GetChatClient("gpt-4o-mini"); // fixed: was gpt-5-mini

            var userPrompt =
                $@"Client request:
                    {clientQuery}
 
                    Architect credentials and experience:
                    {architect.PortfolioText}
 
                    Based only on the credentials above, explain in 2 short sentences why this architect
                    is a good fit for the client's request.";

                        var messages = new List<ChatMessage>
                        {
                    new SystemChatMessage(
                        @"You are a professional architectural matching assistant.
 
                        Your task is to explain why a recommended architect fits the client's specific request,
                        based strictly on the architect's credentials and experience.
 
                        Guidelines:
                        - Speak directly to the client using ""you"" and ""your"".
                        - Ground every claim in the provided credentials — do not invent experience.
                        - Highlight the most relevant skills or projects that match the client's needs.
                        - Never mention portfolios, matching systems, scores, or AI.
                        - Do not list credentials as bullet points.
                        - Keep the tone confident and professional.
                        - Maximum of 2 short sentences."),

                    new UserChatMessage(userPrompt)
                };

            var response = await chatClient.CompleteChatAsync(messages);

            return response.Value.Content.Count > 0
                ? response.Value.Content[0].Text.Trim()
                : "This recommendation aligns well with your project needs and preferences.";
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
                    notification_Date = DateTime.UtcNow,
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

                // ✅ FIXED: only show conversations where an approved match exists
                conversationsRaw = conversationsRaw
                    .Where(c => context.Matches.Any(m =>
                        m.ClientId == clientId &&
                        m.ArchitectId == c.ArchitectId &&
                        m.MatchStatus == "Approved"))
                    .ToList();

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
                        MatchStatus = m.MatchStatus // ✅ FIXED
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

                // ✅ FIXED: block send if no approved match exists
                var approvedMatch = await context.Matches.FirstOrDefaultAsync(m =>
                    m.ClientId == request.ClientId &&
                    m.ArchitectId == request.ArchitectId &&
                    m.MatchStatus == "Approved");
                if (approvedMatch == null)
                    return StatusCode(403, new { success = false, message = "You are not allowed to message this architect." }); // ✅ FIXED

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

        public class MobileClarifyingQuestion
        {
            public string Question { get; set; } = "";
            public List<string> Options { get; set; } = new();
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

            [JsonPropertyName("MatchExplanation")]
            public string? MatchExplanation { get; set; }
            
        }
    }
}

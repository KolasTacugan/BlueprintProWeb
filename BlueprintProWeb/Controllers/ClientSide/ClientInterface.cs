using BlueprintProWeb.Data;
using BlueprintProWeb.Hubs;
using BlueprintProWeb.Models;
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
            ViewData["UserFirstName"] = currentUser?.user_fname ?? "User";
            return View();
        }

        public IActionResult BlueprintMarketplace()
        {
            ViewBag.StripePublishableKey = _stripeSettings.PublishableKey;

            var blueprints = context.Blueprints
                .Where(bp => bp.blueprintIsForSale == true) // only available ones
                .ToList();

            return View("BlueprintMarketplace", blueprints);
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

            var lineItems = cart.Select(item => new SessionLineItemOptions
            {
                PriceData = new SessionLineItemPriceDataOptions
                {
                    UnitAmount = (long)(item.Price * 100),
                    Currency = "php",
                    ProductData = new SessionLineItemPriceDataProductDataOptions
                    {
                        Name = item.Name
                    }
                },
                Quantity = item.Quantity
            }).ToList();

            var options = new SessionCreateOptions
            {
                PaymentMethodTypes = new List<string> { "card" },
                LineItems = lineItems,
                Mode = "payment",
                SuccessUrl = $"{Request.Scheme}://{Request.Host}/ClientInterface/BlueprintMarketplace?session_id={{CHECKOUT_SESSION_ID}}",
                CancelUrl = $"{Request.Scheme}://{Request.Host}/ClientInterface/Cancel"
            };

            Stripe.StripeConfiguration.ApiKey = _stripeSettings.SecretKey; // ✅ now loaded from appsettings

            var service = new SessionService();
            var session = service.Create(options);

            return Json(new { id = session.Id });
        }



        public IActionResult Success() => View();
        public IActionResult Cancel() => View();

        public class CartItemDto
        {
            public string id { get; set; }
            public string name { get; set; }
            public decimal price { get; set; }
            public string image { get; set; }
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> CompletePurchase([FromBody] List<int> blueprintIds)
        {
            var user = await userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            var purchasedBlueprints = await context.Blueprints
                .Where(bp => blueprintIds.Contains(bp.blueprintId))
                .ToListAsync();

            foreach (var bp in purchasedBlueprints)
            {
                bp.blueprintIsForSale = false; // no longer listed
                bp.clentId = user.Id;         // record buyer
            }

            var cart = await context.Carts.Include(c => c.Items)
                .FirstOrDefaultAsync(c => c.UserId == user.Id);

            if (cart != null)
                cart.Items.Clear();

            await context.SaveChangesAsync();

            return Json(new { success = true });
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

                // ✅ Debug logging
                Console.WriteLine($"--- Checking Architect: {a.user_fname} {a.user_lname} ---");
                Console.WriteLine($"Query vector length: {queryVector.Length}");
                Console.WriteLine($"Portfolio vector length: {vecA?.Length ?? 0}");

                    if (vecA == null || vecA.Length == 0 || vecA.Length != queryVector.Length)
                    {
                        Console.WriteLine("⚠️ Skipping architect due to invalid or mismatched vector.");
                        return (Architect: a, Score: double.MinValue);
                    }

                    var score = CosineSimilarity(queryVector, vecA);
                    Console.WriteLine($"✅ Cosine similarity score: {score}");

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
                            MatchStatus = "AI + Portfolio Match",
                            MatchDate = DateTime.UtcNow
                        })
                        .ToList();


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
            if (currentUser == null) return Unauthorized();

            // 1. Load matches for this client
            var matches = await context.Matches
                .Where(m => m.ClientId == currentUser.Id)
                .Select(m => new MatchViewModel
                {
                    MatchId = m.MatchId.ToString(),
                    ClientId = m.ClientId,
                    ArchitectId = m.ArchitectId,
                    ArchitectName = m.Architect.user_fname + " " + m.Architect.user_lname,
                    ArchitectStyle = m.Architect.user_Style,
                    ArchitectLocation = m.Architect.user_Location,
                    ArchitectBudget = m.Architect.user_Budget,
                    MatchStatus = m.MatchStatus,
                    MatchDate = m.MatchDate
                })
                .ToListAsync();

            // 2. Load conversations (one per architect)
            var conversations = await context.Messages
                .Where(m => m.ClientId == currentUser.Id || m.ArchitectId == currentUser.Id)
                .GroupBy(m => m.ArchitectId)
                .Select(g => new ChatViewModel
                {
                    ClientId = g.Key, // using ArchitectId here
                    ClientName = g.First().Architect.user_fname + " " + g.First().Architect.user_lname,
                    ClientProfileUrl = null, // placeholder until profile pics
                    LastMessageTime = g.Max(x => x.MessageDate),
                    Messages = new List<MessageViewModel>()
                })
                .ToListAsync();

            // 3. Load ActiveChat if architectId provided
            ChatViewModel? activeChat = null;
            if (!string.IsNullOrEmpty(architectId))
            {
                var messages = await context.Messages
                    .Where(m =>
                        (m.ArchitectId == architectId && m.ClientId == currentUser.Id) ||
                        (m.ArchitectId == currentUser.Id && m.ClientId == architectId))
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
                        SenderProfilePhoto = null,
                        IsOwnMessage = (m.SenderId == currentUser.Id)
                    })
                    .ToListAsync();

                activeChat = new ChatViewModel
                {
                    ClientId = architectId,
                    ClientName = messages.FirstOrDefault()?.SenderName ?? "Unknown",
                    ClientProfileUrl = null,
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
        public async Task<IActionResult> SendMessage(string architectId, string messageBody)
        {
            var currentUser = await userManager.GetUserAsync(User);
            if (currentUser == null) return Unauthorized();

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

            // notify architect in real-time
            await _hubContext.Clients.User(architectId).SendAsync("ReceiveMessage", new
            {
                SenderId = currentUser.Id,
                SenderName = currentUser.user_fname + " " + currentUser.user_lname,
                MessageBody = messageBody,
                MessageDate = DateTime.UtcNow.ToString("g")
            });

            return RedirectToAction("Messages", new { architectId });
        }
    
}

}




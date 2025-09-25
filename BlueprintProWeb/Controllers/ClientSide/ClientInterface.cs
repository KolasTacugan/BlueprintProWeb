using BlueprintProWeb.Data;
using BlueprintProWeb.Hubs;
using BlueprintProWeb.Models;
using BlueprintProWeb.ViewModels;
using iText.Commons.Actions.Contexts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Embeddings;
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
        public ClientInterfaceController(
           AppDbContext _context,
           UserManager<User> _userManager,
           OpenAIClient openAi,
           EmbeddingClient embeddingClient,
           IHubContext<ChatHub> hubContext)
        {
            context = _context;
            userManager = _userManager;
            _openAi = openAi;
            _embeddingClient = embeddingClient;
            _hubContext = hubContext;
        }


        public async Task<IActionResult> ClientDashboard()
        {
            var currentUser = await userManager.GetUserAsync(User);
            ViewData["UserFirstName"] = currentUser?.user_fname ?? "User";
            return View();
        }

        public IActionResult BlueprintMarketplace()
        {
            var blueprints = context.Blueprints
                .Where(bp => bp.blueprintIsForSale)
                .ToList();
            return View("BlueprintMarketplace", blueprints);
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




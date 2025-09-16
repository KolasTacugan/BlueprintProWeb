using BlueprintProWeb.Data;
using BlueprintProWeb.Models;
using BlueprintProWeb.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenAI;
using OpenAI.Chat;
using System.Text.Json;

namespace BlueprintProWeb.Controllers.ClientSide
{
    public class ClientInterfaceController : Controller
    {
        private readonly AppDbContext context;
        private readonly UserManager<User> userManager;
        private readonly OpenAIClient _openAi;

        public ClientInterfaceController(AppDbContext _context, UserManager<User> _userManager, OpenAIClient openAi)
        {
            context = _context;
            userManager = _userManager;
            _openAi = openAi;
        }

        public IActionResult ClientDashboard()
        {
            return View();
        }

        public IActionResult BlueprintMarketplace()
        {
            var blueprints = context.Blueprints
                .Where(bp => bp.blueprintIsForSale)
                .ToList();
            return View("BlueprintMarketplace", blueprints);
        }

        [HttpGet]
        public async Task<IActionResult> Matches(string query)
        {
            var currentUser = await userManager.GetUserAsync(User);

            if (currentUser == null)
            {
                return RedirectToAction("Login", "Account");
            }

            // If no query provided, fall back to user profile preferences
            string style = currentUser.user_Style;
            string location = currentUser.user_Location;
            string budget = currentUser.user_Budget;

            if (!string.IsNullOrWhiteSpace(query))
            {
                var chatClient = _openAi.GetChatClient("gpt-5-mini");

                var messages = new List<ChatMessage>
        {
            new SystemChatMessage("You are an assistant that extracts architect preferences from user input."),
            new UserChatMessage($"User is looking for: {query}. Extract their preferred Style, Location, and Budget in JSON format with keys: style, location, budget.")
        };

                var response = await chatClient.CompleteChatAsync(messages);
                var aiText = response.Value.Content[0].Text;

                try
                {
                    var prefs = JsonSerializer.Deserialize<Dictionary<string, string>>(aiText);
                    if (prefs != null)
                    {
                        if (prefs.TryGetValue("style", out var aiStyle) && !string.IsNullOrWhiteSpace(aiStyle))
                            style = aiStyle;

                        if (prefs.TryGetValue("location", out var aiLocation) && !string.IsNullOrWhiteSpace(aiLocation))
                            location = aiLocation;

                        if (prefs.TryGetValue("budget", out var aiBudget) && !string.IsNullOrWhiteSpace(aiBudget))
                            budget = aiBudget;
                    }
                }
                catch
                {
                    // fallback to profile values
                }
            }

            // Query DB
            var architects = context.Users.Where(u => u.user_role == "Architect");

            if (!string.IsNullOrEmpty(style))
                architects = architects.Where(a => a.user_Style == style);

            if (!string.IsNullOrEmpty(location))
                architects = architects.Where(a => a.user_Location == location);

            if (!string.IsNullOrEmpty(budget))
                architects = architects.Where(a => a.user_Budget == budget);

            // Build match list
            var matches = architects
                .OrderByDescending(a => a.user_Rating)
                .Select(a => new MatchViewModel
                {
                    MatchId = null,

                    ClientId = currentUser.Id,
                    ClientName = $"{currentUser.user_fname} {currentUser.user_lname}",
                    ArchitectId = a.Id,
                    ArchitectName = $"{a.user_fname} {a.user_lname}",
                    ArchitectStyle = a.user_Style,
                    ArchitectLocation = a.user_Location,
                    ArchitectBudget = a.user_Budget,
                    MatchStatus = string.IsNullOrWhiteSpace(query) ? "Profile Match" : "AI Match",
                    MatchDate = DateTime.UtcNow
                })
                .ToList();

            return View(matches);
        }


        // 🔥 AI-powered architect search

        [HttpGet]
        public async Task<IActionResult> SearchArchitects(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return Json(new List<MatchViewModel>());

            var chatClient = _openAi.GetChatClient("gpt-5-mini");

            var messages = new List<ChatMessage>
    {
        new SystemChatMessage("You are an assistant that extracts architect preferences from user input."),
        new UserChatMessage($"User is looking for: {query}. Extract their preferred Style, Location, and Budget in JSON format with keys: style, location, budget.")
    };

            var response = await chatClient.CompleteChatAsync(messages);
            var aiText = response.Value.Content[0].Text;

            // Parse JSON
            string style = null, location = null, budget = null;
            try
            {
                var prefs = JsonSerializer.Deserialize<Dictionary<string, string>>(aiText);
                prefs?.TryGetValue("style", out style);
                prefs?.TryGetValue("location", out location);
                prefs?.TryGetValue("budget", out budget);
            }
            catch { }

            // Query DB
            var architects = context.Users.Where(u => u.user_role == "Architect");

            if (!string.IsNullOrEmpty(style))
                architects = architects.Where(a => a.user_Style == style);

            if (!string.IsNullOrEmpty(location))
                architects = architects.Where(a => a.user_Location == location);

            if (!string.IsNullOrEmpty(budget))
                architects = architects.Where(a => a.user_Budget == budget);

            // ✅ Convert to MatchViewModel so JSON matches your JS
            var results = architects.Select(a => new MatchViewModel
            {
                MatchId = null,

                ClientId = "AI", // or currentUser.Id if logged in
                ClientName = "AI Search",
                ArchitectId = a.Id,
                ArchitectName = $"{a.user_fname} {a.user_lname}",
                ArchitectStyle = a.user_Style,
                ArchitectLocation = a.user_Location,
                ArchitectBudget = a.user_Budget,
                MatchStatus = "Suggested",
                MatchDate = DateTime.UtcNow
            }).ToList();

            return Json(results);
        }


    }
}

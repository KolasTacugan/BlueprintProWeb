using BlueprintProWeb.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;

namespace BlueprintProWeb.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MobileAuthController : ControllerBase
    {
        private readonly UserManager<User> _userManager;
        private readonly SignInManager<User> _signInManager;

        public MobileAuthController(UserManager<User> userManager, SignInManager<User> signInManager)
        {
            _userManager = userManager;
            _signInManager = signInManager;
        }

        // ✅ REGISTER (Mobile)
        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest model)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var user = new User
            {
                user_fname = model.FirstName,
                user_lname = model.LastName,
                PhoneNumber = model.PhoneNumber,
                Email = model.Email,
                UserName = model.Email,
                user_role = model.Role ?? "Client", // ✅ fallback if role not provided
                user_profilePhoto = model.ProfilePhoto ?? null,
                user_licenseNo = model.LicenseNo,
                user_Style = model.Style,
                user_Specialization = model.Specialization,
                user_Location = model.Location,
                user_Budget = model.LaborCost,
                user_CredentialsFile = model.CredentialsFile,
                user_Rating = 0, // default rating
                user_createdDate = DateTime.UtcNow,

                // ✅ Subscription defaults
                IsPro = false,
                SubscriptionPlan = "Free",
                SubscriptionStartDate = DateTime.UtcNow,
                SubscriptionEndDate = null
            };

            // ✅ Only generate embedding for Architects
            if (user.user_role == "Architect")
            {
                try
                {
                    user.PortfolioText =
                        $"Architect specializing in {user.user_Specialization ?? "various fields"}, " +
                        $"style: {user.user_Style ?? "adaptive"}, " +
                        $"based in {user.user_Location ?? "unspecified location"}, " +
                        $"budget preference: {user.user_Budget ?? "flexible"}, " +
                        $"rating: {user.user_Rating?.ToString() ?? "unrated"}.";

                    var embeddingVector = await GenerateEmbedding(user.PortfolioText);
                    user.PortfolioEmbedding = string.Join(",",
                        embeddingVector.Select(v => v.ToString(CultureInfo.InvariantCulture)));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Embedding generation failed: {ex.Message}");
                }
            }

            var result = await _userManager.CreateAsync(user, model.Password);

            if (result.Succeeded)
            {
                return Ok(new
                {
                    Message = "Registration successful",
                    UserId = user.Id,
                    Role = user.user_role,
                    Email = user.Email
                });
            }

            return BadRequest(result.Errors);
        }

        // ✅ LOGIN (Mobile)
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest model)
        {
            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user == null)
                return Unauthorized("Invalid credentials");

            var result = await _signInManager.CheckPasswordSignInAsync(user, model.Password, false);
            if (!result.Succeeded)
                return Unauthorized("Invalid credentials");

            return Ok(new
            {
                Message = "Login successful",
                UserId = user.Id,
                Email = user.Email,
                Role = user.user_role
            });
        }

        // 🧠 Placeholder — your embedding generator (implement as in your web controller)
        private async Task<float[]> GenerateEmbedding(string text)
        {
            // TODO: Replace this with your real embedding generation logic
            await Task.Delay(100);
            return new float[] { 0.12f, 0.45f, 0.88f }; // mock example
        }
    }

    // ✅ REGISTER REQUEST MODEL (matches User model)
    public class RegisterRequest
    {
        public string Email { get; set; }
        public string Password { get; set; }

        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string PhoneNumber { get; set; }

        public string? Role { get; set; }
        public string? LicenseNo { get; set; }
        public string? Style { get; set; }
        public string? Specialization { get; set; }
        public string? Location { get; set; }
        public string? LaborCost { get; set; }
        public string? ProfilePhoto { get; set; }
        public string? CredentialsFile { get; set; }
    }

    public class LoginRequest
    {
        public string Email { get; set; }
        public string Password { get; set; }
    }
}

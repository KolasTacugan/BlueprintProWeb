using BlueprintProWeb.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenAI;
using OpenAI.Embeddings;
using System.Globalization;

namespace BlueprintProWeb.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MobileAuthController : ControllerBase
    {
        private readonly UserManager<User> _userManager;
        private readonly SignInManager<User> _signInManager;
        private readonly OpenAIClient _openAi;
        private readonly OpenAI.Embeddings.EmbeddingClient _embeddingClient;

        public MobileAuthController(UserManager<User> userManager, SignInManager<User> signInManager, OpenAIClient openAi, OpenAI.Embeddings.EmbeddingClient embeddingClient)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _embeddingClient = embeddingClient;
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
            if (string.IsNullOrWhiteSpace(text))
                return Array.Empty<float>();

            var embeddingResponse = await _embeddingClient.GenerateEmbeddingAsync(text);
            return embeddingResponse.Value.ToFloats().ToArray();
        }


        [HttpPost("change-password")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest model)
        {
            if (model == null || string.IsNullOrEmpty(model.Email) || string.IsNullOrEmpty(model.NewPassword))
                return BadRequest(new { message = "Invalid request data", success = false, statusCode = 400 });

            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user == null)
                return NotFound(new { message = "Email not found", success = false, statusCode = 404 });

            // ✅ Verify if email is confirmed
            if (!user.EmailConfirmed)
                return BadRequest(new { message = "Email not verified. Please verify your email before changing the password.", success = false, statusCode = 400 });

            var removeResult = await _userManager.RemovePasswordAsync(user);
            if (!removeResult.Succeeded)
            {
                var errors = string.Join(", ", removeResult.Errors.Select(e => e.Description));
                return BadRequest(new { message = $"Failed to remove old password: {errors}", success = false, statusCode = 400 });
            }

            var addResult = await _userManager.AddPasswordAsync(user, model.NewPassword);
            if (!addResult.Succeeded)
            {
                var errors = string.Join(", ", addResult.Errors.Select(e => e.Description));
                return BadRequest(new { message = $"Failed to add new password: {errors}", success = false, statusCode = 400 });
            }

            return Ok(new { message = "Password changed successfully", success = true, statusCode = 200 });
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

    public class ChangePasswordRequest
    {
        public string Email { get; set; }
        public string NewPassword { get; set; }
    }
}

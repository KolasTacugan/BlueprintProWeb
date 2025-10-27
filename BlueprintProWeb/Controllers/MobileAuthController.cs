using BlueprintProWeb.Models;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenAI;
using OpenAI.Embeddings;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;


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

        // ✅ GET PROFILE (Mobile)
        [HttpGet("profile/{userId}")]
        public async Task<IActionResult> GetProfile(string userId)
        {
            if (string.IsNullOrEmpty(userId))
                return BadRequest(new { message = "User ID is required", success = false, statusCode = 400 });

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                return NotFound(new { message = "User not found", success = false, statusCode = 404 });

            var model = new
            {
                FirstName = user.user_fname,
                LastName = user.user_lname,
                Email = user.Email,
                PhoneNumber = user.PhoneNumber,
                Role = user.user_role,
                ProfilePhoto = user.user_profilePhoto,
                LicenseNo = user.user_licenseNo,
                Style = user.user_Style,
                Specialization = user.user_Specialization,
                Location = user.user_Location,
                Budget = user.user_Budget,
                CredentialsFilePath = user.user_CredentialsFile,
                PortfolioText = user.PortfolioText ?? "",
                IsPro = user.IsProActive // Computed subscription property
            };

            return Ok(new
            {
                message = "Profile retrieved successfully",
                success = true,
                statusCode = 200,
                data = model
            });
        }

        // ✅ UPDATE PROFILE (Mobile)
        [HttpPost("edit-profile")]
        public async Task<IActionResult> EditProfile([FromForm] MobileEditProfileRequest model)
        {
            if (model == null || string.IsNullOrEmpty(model.UserId))
                return BadRequest(new { message = "Invalid request data", success = false, statusCode = 400 });

            var user = await _userManager.FindByIdAsync(model.UserId);
            if (user == null)
                return NotFound(new { message = "User not found", success = false, statusCode = 404 });

            // ✅ Update common fields
            user.user_fname = model.FirstName ?? user.user_fname;
            user.user_lname = model.LastName ?? user.user_lname;
            user.Email = model.Email ?? user.Email;
            user.UserName = model.Email ?? user.UserName;
            user.PhoneNumber = model.PhoneNumber ?? user.PhoneNumber;

            // ✅ Handle profile picture upload
            if (model.ProfilePhoto != null && model.ProfilePhoto.Length > 0)
            {
                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif" };
                var fileExtension = Path.GetExtension(model.ProfilePhoto.FileName).ToLowerInvariant();

                if (!allowedExtensions.Contains(fileExtension))
                    return BadRequest(new { message = "Invalid image format", success = false, statusCode = 400 });

                if (model.ProfilePhoto.Length > 5 * 1024 * 1024)
                    return BadRequest(new { message = "Profile photo must be under 5MB", success = false, statusCode = 400 });

                string uploadDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images", "profiles");
                Directory.CreateDirectory(uploadDir);

                // Delete old photo if it exists
                if (!string.IsNullOrEmpty(user.user_profilePhoto))
                {
                    var oldPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", user.user_profilePhoto.TrimStart('~', '/'));
                    if (System.IO.File.Exists(oldPath))
                        System.IO.File.Delete(oldPath);
                }

                string fileName = $"{user.Id}_{Guid.NewGuid()}{fileExtension}";
                string filePath = Path.Combine(uploadDir, fileName);
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await model.ProfilePhoto.CopyToAsync(stream);
                }

                user.user_profilePhoto = $"~/images/profiles/{fileName}";
            }

            // ✅ Architect-specific logic
            if (string.Equals(user.user_role, "Architect", StringComparison.OrdinalIgnoreCase))
            {
                user.user_licenseNo = model.LicenseNo ?? user.user_licenseNo;
                user.user_Style = model.Style ?? user.user_Style;
                user.user_Specialization = model.Specialization ?? user.user_Specialization;
                user.user_Location = model.Location ?? user.user_Location;
                user.user_Budget = model.Budget ?? user.user_Budget;

                // ✅ Upload credentials file (portfolio)
                if (model.CredentialsFile != null && model.CredentialsFile.Length > 0)
                {
                    string credentialsDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "credentials");
                    Directory.CreateDirectory(credentialsDir);

                    string fileName = $"{user.Id}_{Guid.NewGuid()}_{model.CredentialsFile.FileName}";
                    string filePath = Path.Combine(credentialsDir, fileName);
                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await model.CredentialsFile.CopyToAsync(stream);
                    }

                    user.user_CredentialsFile = fileName;

                    // ✅ Generate embeddings from PDF text
                    try
                    {
                        string extractedText = await ExtractTextFromPdf(filePath);
                        user.PortfolioText = extractedText;

                        var embeddingVector = await GenerateEmbedding(extractedText);
                        user.PortfolioEmbedding = string.Join(",", embeddingVector.Select(v => v.ToString(CultureInfo.InvariantCulture)));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"⚠️ Embedding generation failed: {ex.Message}");
                    }
                }
            }

            // ✅ Save changes
            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
            {
                var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                return BadRequest(new { message = $"Profile update failed: {errors}", success = false, statusCode = 400 });
            }

            return Ok(new
            {
                message = "Profile updated successfully",
                success = true,
                statusCode = 200,
                data = new
                {
                    user.Id,
                    user.user_fname,
                    user.user_lname,
                    user.Email,
                    user.PhoneNumber,
                    user.user_role,
                    user.user_profilePhoto,
                    user.user_CredentialsFile
                }
            });
        }

        // 🧩 Helper: Extract text from PDF
        private async Task<string> ExtractTextFromPdf(string filePath)
        {
            var sb = new StringBuilder();
            using (var pdfReader = new PdfReader(filePath))
            using (var pdfDoc = new PdfDocument(pdfReader))
            {
                for (int i = 1; i <= pdfDoc.GetNumberOfPages(); i++)
                {
                    var page = pdfDoc.GetPage(i);
                    var text = PdfTextExtractor.GetTextFromPage(page);
                    sb.AppendLine(text);
                }
            }

            var cleaned = Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
            return await Task.FromResult(cleaned);
        }

   
        


    }

    public class MobileEditProfileRequest
    {
        [Required]
        public string UserId { get; set; }

        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Email { get; set; }
        public string? PhoneNumber { get; set; }

        public string? LicenseNo { get; set; }
        public string? Style { get; set; }
        public string? Specialization { get; set; }
        public string? Location { get; set; }
        public string? Budget { get; set; }

        public IFormFile? ProfilePhoto { get; set; }
        public IFormFile? CredentialsFile { get; set; }
    }


    // ✅ REGISTER REQUEST MODEL (matches User model)
    public class RegisterRequest
    {
        public string Email { get; set; }
        public string Password { get; set; }

        public string FirstName { get; set; }
        public string LastName { get; set; }

        [Required(ErrorMessage = "Phone number is required.")]
        [RegularExpression(@"^\d{11}$", ErrorMessage = "Phone number must be exactly 11 digits with no letters.")]
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

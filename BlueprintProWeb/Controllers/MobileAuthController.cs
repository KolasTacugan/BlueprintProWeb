using BlueprintProWeb.Data;
using BlueprintProWeb.Models;
using BlueprintProWeb.Settings;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Embeddings;
using Stripe;
using Stripe.Checkout;
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
        private readonly AppDbContext _context;
        private readonly StripeSettings _stripeSettings;

        public MobileAuthController(UserManager<User> userManager, SignInManager<User> signInManager, OpenAIClient openAi, OpenAI.Embeddings.EmbeddingClient embeddingClient, AppDbContext context,
            IOptions<StripeSettings> stripeSettingsOptions)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _embeddingClient = embeddingClient;
            _context = context;
            _stripeSettings = stripeSettingsOptions.Value;
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

        [HttpGet("edit-profile/{userId}")]
        public async Task<IActionResult> GetEditProfile(string userId)
        {
            if (string.IsNullOrEmpty(userId))
                return BadRequest(new { message = "User ID is required", success = false, statusCode = 400 });

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                return NotFound(new { message = "User not found", success = false, statusCode = 404 });

            var data = new
            {
                Id = user.Id,
                FirstName = user.user_fname,
                LastName = user.user_lname,
                Email = user.Email,
                PhoneNumber = user.PhoneNumber,
                Role = user.user_role,
                ProfilePhoto = user.user_profilePhoto?.Replace("~", ""),
                LicenseNo = user.user_licenseNo,
                Style = user.user_Style,
                Specialization = user.user_Specialization,
                Location = user.user_Location,
                Budget = user.user_Budget,
                CredentialsFile = user.user_CredentialsFile,
                IsPro = user.IsProActive
            };

            return Ok(new
            {
                message = "Edit profile data retrieved successfully",
                success = true,
                statusCode = 200,
                data
            });
        }

        [HttpPost("edit-profile")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> EditProfile([FromForm] MobileEditProfileRequest model)
        {
            if (!ModelState.IsValid)
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
                    return BadRequest(new { message = "Please upload a valid image file (jpg, jpeg, png, gif).", success = false });

                if (model.ProfilePhoto.Length > 5 * 1024 * 1024)
                    return BadRequest(new { message = "Profile picture must be less than 5MB.", success = false });

                string uploadDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images", "profiles");
                Directory.CreateDirectory(uploadDir);

                // Delete old profile picture if it exists
                if (!string.IsNullOrEmpty(user.user_profilePhoto))
                {
                    var oldPhotoPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", user.user_profilePhoto.TrimStart('~', '/'));
                    if (System.IO.File.Exists(oldPhotoPath))
                        System.IO.File.Delete(oldPhotoPath);
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

                // ✅ If portfolio (credentials) file uploaded → overwrite existing embedding
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

                    // ✅ Extract text and embed
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
                else
                {
                    // ✅ No portfolio uploaded → fallback to profile-based embedding
                    try
                    {
                        string profileText =
                            $"Architect specializing in {user.user_Specialization}, style: {user.user_Style}, " +
                            $"based in {user.user_Location}, budget preference: {user.user_Budget}, rating: {user.user_Rating}.";

                        user.PortfolioText = profileText;

                        var embeddingVector = await GenerateEmbedding(profileText);
                        user.PortfolioEmbedding = string.Join(",", embeddingVector.Select(v => v.ToString(CultureInfo.InvariantCulture)));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"⚠️ Profile-based embedding failed: {ex.Message}");
                    }
                }
            }
            else
            {
                // Prevent non-architects from uploading credentials
                if (model.CredentialsFile != null)
                {
                    return BadRequest(new { message = "Only architects can upload credentials.", success = false, statusCode = 403 });
                }
            }

            // ✅ Save updates
            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
            {
                var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                return BadRequest(new { message = $"Profile update failed: {errors}", success = false, statusCode = 400 });
            }

            await _signInManager.RefreshSignInAsync(user);

            // ✅ Success response (JSON)
            return Ok(new
            {
                message = "Profile updated successfully.",
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
                    ProfilePhoto = user.user_profilePhoto?.Replace("~", ""),
                    CredentialsFile = user.user_CredentialsFile,
                    user.user_licenseNo,
                    user.user_Style,
                    user.user_Specialization,
                    user.user_Location,
                    user.user_Budget
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


        // -------------------- ARCHITECT SUBSCRIPTION --------------------
        [HttpPost("CreateArchitectSubscription")]
        [Produces("application/json")]
        public IActionResult CreateArchitectSubscription([FromBody] ArchitectSubscriptionRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.ArchitectId))
                    return BadRequest(new { success = false, message = "ArchitectId is required." });

                StripeConfiguration.ApiKey = _stripeSettings.SecretKey;

                var options = new SessionCreateOptions
                {
                    PaymentMethodTypes = new List<string> { "card" },
                    LineItems = new List<SessionLineItemOptions>
            {
                new SessionLineItemOptions
                {
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        UnitAmount = 18000,
                        Currency = "php",
                        ProductData = new SessionLineItemPriceDataProductDataOptions
                        {
                            Name = "Pro Subscription Plan (Monthly)"
                        }
                    },
                    Quantity = 1
                }
            },
                    Mode = "payment",
                    SuccessUrl = "blueprintpro://subscription-success",
                    CancelUrl = "blueprintpro://subscription-cancel"
                };

                var service = new SessionService();
                var session = service.Create(options);

                return Ok(new
                {
                    success = true,
                    sessionId = session.Id,
                    paymentUrl = session.Url,
                    totalAmount = 180,
                    currency = "PHP"
                });

            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }


        [HttpPost("CompleteSubscription")]
        public async Task<IActionResult> CompleteSubscription([FromBody] ArchitectSubscriptionCompleteRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.ArchitectId))
                    return BadRequest(new { success = false, message = "ArchitectId is required." });

                var architect = await _context.Users.FirstOrDefaultAsync(u => u.Id == request.ArchitectId);
                if (architect == null)
                    return BadRequest(new { success = false, message = "Architect not found." });

                architect.IsPro = true;
                architect.SubscriptionPlan = "Pro";
                architect.SubscriptionStartDate = DateTime.UtcNow;
                architect.SubscriptionEndDate = DateTime.UtcNow.AddMonths(1);

                await _context.SaveChangesAsync();

                return Ok(new { success = true, message = "Pro Subscription activated successfully." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

    
        [HttpPost("DowngradeArchitectPlan")]
        public async Task<IActionResult> DowngradeArchitectPlan([FromBody] ArchitectSubscriptionRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.ArchitectId))
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "ArchitectId is required."
                    });
                }

                // ✅ Find architect by ID
                var architect = await _context.Users
                    .FirstOrDefaultAsync(a => a.Id == request.ArchitectId);

                if (architect == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Architect not found."
                    });
                }

                // ✅ Set plan to Free
                architect.IsPro = false;
                architect.SubscriptionPlan = "Free";
                architect.SubscriptionStartDate = null;
                architect.SubscriptionEndDate = null;

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Successfully downgraded to Free Plan."
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "An error occurred.",
                    error = ex.Message
                });
            }
        }


    }

    public class ArchitectSubscriptionRequest
    {
        public string ArchitectId { get; set; } = "";
    }

    public class ArchitectSubscriptionCompleteRequest
    {
        public string ArchitectId { get; set; } = "";
        // Optionally you may include sessionId or other metadata
        public string? SessionId { get; set; }
    }

    public class MobileEditProfileRequest
    {
        [Required]
        [FromForm(Name = "UserId")]
        public string UserId { get; set; }

        [FromForm(Name = "FirstName")]
        public string? FirstName { get; set; }

        [FromForm(Name = "LastName")]
        public string? LastName { get; set; }

        [FromForm(Name = "Email")]
        public string? Email { get; set; }

        [FromForm(Name = "PhoneNumber")]
        public string? PhoneNumber { get; set; }

        // Architect fields
        [FromForm(Name = "LicenseNo")]
        public string? LicenseNo { get; set; }

        [FromForm(Name = "Style")]
        public string? Style { get; set; }

        [FromForm(Name = "Specialization")]
        public string? Specialization { get; set; }

        [FromForm(Name = "Location")]
        public string? Location { get; set; }

        [FromForm(Name = "Budget")]
        public string? Budget { get; set; }

        [FromForm(Name = "ProfilePhoto")]
        public IFormFile? ProfilePhoto { get; set; }

        [FromForm(Name = "CredentialsFile")]
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

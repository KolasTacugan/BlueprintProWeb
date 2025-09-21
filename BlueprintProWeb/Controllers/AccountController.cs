
using BlueprintProWeb.Models;
using BlueprintProWeb.ViewModels;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenAI;
using OpenAI.Embeddings;
using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BlueprintProWeb.Controllers
{
    public class AccountController : Controller
    {
        private readonly SignInManager<User> _signInManager;
        private readonly UserManager<User> _userManager;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly OpenAIClient _openAi;
        private readonly OpenAI.Embeddings.EmbeddingClient _embeddingClient;

        public AccountController(
            SignInManager<User> signInManager,
            UserManager<User> userManager,
            IWebHostEnvironment webHostEnvironment,
            OpenAIClient openAi,
            OpenAI.Embeddings.EmbeddingClient embeddingClient)

        {
            _signInManager = signInManager;
            _userManager = userManager;
            _webHostEnvironment = webHostEnvironment;
            _openAi = openAi;
            _embeddingClient = embeddingClient;
        }


        // ---------------- LOGIN ----------------
        public IActionResult Login() => View();

        [HttpPost]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user == null)
            {
                ModelState.AddModelError("", "Email or password is incorrect.");
                return View(model);
            }

            var result = await _signInManager.PasswordSignInAsync(model.Email, model.Password, model.RememberMe, false);
            if (result.Succeeded)
            {
                var claims = new List<Claim> { new Claim("FirstName", user.user_fname ?? user.Email) };
                await _signInManager.SignInWithClaimsAsync(user, model.RememberMe, claims);

                return user.user_role switch
                {
                    "Architect" => RedirectToAction("ArchitectDashboard", "ArchitectInterface"),
                    "Client" => RedirectToAction("ClientDashboard", "ClientInterface"),
                    _ => RedirectToAction("Index", "Home")
                };
            }

            ModelState.AddModelError("", "Email or password is incorrect.");
            return View(model);
        }

        // ---------------- REGISTER ----------------
        [AllowAnonymous]
        public IActionResult ChooseRole() => View();

        [HttpGet]
        public IActionResult Register(string role) => View(new RegisterViewModel { Role = role });

        [HttpPost]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var user = new User
            {
                user_fname = model.FirstName,
                user_lname = model.LastName,
                PhoneNumber = model.PhoneNumber,
                Email = model.Email,
                UserName = model.Email,
                user_role = model.Role
            };

            if (model.Role == "Architect")
            {
                user.user_licenseNo = model.LicenseNo;
                user.user_Style = model.Style;
                user.user_Specialization = model.Specialization;
                user.user_Location = model.Location;
                user.user_Budget = model.LaborCost;
            }

                var result = await userManager.CreateAsync(client, model.Password);
                if (result.Succeeded)
                {

            foreach (var error in result.Errors)
                ModelState.AddModelError("", error.Description);

            return View(model);
        }

        // ---------------- PROFILE ----------------
        [HttpGet, Authorize]
        public async Task<IActionResult> Profile()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToAction("Login", "Account");

            var model = new ProfileViewModel
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
                PortfolioText = user.PortfolioText ?? ""
            };

            ViewData["Layout"] = user.user_role == "Architect"
                ? "~/Views/Shared/_ArchitectSharedLayout.cshtml"
                : "~/Views/Shared/_ClientSharedLayout.cshtml";

            return View(model);
        }

        [HttpPost, Authorize]
        public async Task<IActionResult> Profile(ProfileViewModel model)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToAction("Login", "Account");

            user.user_fname = model.FirstName;
            user.user_lname = model.LastName;
            user.PhoneNumber = model.PhoneNumber;

            await _userManager.UpdateAsync(user);
            return RedirectToAction("Profile");
        }

        // ---------------- EDIT PROFILE ----------------
        [HttpGet]
        public async Task<IActionResult> EditProfile()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToAction("Login", "Account");

            ViewData["Layout"] = user.user_role == "Architect"
                ? "~/Views/Shared/_ArchitectSharedLayout.cshtml"
                : "~/Views/Shared/_ClientSharedLayout.cshtml";

            return View(new ProfileViewModel
            {
                FirstName = user.user_fname,
                LastName = user.user_lname,
                Email = user.Email,
                PhoneNumber = user.PhoneNumber,
                Role = user.user_role
            });
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditProfile(ProfileViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
                return RedirectToAction("Login", "Account");

            // Update common fields
            user.user_fname = model.FirstName;
            user.user_lname = model.LastName;
            user.Email = model.Email;
            user.UserName = model.Email; // keep username in sync
            user.PhoneNumber = model.PhoneNumber;

            // Architect-specific
            if (string.Equals(user.user_role, "Architect", StringComparison.OrdinalIgnoreCase))
            {
                user.user_licenseNo = model.LicenseNo ?? user.user_licenseNo;
                user.user_Style = model.Style ?? user.user_Style;
                user.user_Specialization = model.Specialization ?? user.user_Specialization;
                user.user_Location = model.Location ?? user.user_Location;
                user.user_Budget = model.Budget ?? user.user_Budget;
                // Handle credentials file upload
                if (model.CredentialsFile != null)
                {
                    string uploadDir = Path.Combine(_webHostEnvironment.WebRootPath, "credentials");
                    Directory.CreateDirectory(uploadDir);

                    string fileName = Guid.NewGuid().ToString() + "-" + model.CredentialsFile.FileName;
                    string filePath = Path.Combine(uploadDir, fileName);

                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                    {
                        await model.CredentialsFile.CopyToAsync(fileStream);
                    }

                    user.user_CredentialsFile = fileName;

                    // 🔹 Extract text from the uploaded PDF
                    string extractedText = await ExtractTextFromPdf(filePath);
                    user.PortfolioText = extractedText;

                    // 🔹 Generate embeddings from extracted text
                    float[] embeddingVector = await GenerateEmbedding(extractedText);

                    // Save as comma-separated floats for persistence
                    user.PortfolioEmbedding = string.Join(",",
                        embeddingVector.Select(v => v.ToString(CultureInfo.InvariantCulture)));
                }
            }
            else
            {
                // Just in case someone tries to bypass UI and upload as Client
                if (model.CredentialsFile != null)
                {
                    ModelState.AddModelError("", "Only architects can upload credentials.");
                    return View(model);
                }
            }

            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
            {
                foreach (var error in result.Errors)
                    ModelState.AddModelError(string.Empty, error.Description);

                return View(model);
            }

            // Refresh sign-in so updated claims/roles are applied
            await _signInManager.RefreshSignInAsync(user);

            return RedirectToAction("Profile", "Account");
        }


        // ---------------- PASSWORD RESET ----------------
        public IActionResult VerifyEmail() => View();

        [HttpPost]
        public async Task<IActionResult> VerifyEmail(VerifyEmailViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var user = await _userManager.FindByNameAsync(model.Email);
            if (user == null)
            {
                ModelState.AddModelError("", "Email not found.");
                return View(model);
            }
            return RedirectToAction("ChangePassword", "Account", new { username = user.UserName });
        }

        public IActionResult ChangePassword(string username)
        {
            if (string.IsNullOrEmpty(username)) return RedirectToAction("VerifyEmail", "Account");
            return View(new ChangePasswordViewModel { Email = username });
        }

        [HttpPost]
        public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var user = await _userManager.FindByNameAsync(model.Email);
            if (user == null)
            {
                ModelState.AddModelError("", "Email not found!");
                return View(model);
            }

            var result = await _userManager.RemovePasswordAsync(user);
            if (result.Succeeded)
            {
                result = await _userManager.AddPasswordAsync(user, model.NewPassword);
                return RedirectToAction("Login", "Account");
            }

            foreach (var error in result.Errors)
                ModelState.AddModelError("", error.Description);

            return View(model);
        }

        // ---------------- LOGOUT ----------------
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            return RedirectToAction("Index", "Home");
        }
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

            return await Task.FromResult(sb.ToString());
        }

        private async Task<float[]> GenerateEmbedding(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return Array.Empty<float>();

            // Use the injected EmbeddingClient (you already have _embeddingClient in the constructor)
            var embeddingResponse = await _embeddingClient.GenerateEmbeddingAsync(text);

            // Convert to float[] the same way you do elsewhere
            return embeddingResponse.Value.ToFloats().ToArray();
        }




    }
}

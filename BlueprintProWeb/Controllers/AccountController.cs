using BlueprintProWeb.Models;
using BlueprintProWeb.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace BlueprintProWeb.Controllers
{
    public class AccountController : Controller
    {
        private readonly SignInManager<User> signInManager;
        private readonly UserManager<User> userManager;
        public IWebHostEnvironment WebHostEnvironment;

        public AccountController(SignInManager<User> signInManager, UserManager<User> userManager, IWebHostEnvironment webHostEnvironment)
        {
            this.signInManager = signInManager;
            this.userManager = userManager;
            WebHostEnvironment = webHostEnvironment;
        }

        public IActionResult Login()
        {
            return View();
        }
        [HttpPost]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (ModelState.IsValid)
            {
                var user = await userManager.FindByEmailAsync(model.Email);
                if (user == null)
                {
                    ModelState.AddModelError("", "Email or password is incorrect.");
                    return View(model);
                }

                var result = await signInManager.PasswordSignInAsync(model.Email, model.Password, model.RememberMe, false);
                if (result.Succeeded)
                {
                    var claims = new List<Claim>
            {
                new Claim("FirstName", user.user_fname ?? user.Email)
            };

                    await signInManager.SignInWithClaimsAsync(user, model.RememberMe, claims);

                    if (user.user_role == "Architect")
                    {
                        return RedirectToAction("ArchitectDashboard", "ArchitectInterface");
                    }
                    else if (user.user_role == "Client")
                    {
                        return RedirectToAction("ClientDashboard", "ClientInterface");
                    }
                    else
                    {
                        return RedirectToAction("Index", "Home");
                    }
                }
                else
                {
                    ModelState.AddModelError("", "Email or password is incorrect.");
                    return View(model);
                }
            }
            return View(model);
        }


        [AllowAnonymous]
        public IActionResult ChooseRole()
        {
            return View();
        }

        [HttpGet]
        public IActionResult Register( string role)
        {
            var model = new RegisterViewModel
            {
                Role = role 
            };
            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (ModelState.IsValid)
            {
                User client = new User
                {
                    user_fname = model.FirstName,
                    user_lname = model.LastName,
                    PhoneNumber = model.PhoneNumber,
                    Email = model.Email,
                    UserName = model.Email,
                    user_role = model.Role
                };

                // Only assign Architect-specific fields if role is Architect
                if (model.Role == "Architect")
                {
                    client.user_licenseNo = model.LicenseNo;
                    client.user_Style = model.Style;
                    client.user_Specialization = model.Specialization;
                    client.user_Location = model.Location;
                    client.user_Budget = model.LaborCost;
                }

                var result = await userManager.CreateAsync(client, model.Password);
                if (result.Succeeded)
                {

                    return RedirectToAction("Login", "Account");
                }
                else
                {
                    foreach (var error in result.Errors)
                    {
                        ModelState.AddModelError("", error.Description);
                    }
                    return View(model);
                }

            }
            return View(model);

        }

        [HttpPost]
        public async Task<IActionResult> Profile(ProfileViewModel model)
        {
            var user = await userManager.GetUserAsync(User);

            if (user == null)
            {
                return RedirectToAction("Login", "Account");
            }

            // Example if you allow updates
            user.user_fname = model.FirstName;
            user.user_lname = model.LastName;
            user.PhoneNumber = model.PhoneNumber;

            await userManager.UpdateAsync(user);

            // Reload updated values in the view
            return RedirectToAction("Profile");
        }

        [HttpGet]
        [Authorize] // Require login
        public async Task<IActionResult> Profile()
        {
            var user = await userManager.GetUserAsync(User);
            if (user == null)
            {
                return RedirectToAction("Login", "Account");
            }

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
                CredentialsFilePath = user.user_CredentialsFile
            };

            ViewData["Layout"] = user.user_role == "Architect"
                ? "~/Views/Shared/_ArchitectSharedLayout.cshtml"
                : "~/Views/Shared/_ClientSharedLayout.cshtml";

            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> EditProfile()
        {
            var user = await userManager.GetUserAsync(User);
            if (user == null)
                return RedirectToAction("Login", "Account");

            // Pick layout depending on role
            ViewData["Layout"] = user.user_role == "Architect"
                ? "~/Views/Shared/_ArchitectSharedLayout.cshtml"
                : "~/Views/Shared/_ClientSharedLayout.cshtml";

            var model = new ProfileViewModel
            {
                FirstName = user.user_fname,
                LastName = user.user_lname,
                Email = user.Email,
                PhoneNumber = user.PhoneNumber,
                Role= user.user_role
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditProfile(ProfileViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var user = await userManager.GetUserAsync(User);
            if (user == null)
                return RedirectToAction("Login", "Account");

            // Update user properties
            user.user_fname = model.FirstName;
            user.user_lname = model.LastName;
            user.Email = model.Email;
            user.UserName = model.Email; // keep username in sync
            user.PhoneNumber = model.PhoneNumber;

            // Handle PDF upload
            if (model.CredentialsFile != null)
            {
                string uploadDir = Path.Combine(WebHostEnvironment.WebRootPath, "credentials");
                Directory.CreateDirectory(uploadDir);
                string fileName = Guid.NewGuid().ToString() + "-" + model.CredentialsFile.FileName;
                string filePath = Path.Combine(uploadDir, fileName);
                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    await model.CredentialsFile.CopyToAsync(fileStream);
                }

                // Save path to the database (you need a field in User model)
                user.user_CredentialsFile = fileName;
            }

            var result = await userManager.UpdateAsync(user);
            if (!result.Succeeded)
            {
                foreach (var error in result.Errors)
                    ModelState.AddModelError(string.Empty, error.Description);

                return View(model);
            }

            // Refresh sign-in so new claims/values are applied
            await signInManager.RefreshSignInAsync(user);
            return RedirectToAction("Profile", "Account");
        }

        public IActionResult VerifyEmail()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> VerifyEmail(VerifyEmailViewModel model)
        {
            if (ModelState.IsValid)
            {
                var user = await userManager.FindByNameAsync(model.Email);
                if (user == null)
                {
                    ModelState.AddModelError("", "Something is wrong...");
                    return View(model);
                }
                else
                {
                    return RedirectToAction("ChangePassword", "Account", new { username = user.UserName });
                }
            }
            return View(model);
        }

        public IActionResult ChangePassword(string username)
        {
            if (string.IsNullOrEmpty(username))
            {
                return RedirectToAction("VerifyEmail", "Account");
            }
            return View(new ChangePasswordViewModel { Email = username });
        }

        [HttpPost]
        public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
        {
            if (ModelState.IsValid)
            {
                var user = await userManager.FindByNameAsync(model.Email);
                if (user != null)
                {
                    var result = await userManager.RemovePasswordAsync(user);
                    if (result.Succeeded)
                    {
                        result = await userManager.AddPasswordAsync(user, model.NewPassword);
                        return RedirectToAction("Login", "Account");
                    }
                    else
                    {
                        foreach (var error in result.Errors)
                        {
                            ModelState.AddModelError("", error.Description);
                        }
                        return View(model);
                    }
                }
                else
                {
                    ModelState.AddModelError("", "Email not found!");
                    return View(model);
                }

            }
            else
            {
                ModelState.AddModelError("", "Something went wrong...");
                return View(model);

            }
        }
        public async Task<IActionResult> Logout()
        {
            await signInManager.SignOutAsync();
            return RedirectToAction("Index", "Home");
        }
    }
}

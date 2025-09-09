using BlueprintProWeb.Models;
using BlueprintProWeb.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace BlueprintProWeb.Controllers
{
    public class AccountController : Controller
    {
        private readonly SignInManager<User> signInManager;
        private readonly UserManager<User> userManager;

        public AccountController(SignInManager<User> signInManager, UserManager<User> userManager)
        {
            this.signInManager = signInManager;
            this.userManager = userManager;
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
                var result = await signInManager.PasswordSignInAsync(model.Email, model.Password, model.RememberMe, false);
                if (result.Succeeded)
                {
                    var user = await userManager.FindByEmailAsync(model.Email);

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
                        // fallback if role not recognized
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

        
        [HttpGet]
        [AllowAnonymous] 
        public IActionResult Profile()
        {
            var model = new ProfileViewModel();
            return View(model);
        }

        public IActionResult EditProfile()
        {
            return View();
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

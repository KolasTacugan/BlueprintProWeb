using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace BlueprintProWeb.ViewModels
{
    public class RegisterViewModel
    {
        [Required (ErrorMessage = "A name is required.")] 
        [Display(Name = "First Name")]
        public string FirstName { get; set; }
        
        [Display(Name = "Last Name")]
        public string LastName { get; set; }
        
        [Required(ErrorMessage = "Phone number is required.")]
        [RegularExpression(@"^\d{11}$", ErrorMessage = "Phone number must be exactly 11 digits with no letters.")]
        [Display(Name = "Phone Number")]
        public string PhoneNumber { get; set; }
        
        [Required(ErrorMessage = "Email is required.")]
        [EmailAddress]
        public string Email { get; set; }
        
        [Required(ErrorMessage = "Password is required.")]
        [StringLength(40, MinimumLength = 8, ErrorMessage = "The {0} must be at least {2} and at most {1} characters long.")]
        [DataType(DataType.Password)]
        public string Password { get; set; }

        [Required(ErrorMessage = "Confirm Password is required.")]
        [DataType(DataType.Password)]
        [Display(Name = "Confirm Password")]
        [Compare("Password", ErrorMessage = "Password does not match.")]
        public string ConfirmPassword { get; set; }

        public string Role { get; set; }

        public string? LicenseNo { get; set; }
        public string? Style { get; set; }
        public string? Specialization { get; set; }
        public string? Location { get; set; }
        public string? LaborCost { get; set; }
    }
}

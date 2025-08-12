using Microsoft.AspNetCore.Identity;

namespace BlueprintProWeb.Models
{
    public class Client : IdentityUser  
    {
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public DateTime DateOfBirth { get; set; }
        public string PhoneNumber { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
    }
}

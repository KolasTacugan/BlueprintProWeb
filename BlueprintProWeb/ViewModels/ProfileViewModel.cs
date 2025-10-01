using Microsoft.AspNetCore.Http;

namespace BlueprintProWeb.ViewModels
{
    public class ProfileViewModel
    {
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string FullName => $"{FirstName} {LastName}";
        public string Email { get; set; } = string.Empty;
        public string? PhoneNumber { get; set; }

        public string Role { get; set; } = string.Empty;
        public string? ProfilePhoto { get; set; } = null; // Changed: No default profile picture

        // Architect-specific
        public string? LicenseNo { get; set; }
        public string? Style { get; set; }
        public string? Specialization { get; set; }
        public string? Location { get; set; }
        public string? Budget { get; set; }
        public IFormFile? CredentialsFile { get; set; }
        public string? CredentialsFilePath { get; set; }

        public string? PortfolioText { get; set; }      // extracted PDF text
        public string? PortfolioEmbedding { get; set; } // stored as comma-separated floats
    }
}

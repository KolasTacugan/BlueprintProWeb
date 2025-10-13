using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations.Schema;

namespace BlueprintProWeb.Models
{
    public class User : IdentityUser  
    {
        public string user_fname { get; set; }
        public string user_lname { get; set; }
        public string user_role { get; set; }
        public string? user_profilePhoto { get; set; }
        public DateTime user_createdDate { get; set; }
        public string? user_licenseNo { get; set; }
        public string? user_Style { get; set; }
        public string? user_Location { get; set; }
        public string? user_Budget { get; set; }
        public string? user_Specialization { get; set; }
        public double? user_Rating { get; set; }
        public string? user_CredentialsFile { get; set; }

        public string? PortfolioText { get; set; }
        public string? PortfolioEmbedding { get; set; } // store comma-separated floats

        // Subscription properties
        public bool IsPro { get; set; } = false;
        public DateTime? SubscriptionStartDate { get; set; }
        public DateTime? SubscriptionEndDate { get; set; }
        public string? SubscriptionPlan { get; set; } // "Free", "Pro"

        [NotMapped]
        public float[] PortfolioEmbeddingVector
        {
            get
            {
                if (string.IsNullOrEmpty(PortfolioEmbedding))
                    return Array.Empty<float>();

                return PortfolioEmbedding
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => float.Parse(s.Trim()))
                    .ToArray();
            }
            set
            {
                PortfolioEmbedding = string.Join(",", value);
            }
        }

        [NotMapped]
        public bool IsProActive => IsPro && (SubscriptionEndDate == null || SubscriptionEndDate > DateTime.UtcNow);
    }
}

using System;
using System.ComponentModel.DataAnnotations;

namespace BlueprintProWeb.Models
{
    public class Project
    {
        [Key]
        public string project_Id { get; set; } = Guid.NewGuid().ToString();

        // FKs
        [Required]
        public string user_clientId { get; set; }

        [Required]
        public string user_architectId { get; set; }

        [Required]
        public int blueprint_Id { get; set; }

        // Project details
        [Required]
        public string project_Title { get; set; }

        [Required]
        public string project_Status { get; set; } = "Ongoing";

        public string? project_Budget { get; set; }

        public DateTime project_startDate { get; set; } = DateTime.UtcNow;

        public DateTime? project_endDate { get; set; }
        public bool project_clientHasRated { get; set; } = false;

        // Navigation properties
        public User Client { get; set; }
        public User Architect { get; set; }
        public Blueprint Blueprint { get; set; }
    }
}

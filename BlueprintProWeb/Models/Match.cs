using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BlueprintProWeb.Models
{
    public class Match
    {
        [Key]
        public string MatchId { get; set; }

        [Required]
        public string ClientId { get; set; }

        [Required]
        public string ArchitectId { get; set; }

        [Required]
        public string MatchStatus { get; set; } = "Pending";

        [Required]
        public DateTime MatchDate { get; set; } = DateTime.UtcNow;

        // Navigation properties
        [ForeignKey(nameof(ClientId))]
        public User Client { get; set; }

        [ForeignKey(nameof(ArchitectId))]
        public User Architect { get; set; }

        public Match()
        {
            MatchId = Guid.NewGuid().ToString();
        }
    }
}

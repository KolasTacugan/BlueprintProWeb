using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BlueprintProWeb.Models
{
    public class Match
    {
        [Key]
        public int MatchId { get; set; }

        // Foreign Key for Client
        [Required]
        [StringLength(450)] // Matches AspNetUsers.Id (nvarchar(450))
        public string ClientId2 { get; set; }

        [ForeignKey(nameof(ClientId2))]
        public User Client { get; set; }

        // Foreign Key for Architect
        [Required]
        [StringLength(450)] // Matches AspNetUsers.Id (nvarchar(450))
        public string ArchitectId2 { get; set; }

        [ForeignKey(nameof(ArchitectId2))]
        public User Architect { get; set; }

        [Required]
        [MaxLength(50)]
        public string MatchStatus { get; set; }  // e.g., "Pending", "Accepted", "Rejected"

        [Required]
        public DateTime MatchDate { get; set; } = DateTime.UtcNow;
    }
}

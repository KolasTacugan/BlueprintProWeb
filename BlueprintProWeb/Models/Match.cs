using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BlueprintProWeb.Models
{
    public class Match
    {
        [Key]
        public int Match_ID { get; set; }

        // Foreign Key for Client
        [Required]
        [ForeignKey("Client")]
        public string Client_ID { get; set; }
        public User Client { get; set; }

        // Foreign Key for Architect
        [Required]
        [ForeignKey("Architect")]
        public string Architect_ID { get; set; }
        public User Architect { get; set; }

        [Required]
        [MaxLength(50)]
        public string Match_Status { get; set; }  // e.g., "Pending", "Accepted", "Rejected"

        [Required]
        public DateTime Match_Date { get; set; } = DateTime.UtcNow;
    }
}

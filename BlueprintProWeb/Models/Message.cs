using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BlueprintProWeb.Models
{
    public class Message
    {
        [Key]
        public Guid MessageId { get; set; }


        [Required]
        public string ClientId { get; set; }

        [Required]
        public string ArchitectId { get; set; }

        [Required]
        public string SenderId { get; set; }   // identifies who sent the message

        [Required]
        [MaxLength(2000)]
        public string MessageBody { get; set; }

        [Required]
        public DateTime MessageDate { get; set; } = DateTime.UtcNow;

        public bool IsRead { get; set; } = false;
        public bool IsDeleted { get; set; } = false;
        public string? AttachmentUrl { get; set; }

        // Navigation properties
        [ForeignKey(nameof(SenderId))]
        public User Sender { get; set; }

        [ForeignKey(nameof(ClientId))]
        public User Client { get; set; }

        [ForeignKey(nameof(ArchitectId))]
        public User Architect { get; set; }

       
    }
}

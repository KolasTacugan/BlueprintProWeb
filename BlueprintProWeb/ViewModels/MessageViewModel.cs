using System;
using System.ComponentModel.DataAnnotations;

namespace BlueprintProWeb.ViewModels
{
    public class MessageViewModel
    {
        public string MessageId { get; set; }

        [Required]
        public string ClientId { get; set; }

        [Required]
        public string ArchitectId { get; set; }

        [Required]
        public string SenderId { get; set; }

        [Required]
        [MaxLength(2000)]
        public string MessageBody { get; set; }

        public DateTime MessageDate { get; set; } = DateTime.UtcNow;
        public bool IsRead { get; set; } = false;
        public bool IsDeleted { get; set; } = false;
        public string? AttachmentUrl { get; set; }

        // Extra properties for displaying in views
        public string SenderName { get; set; }
        public string? SenderProfilePhoto { get; set; }

        // Helps chat bubble logic in the view
        public bool IsOwnMessage { get; set; }
    }
}

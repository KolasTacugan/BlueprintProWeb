using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BlueprintProWeb.Models
{
    public class Notification
    {
        [Key]
        public int notification_Id { get; set; }

        [Required]
        [ForeignKey("User")]
        public string user_Id { get; set; }   // FK to AspNetUsers (Identity)

        public string notification_Title { get; set; }
        public string notification_Message { get; set; }
        public bool notification_isRead { get; set; } = false;

        public DateTime notification_Date { get; set; } = DateTime.Now;

        // Navigation property
        public virtual User User { get; set; }
    }
}

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BlueprintProWeb.Models
{
    public class Blueprint
    {
        public int blueprintId { get; set; }
        public string blueprintImage { get; set; }
        [MaxLength(60)]
        public string blueprintName { get; set; } = "";
        public int blueprintPrice { get; set; }
        public string blueprintDescription { get; set; } = "";
        public string blueprintStyle { get; set; } = "";
        public DateTime Blueprint_CreatedDate { get; set; } = DateTime.UtcNow;
        
        [ForeignKey("Architect")]
        public string ArchitectId { get; set; }

        // Navigation property
        public User Architect { get; set; }
    }
}

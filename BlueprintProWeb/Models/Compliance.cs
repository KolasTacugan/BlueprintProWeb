using System.ComponentModel.DataAnnotations;

namespace BlueprintProWeb.Models
{
    public class Compliance
    {
        [Key]
        public int compliance_Id { get; set; }

        // FK → ProjectTracker
        public int projectTrack_Id { get; set; }
        public ProjectTracker ProjectTracker { get; set; }

        // Compliance files (paths or filenames)
        public string compliance_Structural { get; set; }
        public string compliance_Electrical { get; set; }
        public string compliance_Sanitary { get; set; }
        public string compliance_Zoning { get; set; }
        public string compliance_Others { get; set; }
    }
}

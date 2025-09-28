using System;
using System.ComponentModel.DataAnnotations;

namespace BlueprintProWeb.Models
{
    public class ProjectFile
    {
        [Key]
        public int projectFile_Id { get; set; }

        // FK → Project
        [Required]
        public string project_Id { get; set; }
        public Project Project { get; set; }

        // File details
        [Required]
        public string projectFile_fileName { get; set; }

        [Required]
        public string projectFile_Path { get; set; }

        public int projectFile_Version { get; set; }   // increments per project_Id
        public DateTime projectFile_uploadedDate { get; set; } = DateTime.UtcNow;
    }
}

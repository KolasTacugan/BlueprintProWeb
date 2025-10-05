using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace BlueprintProWeb.Models
{
    public class ProjectTracker
    {
        [Key]
        public int projectTrack_Id { get; set; }

        // FK → Project
        [Required]
        public string project_Id { get; set; }
        public Project Project { get; set; }

        // Tracker details
        public string project_Title { get; set; }
        public string blueprint_Description { get; set; }
        public DateTime projectTrack_dueDate { get; set; }
        public string projectTrack_Status { get; set; } = "Review";

        public string projectTrack_currentFileName { get; set; }
        public string projectTrack_currentFilePath { get; set; }
        public int projectTrack_currentRevision { get; set; } = 1;
        public string projectTrack_FinalizationNotes { get; set; }
        = "Estimated Cost (construction materials):\n\n--\n\n" +
          "Total Payment (payment for architect):\n\n--\n\n" +
          "Other Information:\n\n--";

        // Navigation
        public Compliance Compliance { get; set; }
        public ICollection<ProjectFile> ProjectFiles { get; set; } = new List<ProjectFile>();
    }
}

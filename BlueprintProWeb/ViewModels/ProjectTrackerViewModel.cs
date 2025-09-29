using BlueprintProWeb.Models; 

namespace BlueprintProWeb.ViewModels
{
    public class ProjectTrackerViewModel
    {
        public int projectTrack_Id { get; set; }
        public string project_Id { get; set; }

        public string CurrentFileName { get; set; }
        public string CurrentFilePath { get; set; }
        public int CurrentRevision { get; set; }
        public string Status { get; set; }

        public List<ProjectFile> RevisionHistory { get; set; } = new();
        public Compliance? Compliance { get; set; }
    }
}

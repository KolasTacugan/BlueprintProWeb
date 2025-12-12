using BlueprintProWeb.Models;

namespace BlueprintProWeb.ViewModels
{
    public class ClientDashboardViewModel
    {
        public int TotalMatches { get; set; }
        public int TotalPurchases { get; set; }
        public int TotalProjects { get; set; }
        public List<MatchSummary> RecentMatches { get; set; } = new();
        public List<BlueprintPurchase> RecentPurchases { get; set; } = new();
        public ProjectOverview? CurrentProject { get; set; }
        public List<ProjectOverview> Projects { get; set; } = new();

    }

    public class MatchSummary
    {
        public string ArchitectId { get; set; } // Added for messaging
        public string ArchitectName { get; set; }
        public string ArchitectSpecialty { get; set; }
        public string Status { get; set; }
        public DateTime MatchDate { get; set; }
    }

    public class BlueprintPurchase
    {
        public int BlueprintId { get; set; } // Added for project tracker linking
        public string BlueprintName { get; set; }
        public DateTime PurchaseDate { get; set; }
        public decimal Price { get; set; }
    }

    public class ProjectOverview
    {
        public string ProjectTitle { get; set; }
        public string Status { get; set; }
        public int ProgressPercentage { get; set; }
        public DateTime StartDate { get; set; }
        public string ArchitectName { get; set; }
    }
}
using BlueprintProWeb.Models;

namespace BlueprintProWeb.ViewModels
{
    public class ArchitectDashboardViewModel
    {
        public int TotalMatches { get; set; }
        public int TotalUploads { get; set; }
        public int TotalProjects { get; set; }
        public List<ClientMatchSummary> RecentMatches { get; set; } = new();
        public List<BlueprintUpload> RecentUploads { get; set; } = new();
        public ProjectOverview? CurrentProject { get; set; }
    }

    public class ClientMatchSummary
    {
        public string ClientId { get; set; } // Added for messaging
        public string ClientName { get; set; }
        public string ClientNeeds { get; set; }
        public string Status { get; set; }
        public DateTime MatchDate { get; set; }
    }

    public class BlueprintUpload
    {
        public string BlueprintName { get; set; }
        public DateTime UploadDate { get; set; }
        public decimal Price { get; set; }
        public bool IsForSale { get; set; }
    }
}
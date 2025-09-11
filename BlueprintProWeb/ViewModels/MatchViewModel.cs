namespace BlueprintProWeb.ViewModels
{
    public class MatchViewModel
    {
        public int MatchId { get; set; }

        // Client details
        public string ClientId { get; set; }
        public string ClientName { get; set; }

        // Architect details
        public string ArchitectId { get; set; }
        public string ArchitectName { get; set; }
        public string ArchitectStyle { get; set; }
        public string ArchitectLocation { get; set; }
        public string ArchitectBudget { get; set; }
        public double ArchitectRating { get; set; }

        // Match details
        public string MatchStatus { get; set; }   // "Pending", "Accepted", etc.
        public DateTime MatchDate { get; set; }
    }
}

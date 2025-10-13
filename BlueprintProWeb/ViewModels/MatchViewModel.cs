namespace BlueprintProWeb.ViewModels
{
    public class MatchViewModel
    {
        public string MatchId { get; set; }

        // IDs to track relationships
        public string ClientId { get; set; }
        public string ArchitectId { get; set; }

        // Optional: display names instead of IDs in the UI
        public string ClientName { get; set; }
        public string ClientEmail { get; set; }
        public string ClientPhone { get; set; }
        public string ArchitectName { get; set; }
        public string ArchitectEmail { get; set; }
        public string ArchitectPhone { get; set; }

        public string MatchStatus { get; set; }
        public DateTime MatchDate { get; set; }
        public string? ProfilePhoto { get; set; }
        public string ArchitectStyle { get; set; }
        public string ArchitectLocation { get; set; }
        public string ArchitectBudget { get; set; }

        public string? ClientProfileUrl { get; set; }

        public string? ArchitectProfileUrl { get; set; }

    }
}

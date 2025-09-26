namespace BlueprintProWeb.ViewModels
{
    public class MatchSummaryViewModel
    {
        public string ClientId { get; set; }
        public string ClientName { get; set; }
        public string? ProfileImageUrl { get; set; }
    }


    public class ChatViewModel
    {
        public string ClientId { get; set; }
        public string ClientName { get; set; }
        public string? ClientProfileUrl { get; set; }
        public DateTime LastMessageTime { get; set; }
        public List<MessageViewModel> Messages { get; set; } = new();
    }


    public class ChatPageViewModel
    {
        public List<MatchViewModel> Matches { get; set; } = new();
        public List<ChatViewModel> Conversations { get; set; } = new();
        public ChatViewModel? ActiveChat { get; set; }
    }
}

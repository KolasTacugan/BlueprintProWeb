namespace BlueprintProWeb.ViewModels
{
    public class ProfileViewModel
    {
        public string FullName { get; set; } = "Jane Doe";
        public string Email { get; set; } = "jane@example.com";
        public string PhoneNumber { get; set; } = "+63 900 000 0000";
        public DateTime? BirthDate { get; set; } = new DateTime(1999, 1, 1);
        public string Address { get; set; } = "Makati, Metro Manila";
        public string? AvatarUrl { get; set; } = "~/images/avatar-placeholder.png";
    }
}

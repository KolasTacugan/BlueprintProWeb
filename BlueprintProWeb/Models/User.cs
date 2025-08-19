using Microsoft.AspNetCore.Identity;

namespace BlueprintProWeb.Models
{
    public class User : IdentityUser  
    {
        public string user_fname { get; set; }
        public string user_lname { get; set; }
        public string user_role { get; set; }
        public string? user_profilePhoto { get; set; }
        public DateTime user_createdDate { get; set; }
        public string? user_licenseNo { get; set; }
        public string? user_Style { get; set; }
        public string? user_Location { get; set; }
        public string? user_Budget { get; set; }
        public string? user_Specialization { get; set; }
        public double? user_Rating { get; set; }


    }
}

using Microsoft.AspNetCore.Identity;

namespace BlueprintProWeb.Models
{
    public class Client : IdentityUser  
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
       
    }
}

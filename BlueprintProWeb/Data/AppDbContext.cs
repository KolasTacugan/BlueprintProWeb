using BlueprintProWeb.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BlueprintProWeb.Data
{
    public class AppDbContext : IdentityDbContext<Client>
    {
        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options)
        {
        }
        // DbSets for your entities can be added here, e.g.:
        // public DbSet<YourEntity> YourEntities { get; set; }
    }
}
    
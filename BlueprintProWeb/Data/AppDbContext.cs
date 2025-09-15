using BlueprintProWeb.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BlueprintProWeb.Data
{
    public class AppDbContext : IdentityDbContext<User>
    {
        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options)
        {
        }

        public DbSet<Blueprint> Blueprints { get; set; }
        public DbSet<Match> Matches { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // Configure Match entity relationships
            builder.Entity<Match>()
                .HasOne(m => m.Client)
                .WithMany()
                .HasForeignKey(m => m.ClientId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Match>()
                .HasOne(m => m.Architect)
                .WithMany()
                .HasForeignKey(m => m.ArchitectId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}

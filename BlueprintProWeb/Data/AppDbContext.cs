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
        public DbSet<Project> Projects { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // Configure Match
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

            // Configure Project
            builder.Entity<Project>(entity =>
            {
                entity.HasKey(p => p.project_Id);

                entity.HasOne(p => p.Client)
                      .WithMany()
                      .HasForeignKey(p => p.user_clientId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(p => p.Architect)
                      .WithMany()
                      .HasForeignKey(p => p.user_architectId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(p => p.Blueprint)
                      .WithMany()
                      .HasForeignKey(p => p.blueprint_Id)
                      .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}

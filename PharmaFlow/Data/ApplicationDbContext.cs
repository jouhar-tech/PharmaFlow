using Microsoft.EntityFrameworkCore;
using PharmaFlow.Models;

namespace PharmaFlow.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Profile> Profiles => Set<Profile>();
    public DbSet<Feedback> Feedback => Set<Feedback>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Feedback>(entity =>
        {
            entity.ToTable("feedback", "public");
            entity.HasKey(f => f.Id);
            entity.Property(f => f.Id).HasColumnName("id");
            entity.Property(f => f.ProfileId).HasColumnName("profile_id");
            entity.Property(f => f.Message).HasColumnName("message");
            entity.Property(f => f.CreatedAt).HasColumnName("created_at");
            entity.HasOne<Profile>()
                .WithMany()
                .HasForeignKey(f => f.ProfileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Profile>(entity =>
        {
            entity.ToTable("profiles", "public");
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Id).HasColumnName("id");
            entity.Property(p => p.UserId).HasColumnName("user_id");
            entity.Property(p => p.Username).HasColumnName("username");
            entity.Property(p => p.Email).HasColumnName("email");
            entity.Property(p => p.BusinessName).HasColumnName("business_name");
            entity.Property(p => p.PhoneNumber).HasColumnName("phone_number");
            entity.Property(p => p.ActiveStatus).HasColumnName("active_status");
            entity.Property(p => p.CreatedAt).HasColumnName("created_at");
        });
    }
}

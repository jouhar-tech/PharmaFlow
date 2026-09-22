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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Profile>(entity =>
        {
            entity.ToTable("profiles", "public");
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Id).HasColumnName("id");
            entity.Property(p => p.UserId).HasColumnName("user_id");
            entity.Property(p => p.Username).HasColumnName("username");
            entity.Property(p => p.Email).HasColumnName("email");
            entity.Property(p => p.PhoneNumber).HasColumnName("phone_number");
            entity.Property(p => p.CreatedAt).HasColumnName("created_at");
        });
    }
}

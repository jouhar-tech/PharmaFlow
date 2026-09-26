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
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductBatch> ProductBatches => Set<ProductBatch>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Feedback>(entity =>
        {
            entity.ToTable("feedback", "public");
            entity.HasKey(f => f.FeedBackID);
            entity.Property(f => f.FeedBackID).HasColumnName("FeedBackID");
            entity.Property(f => f.ProfileId).HasColumnName("profile_id");
            entity.Property(f => f.Message).HasColumnName("message");
            entity.Property(f => f.CreatedAt).HasColumnName("created_at");
            entity.HasOne<Profile>()
                .WithMany()
                .HasForeignKey(f => f.ProfileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("products", "public");
            entity.HasKey(p => p.ProductId);
            entity.Property(p => p.ProductId).HasColumnName("product_id");
            entity.Property(p => p.ProfileId).HasColumnName("profile_id");
            entity.Property(p => p.ProductName).HasColumnName("product_name");
            entity.Property(p => p.GenericName).HasColumnName("generic_name");
            entity.Property(p => p.BrandName).HasColumnName("brand_name");
            entity.Property(p => p.CategoryId).HasColumnName("category_id");
            entity.Property(p => p.DosageForm).HasColumnName("dosage_form");
            entity.Property(p => p.Strength).HasColumnName("strength");
            entity.Property(p => p.PackSize).HasColumnName("pack_size");
            entity.Property(p => p.Barcode).HasColumnName("barcode");
            entity.Property(p => p.Manufacturer).HasColumnName("manufacturer");
            entity.Property(p => p.HsnCode).HasColumnName("hsn_code");
            entity.Property(p => p.GstRate).HasColumnName("gst_rate");
            entity.Property(p => p.ReorderLevel).HasColumnName("reorder_level");
            entity.Property(p => p.IsPrescriptionRequired).HasColumnName("is_prescription_required");
            entity.Property(p => p.IsActive).HasColumnName("is_active");
            entity.Property(p => p.CreatedAt).HasColumnName("created_at");
            entity.Property(p => p.UpdatedAt).HasColumnName("updated_at");

            entity.HasMany(p => p.Batches)
                .WithOne(b => b.Product)
                .HasForeignKey(b => b.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProductBatch>(entity =>
        {
            entity.ToTable("product_batches", "public");
            entity.HasKey(b => b.BatchId);
            entity.Property(b => b.BatchId).HasColumnName("batch_id");
            entity.Property(b => b.ProductId).HasColumnName("product_id");
            entity.Property(b => b.BatchNumber).HasColumnName("batch_number");
            entity.Property(b => b.ManufacturingDate).HasColumnName("manufacturing_date");
            entity.Property(b => b.ExpiryDate).HasColumnName("expiry_date");
            entity.Property(b => b.QuantityOnHand).HasColumnName("quantity_on_hand");
            entity.Property(b => b.PurchaseUnitPrice).HasColumnName("purchase_unit_price");
            entity.Property(b => b.SellingUnitPrice).HasColumnName("selling_unit_price");
            entity.Property(b => b.SupplierId).HasColumnName("supplier_id");
            entity.Property(b => b.Location).HasColumnName("location");
            entity.Property(b => b.IsQuarantined).HasColumnName("is_quarantined");
            entity.Property(b => b.IsActive).HasColumnName("is_active");
            entity.Property(b => b.CreatedAt).HasColumnName("created_at");
            entity.Property(b => b.UpdatedAt).HasColumnName("updated_at");
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
            entity.Property(p => p.LastLoginAt).HasColumnName("last_login_at");
            entity.Property(p => p.LastLogoutAt).HasColumnName("last_logout_at");
            entity.Property(p => p.CreatedAt).HasColumnName("created_at");
        });
    }
}

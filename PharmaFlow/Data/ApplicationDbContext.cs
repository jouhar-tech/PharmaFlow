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
    public DbSet<ProductCatalog> ProductCatalog => Set<ProductCatalog>();
    public DbSet<ProductBatch> ProductBatches => Set<ProductBatch>();
    public DbSet<InvoiceImport> InvoiceImports => Set<InvoiceImport>();
    public DbSet<InvoiceImportItem> InvoiceImportItems => Set<InvoiceImportItem>();
    public DbSet<Staff> Staff => Set<Staff>();
    public DbSet<PushDeviceSubscription> PushDeviceSubscriptions => Set<PushDeviceSubscription>();
    public DbSet<NotificationDispatchLog> NotificationDispatchLogs => Set<NotificationDispatchLog>();
    public DbSet<PharmaFlowSavingsEvent> PharmaFlowSavingsEvents => Set<PharmaFlowSavingsEvent>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<SalesBill> SalesBills => Set<SalesBill>();
    public DbSet<SalesBillItem> SalesBillItems => Set<SalesBillItem>();

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
            entity.Property(p => p.CatalogId).HasColumnName("catalog_id");
            entity.HasOne<ProductCatalog>()
                .WithMany()
                .HasForeignKey(p => p.CatalogId)
                .OnDelete(DeleteBehavior.SetNull);
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

        modelBuilder.Entity<InvoiceImport>(entity =>
        {
            entity.ToTable("invoice_imports", "public");
            entity.HasKey(item => item.ImportId);
            entity.Property(item => item.ImportId).HasColumnName("import_id");
            entity.Property(item => item.ProfileId).HasColumnName("profile_id");
            entity.Property(item => item.OriginalFileName).HasColumnName("original_file_name");
            entity.Property(item => item.SourceType).HasColumnName("source_type");
            entity.Property(item => item.DistributorName).HasColumnName("distributor_name");
            entity.Property(item => item.InvoiceNumber).HasColumnName("invoice_number");
            entity.Property(item => item.InvoiceDate).HasColumnName("invoice_date");
            entity.Property(item => item.TotalAmount).HasColumnName("total_amount").HasPrecision(14, 2);
            entity.Property(item => item.RawOcrText).HasColumnName("raw_ocr_text");
            entity.Property(item => item.OcrConfidence).HasColumnName("ocr_confidence");
            entity.Property(item => item.Status).HasColumnName("status");
            entity.Property(item => item.ErrorMessage).HasColumnName("error_message");
            entity.Property(item => item.CreatedAt).HasColumnName("created_at");
            entity.Property(item => item.UpdatedAt).HasColumnName("updated_at");

            entity.HasOne<Profile>()
                .WithMany()
                .HasForeignKey(item => item.ProfileId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(item => item.Items)
                .WithOne(item => item.Import)
                .HasForeignKey(item => item.ImportId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<InvoiceImportItem>(entity =>
        {
            entity.ToTable("invoice_import_items", "public");
            entity.HasKey(item => item.ImportItemId);
            entity.Property(item => item.ImportItemId).HasColumnName("import_item_id");
            entity.Property(item => item.ImportId).HasColumnName("import_id");
            entity.Property(item => item.RowNumber).HasColumnName("row_number");
            entity.Property(item => item.RawLine).HasColumnName("raw_line");
            entity.Property(item => item.ProductName).HasColumnName("product_name");
            entity.Property(item => item.BatchNumber).HasColumnName("batch_number");
            entity.Property(item => item.ExpiryDate).HasColumnName("expiry_date");
            entity.Property(item => item.Quantity).HasColumnName("quantity");
            entity.Property(item => item.Mrp).HasColumnName("mrp").HasPrecision(14, 2);
            entity.Property(item => item.Confidence).HasColumnName("confidence");
            entity.Property(item => item.ValidationStatus).HasColumnName("validation_status");
            entity.Property(item => item.ValidationMessage).HasColumnName("validation_message");
            entity.Property(item => item.MatchedProductId).HasColumnName("matched_product_id");
            entity.Property(item => item.SavedBatchId).HasColumnName("saved_batch_id");
            entity.Property(item => item.CreatedAt).HasColumnName("created_at");
            entity.Property(item => item.UpdatedAt).HasColumnName("updated_at");

            entity.HasOne(item => item.MatchedProduct)
                .WithMany()
                .HasForeignKey(item => item.MatchedProductId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(item => item.SavedBatch)
                .WithMany()
                .HasForeignKey(item => item.SavedBatchId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Staff>(entity =>
        {
            entity.ToTable("staff", "public");
            entity.HasKey(s => s.StaffId);
            entity.Property(s => s.StaffId).HasColumnName("staff_id");
            entity.Property(s => s.ProfileId).HasColumnName("profile_id");
            entity.Property(s => s.AuthUserId).HasColumnName("auth_user_id");
            entity.Property(s => s.FullName).HasColumnName("full_name").HasMaxLength(120);
            entity.Property(s => s.Address).HasColumnName("address").HasMaxLength(500);
            entity.Property(s => s.PhoneNumber).HasColumnName("phone_number").HasMaxLength(20);
            entity.Property(s => s.Email).HasColumnName("email").HasMaxLength(160);
            entity.Property(s => s.ProfilePhotoUrl).HasColumnName("profile_photo_url");
            entity.Property(s => s.IsActive).HasColumnName("is_active");
            entity.Property(s => s.LastLoginAt).HasColumnName("last_login_at");
            entity.Property(s => s.LastLogoutAt).HasColumnName("last_logout_at");
            entity.Property(s => s.CreatedAt).HasColumnName("created_at");

            entity.HasOne(s => s.Profile)
                .WithMany()
                .HasForeignKey(s => s.ProfileId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(s => s.AuthUserId).IsUnique();
            entity.HasIndex(s => new { s.ProfileId, s.Email }).IsUnique();
            entity.HasIndex(s => new { s.ProfileId, s.PhoneNumber }).IsUnique();
        });

        modelBuilder.Entity<Profile>(entity =>
        {
            entity.ToTable("profiles", "public");
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Id).HasColumnName("id");
            entity.Property(p => p.CatalogId).HasColumnName("catalog_id");
            entity.Property(p => p.UserId).HasColumnName("user_id");
            entity.Property(p => p.Username).HasColumnName("username");
            entity.Property(p => p.Email).HasColumnName("email");
            entity.Property(p => p.BusinessName).HasColumnName("business_name");
            entity.Property(p => p.PhoneNumber).HasColumnName("phone_number");
            entity.Property(p => p.FullName).HasColumnName("full_name");
            entity.Property(p => p.Address).HasColumnName("address");
            entity.Property(p => p.SubscriptionPlan).HasColumnName("subscription_plan").HasMaxLength(20);
            entity.Property(p => p.SubscriptionStartsAt).HasColumnName("subscription_starts_at");
            entity.Property(p => p.SubscriptionEndsAt).HasColumnName("subscription_ends_at");
            entity.Property(p => p.ActiveStatus).HasColumnName("active_status");
            entity.Property(p => p.LastLoginAt).HasColumnName("last_login_at");
            entity.Property(p => p.LastLogoutAt).HasColumnName("last_logout_at");
            entity.Property(p => p.CreatedAt).HasColumnName("created_at");
            entity.Property(p => p.NotificationCycleStartAt).HasColumnName("notification_cycle_start_at");
        });

        modelBuilder.Entity<ProductCatalog>(entity =>
        {
            entity.ToTable("product_catalog", "public");
            entity.HasKey(c => c.CatalogId);
            entity.Property(c => c.CatalogId).HasColumnName("catalog_id");
            entity.Property(c => c.Source).HasColumnName("source").HasMaxLength(50);
            entity.Property(c => c.ExternalId).HasColumnName("external_id").HasMaxLength(160);
            entity.Property(c => c.ProductType).HasColumnName("product_type").HasMaxLength(40);
            entity.Property(c => c.ProductName).HasColumnName("product_name").HasMaxLength(200);
            entity.Property(c => c.GenericName).HasColumnName("generic_name").HasMaxLength(500);
            entity.Property(c => c.BrandName).HasColumnName("brand_name").HasMaxLength(160);
            entity.Property(c => c.Manufacturer).HasColumnName("manufacturer").HasMaxLength(200);
            entity.Property(c => c.DosageForm).HasColumnName("dosage_form").HasMaxLength(100);
            entity.Property(c => c.Strength).HasColumnName("strength").HasMaxLength(100);
            entity.Property(c => c.PackSize).HasColumnName("pack_size").HasMaxLength(100);
            entity.Property(c => c.Barcode).HasColumnName("barcode").HasMaxLength(100);
            entity.Property(c => c.HsnCode).HasColumnName("hsn_code").HasMaxLength(50);
            entity.Property(c => c.GstRate).HasColumnName("gst_rate").HasPrecision(5, 2);
            entity.Property(c => c.IsPrescriptionRequired).HasColumnName("is_prescription_required");
            entity.Property(c => c.SourceUrl).HasColumnName("source_url").HasMaxLength(500);
            entity.Property(c => c.FirstSeenAt).HasColumnName("first_seen_at");
            entity.Property(c => c.LastSyncedAt).HasColumnName("last_synced_at");
            entity.Property(c => c.UpdatedAt).HasColumnName("updated_at");
            entity.HasIndex(c => new { c.Source, c.ExternalId }).IsUnique();
            entity.HasIndex(c => c.Barcode);
            entity.HasIndex(c => c.ProductName);
        });

        modelBuilder.Entity<PushDeviceSubscription>(entity =>
        {
            entity.ToTable("push_device_subscriptions", "public");
            entity.HasKey(s => s.SubscriptionId);
            entity.Property(s => s.SubscriptionId).HasColumnName("subscription_id");
            entity.Property(s => s.ProfileId).HasColumnName("profile_id");
            entity.Property(s => s.UserRole).HasColumnName("user_role").HasMaxLength(20);
            entity.Property(s => s.Endpoint).HasColumnName("endpoint");
            entity.Property(s => s.P256dh).HasColumnName("p256dh");
            entity.Property(s => s.Auth).HasColumnName("auth");
            entity.Property(s => s.UserAgent).HasColumnName("user_agent");
            entity.Property(s => s.IsActive).HasColumnName("is_active");
            entity.Property(s => s.CreatedAt).HasColumnName("created_at");
            entity.Property(s => s.UpdatedAt).HasColumnName("updated_at");
            entity.Property(s => s.LastSuccessAt).HasColumnName("last_success_at");
            entity.Property(s => s.LastFailureAt).HasColumnName("last_failure_at");
            entity.HasOne<Profile>()
                .WithMany()
                .HasForeignKey(s => s.ProfileId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(s => s.Endpoint).IsUnique();
            entity.HasIndex(s => new { s.ProfileId, s.IsActive });
        });

        modelBuilder.Entity<NotificationDispatchLog>(entity =>
        {
            entity.ToTable("notification_dispatch_log", "public");
            entity.HasKey(n => n.NotificationId);
            entity.Property(n => n.NotificationId).HasColumnName("notification_id");
            entity.Property(n => n.ProfileId).HasColumnName("profile_id");
            entity.Property(n => n.NotificationType).HasColumnName("notification_type").HasMaxLength(40);
            entity.Property(n => n.PeriodKey).HasColumnName("period_key").HasMaxLength(20);
            entity.Property(n => n.SentAt).HasColumnName("sent_at");
            entity.HasOne<Profile>()
                .WithMany()
                .HasForeignKey(n => n.ProfileId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(n => new { n.ProfileId, n.NotificationType, n.PeriodKey }).IsUnique();
        });

        modelBuilder.Entity<PharmaFlowSavingsEvent>(entity =>
        {
            entity.ToTable("pharmaflow_savings_events", "public");
            entity.HasKey(e => e.EventId);
            entity.Property(e => e.EventId).HasColumnName("event_id");
            entity.Property(e => e.ProfileId).HasColumnName("profile_id");
            entity.Property(e => e.Category).HasColumnName("category").HasMaxLength(50);
            entity.Property(e => e.Amount).HasColumnName("amount").HasPrecision(14, 2);
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.SourceType).HasColumnName("source_type").HasMaxLength(80);
            entity.Property(e => e.SourceId).HasColumnName("source_id").HasMaxLength(160);
            entity.Property(e => e.OccurredAt).HasColumnName("occurred_at");
            entity.HasOne<Profile>()
                .WithMany()
                .HasForeignKey(e => e.ProfileId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.ProfileId, e.OccurredAt });
        });

        modelBuilder.Entity<Customer>(entity =>
        {
            entity.ToTable("customers", "public");
            entity.HasKey(c => c.CustomerId);
            entity.Property(c => c.CustomerId).HasColumnName("customer_id");
            entity.Property(c => c.ProfileId).HasColumnName("profile_id");
            entity.Property(c => c.FullName).HasColumnName("full_name").HasMaxLength(120);
            entity.Property(c => c.PhoneNumber).HasColumnName("phone_number").HasMaxLength(20);
            entity.Property(c => c.CreatedAt).HasColumnName("created_at");
            entity.Property(c => c.UpdatedAt).HasColumnName("updated_at");
            entity.HasOne<Profile>()
                .WithMany()
                .HasForeignKey(c => c.ProfileId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(c => new { c.ProfileId, c.PhoneNumber });
        });

        modelBuilder.Entity<SalesBill>(entity =>
        {
            entity.ToTable("sales_bills", "public");
            entity.HasKey(b => b.BillId);
            entity.Property(b => b.BillId).HasColumnName("bill_id");
            entity.Property(b => b.ProfileId).HasColumnName("profile_id");
            entity.Property(b => b.CustomerId).HasColumnName("customer_id");
            entity.Property(b => b.BillNumber).HasColumnName("bill_number").HasMaxLength(40);
            entity.Property(b => b.CustomerName).HasColumnName("customer_name").HasMaxLength(120);
            entity.Property(b => b.CustomerPhone).HasColumnName("customer_phone").HasMaxLength(20);
            entity.Property(b => b.Subtotal).HasColumnName("subtotal").HasPrecision(14, 2);
            entity.Property(b => b.TaxableAmount).HasColumnName("taxable_amount").HasPrecision(14, 2);
            entity.Property(b => b.CgstAmount).HasColumnName("cgst_amount").HasPrecision(14, 2);
            entity.Property(b => b.SgstAmount).HasColumnName("sgst_amount").HasPrecision(14, 2);
            entity.Property(b => b.IgstAmount).HasColumnName("igst_amount").HasPrecision(14, 2);
            entity.Property(b => b.GstAmount).HasColumnName("gst_amount").HasPrecision(14, 2);
            entity.Property(b => b.TotalAmount).HasColumnName("total_amount").HasPrecision(14, 2);
            entity.Property(b => b.PaymentMethod).HasColumnName("payment_method").HasMaxLength(20);
            entity.Property(b => b.Status).HasColumnName("status").HasMaxLength(20);
            entity.Property(b => b.CreatedAt).HasColumnName("created_at");
            entity.HasOne<Profile>()
                .WithMany()
                .HasForeignKey(b => b.ProfileId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Customer>()
                .WithMany()
                .HasForeignKey(b => b.CustomerId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(b => b.BillNumber).IsUnique();
            entity.HasIndex(b => new { b.ProfileId, b.CreatedAt });
        });

        modelBuilder.Entity<SalesBillItem>(entity =>
        {
            entity.ToTable("sales_bill_items", "public");
            entity.HasKey(i => i.BillItemId);
            entity.Property(i => i.BillItemId).HasColumnName("bill_item_id");
            entity.Property(i => i.BillId).HasColumnName("bill_id");
            entity.Property(i => i.ProductId).HasColumnName("product_id");
            entity.Property(i => i.BatchId).HasColumnName("batch_id");
            entity.Property(i => i.ProductName).HasColumnName("product_name").HasMaxLength(200);
            entity.Property(i => i.BatchNumber).HasColumnName("batch_number").HasMaxLength(100);
            entity.Property(i => i.ExpiryDate).HasColumnName("expiry_date");
            entity.Property(i => i.Quantity).HasColumnName("quantity").HasPrecision(12, 2);
            entity.Property(i => i.UnitPrice).HasColumnName("unit_price").HasPrecision(14, 2);
            entity.Property(i => i.PurchaseUnitPrice).HasColumnName("purchase_unit_price").HasPrecision(14, 2);
            entity.Property(i => i.Mrp).HasColumnName("mrp").HasPrecision(14, 2);
            entity.Property(i => i.GstRate).HasColumnName("gst_rate").HasPrecision(5, 2);
            entity.Property(i => i.TaxableAmount).HasColumnName("taxable_amount").HasPrecision(14, 2);
            entity.Property(i => i.CgstAmount).HasColumnName("cgst_amount").HasPrecision(14, 2);
            entity.Property(i => i.SgstAmount).HasColumnName("sgst_amount").HasPrecision(14, 2);
            entity.Property(i => i.IgstAmount).HasColumnName("igst_amount").HasPrecision(14, 2);
            entity.Property(i => i.GstAmount).HasColumnName("gst_amount").HasPrecision(14, 2);
            entity.Property(i => i.LineTotal).HasColumnName("line_total").HasPrecision(14, 2);

            entity.HasOne(i => i.Bill)
                .WithMany(b => b.Items)
                .HasForeignKey(i => i.BillId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Product>()
                .WithMany()
                .HasForeignKey(i => i.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ProductBatch>()
                .WithMany()
                .HasForeignKey(i => i.BatchId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}

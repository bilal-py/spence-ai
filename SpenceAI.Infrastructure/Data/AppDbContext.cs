using Microsoft.EntityFrameworkCore;
using SpenceAI.Application;
using SpenceAI.Domain;

namespace SpenceAI.Infrastructure.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext
{
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<MerchantRule> MerchantRules => Set<MerchantRule>();
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();
    public DbSet<AppSettings> Settings => Set<AppSettings>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // Readable enums in the DB file; easier to inspect and safe against reordering.
        builder.Properties<TransactionType>().HaveConversion<string>().HaveMaxLength(16);
        builder.Properties<TransactionSource>().HaveConversion<string>().HaveMaxLength(16);
        builder.Properties<ImportStatus>().HaveConversion<string>().HaveMaxLength(16);
        builder.Properties<AiProvider>().HaveConversion<string>().HaveMaxLength(16);
        builder.Properties<MoneyDirection>().HaveConversion<string>().HaveMaxLength(8);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Transaction>(e =>
        {
            e.Property(t => t.Description).HasMaxLength(500);
            e.Property(t => t.Merchant).HasMaxLength(200);
            e.Property(t => t.Notes).HasMaxLength(2000);
            e.Property(t => t.Fingerprint).HasMaxLength(32);
            e.HasIndex(t => t.Date);
            e.HasIndex(t => t.Fingerprint);
            e.HasOne(t => t.Category).WithMany(c => c.Transactions).HasForeignKey(t => t.CategoryId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(t => t.ImportBatch).WithMany(i => i.Transactions).HasForeignKey(t => t.ImportBatchId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Category>(e =>
        {
            e.Property(c => c.Name).HasMaxLength(60);
            e.Property(c => c.Color).HasMaxLength(9);
            e.Property(c => c.Icon).HasMaxLength(40);
            e.HasIndex(c => c.Name).IsUnique();
        });

        b.Entity<MerchantRule>(e =>
        {
            e.Property(r => r.Pattern).HasMaxLength(100);
            e.HasIndex(r => new { r.Pattern, r.Direction }).IsUnique();
            e.HasOne(r => r.Category).WithMany().HasForeignKey(r => r.CategoryId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ImportBatch>(e =>
        {
            e.Property(i => i.FileName).HasMaxLength(260);
            e.HasIndex(i => i.CreatedAt);
        });

        b.Entity<AppSettings>(e =>
        {
            e.Property(s => s.AiModel).HasMaxLength(100);
            e.Property(s => s.OllamaUrl).HasMaxLength(200);
            e.Property(s => s.Currency).HasMaxLength(3);
        });
    }
}

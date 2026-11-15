using Microsoft.EntityFrameworkCore;
using SpenceAI.Application;
using SpenceAI.Domain;

namespace SpenceAI.Infrastructure.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext
{
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<MerchantRule> MerchantRules => Set<MerchantRule>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();
    public DbSet<AppSettings> Settings => Set<AppSettings>();
    public DbSet<SavingsPot> SavingsPots => Set<SavingsPot>();
    public DbSet<PotContribution> PotContributions => Set<PotContribution>();
    public DbSet<PotChallenge> PotChallenges => Set<PotChallenge>();
    public DbSet<Bill> Bills => Set<Bill>();
    public DbSet<BillMark> BillMarks => Set<BillMark>();
    public DbSet<QuestCompletion> QuestCompletions => Set<QuestCompletion>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // Readable enums in the DB file; easier to inspect and safe against reordering.
        builder.Properties<TransactionType>().HaveConversion<string>().HaveMaxLength(16);
        builder.Properties<TransactionSource>().HaveConversion<string>().HaveMaxLength(16);
        builder.Properties<ImportStatus>().HaveConversion<string>().HaveMaxLength(16);
        builder.Properties<AiProvider>().HaveConversion<string>().HaveMaxLength(16);
        builder.Properties<AccountKind>().HaveConversion<string>().HaveMaxLength(16);
        builder.Properties<MoneyDirection>().HaveConversion<string>().HaveMaxLength(8);
        builder.Properties<PotSource>().HaveConversion<string>().HaveMaxLength(16);
        builder.Properties<ChallengeKind>().HaveConversion<string>().HaveMaxLength(16);
        builder.Properties<BillCadence>().HaveConversion<string>().HaveMaxLength(16);
        builder.Properties<BillMarkKind>().HaveConversion<string>().HaveMaxLength(16);
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
            e.HasOne(t => t.Account).WithMany(a => a.Transactions).HasForeignKey(t => t.AccountId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(t => t.ImportBatch).WithMany(i => i.Transactions).HasForeignKey(t => t.ImportBatchId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Category>(e =>
        {
            e.Property(c => c.Name).HasMaxLength(60);
            e.Property(c => c.Color).HasMaxLength(9);
            e.Property(c => c.Icon).HasMaxLength(40);
            e.HasIndex(c => c.Name).IsUnique();
        });

        b.Entity<Account>(e =>
        {
            e.Property(a => a.Name).HasMaxLength(60);
            e.Property(a => a.Last4).HasMaxLength(4);
            e.Property(a => a.Color).HasMaxLength(9);
            e.HasIndex(a => a.Name).IsUnique();
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
            e.Property(s => s.BackupCopyFolder).HasMaxLength(500);
            e.Property(s => s.PetName).HasMaxLength(30).HasDefaultValue("Sprig");
            e.Property(s => s.PetEnabled).HasDefaultValue(true);
            e.Property(s => s.PetAccessory).HasMaxLength(20);
            e.Property(s => s.DismissedBillSuggestions).HasMaxLength(4000);
        });

        b.Entity<SavingsPot>(e =>
        {
            e.Property(p => p.Name).HasMaxLength(60);
            e.Property(p => p.Color).HasMaxLength(9);
            e.Property(p => p.Icon).HasMaxLength(40);
            e.Property(p => p.LinkPattern).HasMaxLength(60);
        });

        b.Entity<PotContribution>(e =>
        {
            e.Property(c => c.Note).HasMaxLength(200);
            e.HasIndex(c => new { c.PotId, c.Date });
            e.HasOne(c => c.Pot).WithMany(p => p.Contributions).HasForeignKey(c => c.PotId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<PotChallenge>(e =>
        {
            e.HasOne(c => c.Pot).WithMany(p => p.Challenges).HasForeignKey(c => c.PotId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(c => c.Category).WithMany().HasForeignKey(c => c.CategoryId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Bill>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(60);
            e.Property(x => x.MatchPattern).HasMaxLength(100);
            e.Property(x => x.Notes).HasMaxLength(500);
            e.Property(x => x.Interval).HasDefaultValue(1);
            e.Property(x => x.RemindDaysBefore).HasDefaultValue(3);
            e.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<BillMark>(e =>
        {
            e.HasIndex(x => new { x.BillId, x.DueDate }).IsUnique();
            e.HasOne(x => x.Bill).WithMany(x => x.Marks).HasForeignKey(x => x.BillId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Transaction).WithMany().HasForeignKey(x => x.TransactionId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<QuestCompletion>(e =>
        {
            e.Property(x => x.Key).HasMaxLength(40);
            e.HasIndex(x => new { x.WeekStart, x.Key }).IsUnique();
        });
    }
}

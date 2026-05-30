using Microsoft.EntityFrameworkCore;
using SpenceAI.Application.Common.Interfaces;
using SpenceAI.Domain.Entities;

namespace SpenceAI.Infrastructure.Data;

public class ApplicationDbContext : DbContext, IApplicationDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<Category> Categories => Set<Category>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Expense>(entity =>
        {
            entity.Property(e => e.Amount).HasColumnType("decimal(18,2)");
        });

        modelBuilder.Entity<Category>()
            .HasMany(c => c.Expenses)
            .WithOne(e => e.Category)
            .HasForeignKey(e => e.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

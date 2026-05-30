using Microsoft.EntityFrameworkCore;
using SpenceAI.Domain.Entities;

namespace SpenceAI.Infrastructure.Data;

public static class DevelopmentDataSeeder
{
    public static async Task SeedAsync(ApplicationDbContext db)
    {
        if (await db.Expenses.AnyAsync())
        {
            return;
        }

        var categories = new[]
        {
            new Category { Name = "Groceries", ColorCode = "#0f766e" },
            new Category { Name = "Transport", ColorCode = "#2563eb" },
            new Category { Name = "Dining", ColorCode = "#dc2626" },
        };

        db.Categories.AddRange(categories);
        await db.SaveChangesAsync();

        var now = DateTime.UtcNow;
        db.Expenses.AddRange(
            new Expense
            {
                Amount = 84.32m,
                Date = now.AddDays(-3),
                Description = "Weekly supermarket run",
                CategoryId = categories[0].Id,
            },
            new Expense
            {
                Amount = 42.50m,
                Date = now.AddDays(-7),
                Description = "Ride share",
                CategoryId = categories[1].Id,
            },
            new Expense
            {
                Amount = 28.75m,
                Date = now.AddDays(-1),
                Description = "Dinner with friends",
                CategoryId = categories[2].Id,
            });

        await db.SaveChangesAsync();
    }
}

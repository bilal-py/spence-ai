using Microsoft.EntityFrameworkCore;
using SpenceAI.Domain;

namespace SpenceAI.Application.Categories;

/// <summary>Starter categories created on first run. Users can rename, recolor or delete them.</summary>
public static class CategoryTemplates
{
    public static readonly (string Name, string Icon, string Color, TransactionType Kind)[] Defaults =
    [
        ("Groceries", "shopping-basket", "#3f8f5f", TransactionType.Expense),
        ("Dining", "utensils", "#d9663b", TransactionType.Expense),
        ("Transport", "car", "#3b6fd9", TransactionType.Expense),
        ("Fuel", "fuel", "#7a5bd1", TransactionType.Expense),
        ("Shopping", "shopping-bag", "#c94f8a", TransactionType.Expense),
        ("Bills & utilities", "receipt", "#2f8f9d", TransactionType.Expense),
        ("Housing", "home", "#8a6a3b", TransactionType.Expense),
        ("Health", "heart-pulse", "#d94848", TransactionType.Expense),
        ("Entertainment", "clapperboard", "#b5832a", TransactionType.Expense),
        ("Subscriptions", "repeat", "#5b6bd1", TransactionType.Expense),
        ("Travel", "plane", "#2a9d6f", TransactionType.Expense),
        ("Education", "graduation-cap", "#6b7f2a", TransactionType.Expense),
        ("Personal care", "sparkles", "#a3589e", TransactionType.Expense),
        ("Gifts & donations", "gift", "#c7653f", TransactionType.Expense),
        ("Fees & charges", "badge-percent", "#7d7468", TransactionType.Expense),
        ("Salary", "briefcase", "#2f7d4f", TransactionType.Income),
        ("Refunds", "rotate-ccw", "#4f8fbf", TransactionType.Income),
        ("Other income", "circle-plus", "#6f9f3f", TransactionType.Income),
    ];

    /// <summary>Colors handed to categories the AI or user creates, in order.</summary>
    public static readonly string[] Palette =
    [
        "#3f8f5f", "#d9663b", "#3b6fd9", "#7a5bd1", "#c94f8a", "#2f8f9d", "#8a6a3b",
        "#d94848", "#b5832a", "#5b6bd1", "#2a9d6f", "#6b7f2a", "#a3589e", "#c7653f",
    ];

    public static async Task SeedAsync(IAppDbContext db, CancellationToken ct = default)
    {
        if (await db.Categories.AnyAsync(ct)) return;

        foreach (var (name, icon, color, kind) in Defaults)
        {
            db.Categories.Add(new Category { Name = name, Icon = icon, Color = color, Kind = kind });
        }

        await db.SaveChangesAsync(ct);
    }

    public static string NextColor(int existingCount) => Palette[existingCount % Palette.Length];
}

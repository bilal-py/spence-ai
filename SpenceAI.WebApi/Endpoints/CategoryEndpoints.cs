using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SpenceAI.Application;
using SpenceAI.Domain;

namespace SpenceAI.WebApi.Endpoints;

public record CategoryDto(int Id, string Name, string Color, string Icon, TransactionType Kind, decimal? MonthlyBudget, int TransactionCount);
public record UpsertCategoryRequest(string Name, string Color, string Icon, TransactionType Kind, decimal? MonthlyBudget);
public record RuleDto(int Id, string Pattern, int CategoryId, string CategoryName, string CategoryColor);

public static partial class CategoryEndpoints
{
    public static void MapCategoryEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/categories").WithTags("Categories");
        g.MapGet("/", List);
        g.MapPost("/", Create);
        g.MapPut("/{id:int}", Update);
        g.MapDelete("/{id:int}", Delete);

        var rules = app.MapGroup("/api/rules").WithTags("Rules");
        rules.MapGet("/", ListRules);
        rules.MapDelete("/{id:int}", DeleteRule);
    }

    private static async Task<List<CategoryDto>> List(IAppDbContext db, CancellationToken ct) =>
        await db.Categories.AsNoTracking()
            .OrderBy(c => c.Kind).ThenBy(c => c.Name)
            .Select(c => new CategoryDto(c.Id, c.Name, c.Color, c.Icon, c.Kind, c.MonthlyBudget, c.Transactions.Count))
            .ToListAsync(ct);

    private static async Task<CategoryDto> Create(UpsertCategoryRequest req, IAppDbContext db, CancellationToken ct)
    {
        var category = new Category();
        await Apply(category, req, db, ct);
        db.Categories.Add(category);
        await db.SaveChangesAsync(ct);
        return new CategoryDto(category.Id, category.Name, category.Color, category.Icon, category.Kind, category.MonthlyBudget, 0);
    }

    private static async Task<CategoryDto> Update(int id, UpsertCategoryRequest req, IAppDbContext db, CancellationToken ct)
    {
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new AppException("Category not found.", 404);
        await Apply(category, req, db, ct);
        await db.SaveChangesAsync(ct);
        var count = await db.Transactions.CountAsync(t => t.CategoryId == id, ct);
        return new CategoryDto(category.Id, category.Name, category.Color, category.Icon, category.Kind, category.MonthlyBudget, count);
    }

    /// <summary>Transactions in the category become uncategorized; rules pointing at it are removed.</summary>
    private static async Task<IResult> Delete(int id, IAppDbContext db, CancellationToken ct)
    {
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new AppException("Category not found.", 404);
        await db.Transactions.Where(t => t.CategoryId == id).ExecuteUpdateAsync(s => s.SetProperty(t => t.CategoryId, (int?)null), ct);
        db.Categories.Remove(category);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<List<RuleDto>> ListRules(IAppDbContext db, CancellationToken ct) =>
        await db.MerchantRules.AsNoTracking()
            .OrderBy(r => r.Pattern)
            .Select(r => new RuleDto(r.Id, r.Pattern, r.CategoryId, r.Category!.Name, r.Category.Color))
            .ToListAsync(ct);

    private static async Task<IResult> DeleteRule(int id, IAppDbContext db, CancellationToken ct)
    {
        await db.MerchantRules.Where(r => r.Id == id).ExecuteDeleteAsync(ct);
        return Results.NoContent();
    }

    private static async Task Apply(Category c, UpsertCategoryRequest req, IAppDbContext db, CancellationToken ct)
    {
        var name = req.Name?.Trim() ?? "";
        if (name.Length is 0 or > 60) throw new AppException("Category names need 1 to 60 characters.");
        if (!HexColor().IsMatch(req.Color ?? "")) throw new AppException("Pick a color like #3f8f5f.");
        if (req.MonthlyBudget is < 0) throw new AppException("Budget can't be negative.");
        if (await db.Categories.AnyAsync(x => x.Id != c.Id && x.Name.ToLower() == name.ToLower(), ct))
        {
            throw new AppException($"You already have a category called \"{name}\".");
        }

        c.Name = name;
        c.Color = req.Color!;
        c.Icon = string.IsNullOrWhiteSpace(req.Icon) ? "tag" : req.Icon.Trim();
        c.Kind = req.Kind == TransactionType.Income ? TransactionType.Income : TransactionType.Expense;
        c.MonthlyBudget = req.MonthlyBudget is 0 ? null : req.MonthlyBudget;
    }

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColor();
}

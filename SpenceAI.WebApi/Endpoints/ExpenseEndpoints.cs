using SpenceAI.Application.Common.Interfaces;
using SpenceAI.Domain.Entities;

namespace SpenceAI.WebApi.Endpoints;

public static class ExpenseEndpoints
{
    public static RouteGroupBuilder MapExpenseEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/expenses").WithTags("Expenses");

        group.MapGet("/", GetExpenses);
        group.MapGet("/summary", GetSummary);

        return group;
    }

    private static async Task<IResult> GetExpenses(
        int? year,
        int? month,
        string? categoryIds,
        IExpenseRepository repository)
    {
        var ids = ParseCategoryIds(categoryIds);
        var expenses = await repository.GetFilteredExpensesAsync(year, month, ids);
        return Results.Ok(expenses);
    }

    private static async Task<IResult> GetSummary(
        int? year,
        int? month,
        IExpenseRepository repository)
    {
        var expenses = await repository.GetFilteredExpensesAsync(year, month, null);

        var byCategory = expenses
            .GroupBy(e => new
            {
                e.CategoryId,
                Name = e.Category?.Name ?? "Uncategorized",
                Color = e.Category?.ColorCode ?? "#64748b",
            })
            .Select(g => new CategoryBreakdownDto(
                g.Key.Name,
                g.Sum(e => e.Amount),
                g.Key.Color,
                g.Count()))
            .OrderByDescending(c => c.Total)
            .ToList();

        var top = byCategory.FirstOrDefault();

        var response = new ExpenseSummaryResponse(
            expenses.Sum(e => e.Amount),
            top is null
                ? null
                : new CategorySummaryDto(top.CategoryName, top.Total, top.ColorCode),
            UploadCount: 0,
            byCategory);

        return Results.Ok(response);
    }

    private static List<int>? ParseCategoryIds(string? categoryIds)
    {
        if (string.IsNullOrWhiteSpace(categoryIds))
        {
            return null;
        }

        var ids = categoryIds
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(id => int.TryParse(id, out var parsed) ? parsed : (int?)null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToList();

        return ids.Count > 0 ? ids : null;
    }

    private sealed record ExpenseSummaryResponse(
        decimal TotalSpent,
        CategorySummaryDto? TopCategory,
        int UploadCount,
        List<CategoryBreakdownDto> ByCategory);

    private sealed record CategorySummaryDto(string Name, decimal Total, string? ColorCode);

    private sealed record CategoryBreakdownDto(
        string CategoryName,
        decimal Total,
        string ColorCode,
        int Count);
}

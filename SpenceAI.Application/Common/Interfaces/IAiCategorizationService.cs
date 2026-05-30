using SpenceAI.Application.DTOs;

namespace SpenceAI.Application.Common.Interfaces;

public interface IAiCategorizationService
{
    Task<List<ExtractedExpenseDto>> CategorizeExpensesAsync(string rawText, List<string> existingCategories);
}

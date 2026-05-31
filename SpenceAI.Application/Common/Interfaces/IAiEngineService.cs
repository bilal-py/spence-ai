using SpenceAI.Application.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SpenceAI.Application.Common.Interfaces;

public interface IAiEngineService
{
    /// <summary>
    /// Ask the AI engine to categorize expenses from the provided raw text.
    /// The implementation should return a list of <see cref="ExtractedExpenseDto"/>.
    /// </summary>
    Task<List<ExtractedExpenseDto>> CategorizeExpensesAsync(string rawText, List<string> existingCategories, string apiKey);
}

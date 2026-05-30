using SpenceAI.Domain.Entities;

namespace SpenceAI.Application.Common.Interfaces;

public interface IExpenseRepository
{
    Task<Expense?> GetByIdAsync(int id);
    Task AddAsync(Expense expense);
    Task UpdateAsync(Expense expense);
    Task DeleteAsync(int id);
    Task<List<Expense>> GetFilteredExpensesAsync(int? year, int? month, List<int>? categoryIds);
}

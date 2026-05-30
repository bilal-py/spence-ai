using Microsoft.EntityFrameworkCore;
using SpenceAI.Application.Common.Interfaces;
using SpenceAI.Domain.Entities;

namespace SpenceAI.Infrastructure.Data.Repositories;

public class ExpenseRepository : IExpenseRepository
{
    private readonly ApplicationDbContext _context;

    public ExpenseRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Expense?> GetByIdAsync(int id)
    {
        return await _context.Expenses
            .Include(e => e.Category)
            .FirstOrDefaultAsync(e => e.Id == id);
    }

    public async Task AddAsync(Expense expense)
    {
        await _context.Expenses.AddAsync(expense);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateAsync(Expense expense)
    {
        _context.Expenses.Update(expense);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var expense = await _context.Expenses.FindAsync(id);
        if (expense is not null)
        {
            _context.Expenses.Remove(expense);
            await _context.SaveChangesAsync();
        }
    }

    public async Task<List<Expense>> GetFilteredExpensesAsync(int? year, int? month, List<int>? categoryIds)
    {
        IQueryable<Expense> query = _context.Expenses
            .Include(e => e.Category)
            .AsQueryable();

        if (year.HasValue)
        {
            query = query.Where(e => e.Date.Year == year.Value);
        }

        if (month.HasValue)
        {
            query = query.Where(e => e.Date.Month == month.Value);
        }

        if (categoryIds is not null)
        {
            query = query.Where(e => categoryIds.Contains(e.CategoryId));
        }

        return await query
            .OrderByDescending(e => e.Date)
            .ToListAsync();
    }
}

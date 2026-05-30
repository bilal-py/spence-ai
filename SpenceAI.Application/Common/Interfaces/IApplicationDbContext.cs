using Microsoft.EntityFrameworkCore;
using SpenceAI.Domain.Entities;

namespace SpenceAI.Application.Common.Interfaces;

public interface IApplicationDbContext
{
    DbSet<Category> Categories { get; }
    DbSet<Expense> Expenses { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

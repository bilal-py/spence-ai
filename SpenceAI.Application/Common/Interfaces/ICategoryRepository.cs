using SpenceAI.Domain.Entities;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SpenceAI.Application.Common.Interfaces;

public interface ICategoryRepository
{
    Task<Category?> GetByIdAsync(int id);
    Task<List<Category>> GetAllAsync();
    Task AddAsync(Category category);
    Task UpdateAsync(Category category);
    Task DeleteAsync(int id);
}
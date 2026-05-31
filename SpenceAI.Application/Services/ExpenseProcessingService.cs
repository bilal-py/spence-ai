using Microsoft.EntityFrameworkCore;
using SpenceAI.Application.Common.Interfaces;
using SpenceAI.Application.DTOs;
using SpenceAI.Domain.Entities;

namespace SpenceAI.Application.Services;

public class ExpenseProcessingService
{
    private const string DefaultNewCategoryColor = "#64748b";

    private readonly IPdfExtractionService _pdfExtractionService;
    private readonly IAiEngineFactory _aiEngineFactory;
    private readonly IEncryptionService _encryptionService;
    private readonly IApplicationDbContext _db;

    public ExpenseProcessingService(
        IPdfExtractionService pdfExtractionService,
        IAiEngineFactory aiEngineFactory,
        IEncryptionService encryptionService,
        IApplicationDbContext db)
    {
        _pdfExtractionService = pdfExtractionService;
        _aiEngineFactory = aiEngineFactory;
        _encryptionService = encryptionService;
        _db = db;
    }

    public async Task ProcessPdfUploadAsync(Stream pdfStream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pdfStream);

        var rawText = await _pdfExtractionService.ExtractTextFromPdfAsync(pdfStream);

        var existingCategoryNames = await _db.Categories
            .AsNoTracking()
            .Select(c => c.Name)
            .ToListAsync(cancellationToken);

        // Resolve AI provider and API key from settings
        var settings = await _db.AppSettings.FirstOrDefaultAsync(cancellationToken);
        var provider = settings?.SelectedAiProvider ?? "Gemini";
        var encryptedKey = settings?.EncryptedAiApiKey;
        var apiKey = string.IsNullOrWhiteSpace(encryptedKey)
            ? string.Empty
            : _encryptionService.Decrypt(encryptedKey);

        var engine = _aiEngineFactory.GetEngine(provider);
        var extractedExpenses = await engine.CategorizeExpensesAsync(rawText, existingCategoryNames, apiKey);

        var categories = await _db.Categories.ToListAsync(cancellationToken);
        var categoryByName = categories.ToDictionary(
            c => c.Name,
            StringComparer.OrdinalIgnoreCase);

        foreach (var dto in extractedExpenses)
        {
            var category = await ResolveCategoryAsync(
                dto,
                categoryByName,
                existingCategoryNames,
                cancellationToken);

            var expense = new Expense
            {
                Description = dto.Description,
                Amount = dto.Amount,
                Date = dto.Date,
                CategoryId = category.Id,
            };

            _db.Expenses.Add(expense);
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<Category> ResolveCategoryAsync(
        ExtractedExpenseDto dto,
        Dictionary<string, Category> categoryByName,
        List<string> existingCategoryNames,
        CancellationToken cancellationToken)
    {
        var categoryName = dto.CategoryName.Trim();

        if (categoryByName.TryGetValue(categoryName, out var existingCategory))
        {
            return existingCategory;
        }

        var nameExistsInDatabase = existingCategoryNames.Any(
            name => string.Equals(name, categoryName, StringComparison.OrdinalIgnoreCase));

        if (!dto.IsNewCategory && nameExistsInDatabase)
        {
            var categoryFromDb = await _db.Categories
                .FirstAsync(
                    c => c.Name.ToLower() == categoryName.ToLower(),
                    cancellationToken);

            categoryByName[categoryFromDb.Name] = categoryFromDb;
            return categoryFromDb;
        }

        var newCategory = new Category
        {
            Name = categoryName,
            ColorCode = DefaultNewCategoryColor,
        };

        _db.Categories.Add(newCategory);
        await _db.SaveChangesAsync(cancellationToken);

        categoryByName[categoryName] = newCategory;
        existingCategoryNames.Add(categoryName);

        return newCategory;
    }
}

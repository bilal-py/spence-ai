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

        // Extract pages individually for filtering
        var pages = await _pdfExtractionService.ExtractPagesFromPdfAsync(pdfStream);

        // Filter pages that likely contain transaction data
        var transactionPages = FilterTransactionPages(pages);

        // Combine only the relevant pages (simple concatenation to match original behavior)
        var rawText = string.Concat(transactionPages);
        //rawText = SanitizeStatementText(rawText);
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

    /// <summary>
    /// Filters PDF pages to keep only those likely containing transaction data.
    /// Looks for common transaction-related keywords and abbreviations.
    /// </summary>
    private List<string> FilterTransactionPages(List<string> pages)
    {
        var transactionKeywords = new[]
        {
            "amount", "trans", "date", "debit", "credit", "balance",
            "amt", "txn", "dt", "dr", "cr", "bal",
            "withdrawal", "deposit", "payment", "charge", "fee",
            "desc", "description", "merchant", "payee",
            "total", "sum", "value", "cost",
            "day", "when", "posted", "effective",
            "remaining", "outstanding", "available",
            "statement", "account", "activity", "history",
            "check", "transfer", "ach", "wire",
            "dir", "dep", "atm", "pos", "online",
            "purchase", "sale", "income", "expense"
        };
        var filteredPages = new List<string>();

        foreach (var page in pages)
        {
            if (string.IsNullOrWhiteSpace(page))
                continue;

            var lowerCasePage = page.ToLower();
            foreach (var keyword in transactionKeywords)
            {
                if (lowerCasePage.Contains(keyword))
                {
                    filteredPages.Add(page);
                    break; // Found at least one keyword, keep this page
                }
            }
        }

        // If no pages matched keywords (unlikely but possible), fall back to all pages
        return filteredPages.Any() ? filteredPages : pages;
    }

    private string SanitizeStatementText(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText)) return rawText;

        // Cut off the document the moment the generic legal terms start
        string[] cutOffPhrases = {
            "IMPORTANT MESSAGES",
            "MOST IMPORTANT TERMS AND CONDITIONS",
            "IMPORTANT INFORMATION ON YOUR CREDIT CARD"
        };

        //foreach (var phrase in cutOffPhrases)
        //{
        //    int index = rawText.IndexOf(phrase, StringComparison.OrdinalIgnoreCase);
        //    if (index > 0)
        //    {
        //        rawText = rawText.Substring(0, index);
        //    }
        //}

        return rawText;
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

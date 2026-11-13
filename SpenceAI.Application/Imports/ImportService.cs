using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SpenceAI.Application.Categories;
using SpenceAI.Application.Settings;
using SpenceAI.Domain;

namespace SpenceAI.Application.Imports;

/// <summary>
/// Upload → (background) extract → normalize → dedup → categorize (rules over AI) → stage for review → commit.
/// Each upload is an <see cref="ImportBatch"/>; committing creates transactions linked to it, so an import can be undone.
/// </summary>
public class ImportService(
    IAppDbContext db,
    IImportFileStore files,
    IImportQueue queue,
    IPdfTextExtractor pdf,
    TransactionExtractor extractor,
    SettingsService settings,
    ILogger<ImportService> logger)
{
    public const long MaxFileBytes = 20 * 1024 * 1024;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly Dictionary<string, (TransactionSource Source, string MediaType)> Supported = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = (TransactionSource.Pdf, "application/pdf"),
        [".csv"] = (TransactionSource.Csv, "text/csv"),
        [".txt"] = (TransactionSource.Csv, "text/plain"),
        [".png"] = (TransactionSource.Image, "image/png"),
        [".jpg"] = (TransactionSource.Image, "image/jpeg"),
        [".jpeg"] = (TransactionSource.Image, "image/jpeg"),
        [".webp"] = (TransactionSource.Image, "image/webp"),
    };

    public static IEnumerable<string> SupportedExtensions => Supported.Keys;

    // ---------- Upload ----------

    public async Task<ImportBatchDto> CreateAsync(string fileName, long length, Stream content, CancellationToken ct)
    {
        if (length == 0) throw new AppException("The file is empty.");
        if (length > MaxFileBytes) throw new AppException("Files must be 20 MB or smaller.");
        if (!Supported.TryGetValue(Path.GetExtension(fileName), out var kind))
        {
            throw new AppException("Upload a PDF statement, a CSV export, or a photo (PNG, JPG, WEBP).");
        }

        var batch = new ImportBatch { FileName = Path.GetFileName(fileName), Source = kind.Source };
        await files.SaveAsync(batch.Id, content, ct);

        db.ImportBatches.Add(batch);
        await db.SaveChangesAsync(ct);
        await queue.EnqueueAsync(batch.Id, ct);

        return ToDto(batch, includeRows: false);
    }

    // ---------- Background processing ----------

    public async Task ProcessAsync(Guid batchId, CancellationToken ct)
    {
        var batch = await db.ImportBatches.FirstOrDefaultAsync(b => b.Id == batchId, ct);
        if (batch is null || batch.Status != ImportStatus.Queued) return;

        batch.Status = ImportStatus.Processing;
        await db.SaveChangesAsync(ct);

        AiConnection? connection = null;
        try
        {
            connection = await settings.GetConnectionAsync(ct);
            var client = await settings.CreateChatClientAsync(ct);
            var categories = await db.Categories.AsNoTracking().ToListAsync(ct);
            var categoryNames = categories.Select(c => c.Name).ToList();
            var bytes = await files.ReadAsync(batchId, ct);

            List<ExtractedTransaction> extracted;
            switch (batch.Source)
            {
                case TransactionSource.Pdf:
                    var pages = pdf.ExtractPages(new MemoryStream(bytes));
                    if (pages.All(string.IsNullOrWhiteSpace))
                    {
                        throw new AppException("This PDF has no readable text (it's probably a scan). Upload a photo or screenshot of it instead.");
                    }
                    extracted = await extractor.FromTextChunksAsync(client, TransactionExtractor.ChunkPages(pages), categoryNames, ct);
                    break;

                case TransactionSource.Csv:
                    var csv = Encoding.UTF8.GetString(bytes);
                    extracted = await extractor.FromTextChunksAsync(client, TransactionExtractor.ChunkCsv(csv), categoryNames, ct);
                    break;

                default:
                    var mediaType = Supported[Path.GetExtension(batch.FileName)].MediaType;
                    extracted = await extractor.FromImageAsync(client, bytes, mediaType, categoryNames, ct);
                    break;
            }

            var rows = await StageAsync(extracted, categories, ct);
            batch.StagedRowsJson = JsonSerializer.Serialize(rows, Json);
            batch.TransactionCount = rows.Count;
            batch.Status = ImportStatus.ReadyForReview;
            batch.CompletedAt = DateTime.UtcNow;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Import {BatchId} failed", batchId);
            batch.Status = ImportStatus.Failed;
            batch.Error = ex is AppException || connection is null
                ? ex.Message
                : SettingsService.DescribeAiFailure(connection, ex);
            batch.CompletedAt = DateTime.UtcNow;
        }
        finally
        {
            files.Delete(batchId);
        }

        await db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<List<StagedRow>> StageAsync(List<ExtractedTransaction> extracted, List<Category> categories, CancellationToken ct)
    {
        var byName = categories.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var rules = await db.MerchantRules.AsNoTracking().ToListAsync(ct);

        var dates = TransactionExtractor.ResolveDates(extracted);
        var parsed = extracted
            .Select((e, i) => (Item: e, Date: dates[i], Type: TransactionExtractor.ParseType(e.Type), Amount: Math.Abs(decimal.Round(e.Amount, 2))))
            .Where(p => p.Amount != 0 && p.Date is not null)
            .Select(p => (p.Item, Date: p.Date!.Value, p.Type, p.Amount))
            .ToList();

        // Fingerprints of transactions already saved in the same date range → duplicates.
        var existing = new HashSet<string>();
        if (parsed.Count > 0)
        {
            var min = parsed.Min(p => p.Date);
            var max = parsed.Max(p => p.Date);
            existing = (await db.Transactions.AsNoTracking()
                    .Where(t => t.Date >= min && t.Date <= max)
                    .Select(t => t.Fingerprint)
                    .ToListAsync(ct))
                .ToHashSet();
        }

        var rows = new List<StagedRow>(parsed.Count);
        foreach (var (item, date, type, amount) in parsed.OrderBy(p => p.Date))
        {
            var description = item.Description.Trim();
            var merchant = string.IsNullOrWhiteSpace(item.Merchant) ? null : item.Merchant.Trim();

            int? categoryId = null;
            string? newCategory = null;
            if (!string.IsNullOrWhiteSpace(item.Category))
            {
                if (byName.TryGetValue(item.Category.Trim(), out var match)) categoryId = match.Id;
                else newCategory = item.Category.Trim();
            }

            var key = MerchantKey.From(merchant ?? description);
            var rule = rules.FirstOrDefault(r => MerchantKey.Matches(key, r.Pattern));
            if (rule is not null)
            {
                categoryId = rule.CategoryId;
                newCategory = null;
            }

            var isDuplicate = existing.Contains(TransactionFingerprint.Compute(date, amount, type, description));

            rows.Add(new StagedRow(rows.Count, date, amount, type, description, merchant, categoryId, newCategory,
                isDuplicate, Include: !isDuplicate, MatchedRule: rule?.Pattern));
        }

        return rows;
    }

    // ---------- Review / commit / undo ----------

    public async Task<IReadOnlyList<ImportBatchDto>> ListAsync(CancellationToken ct) =>
        (await db.ImportBatches.AsNoTracking()
            .Where(b => b.Status != ImportStatus.Discarded)
            .OrderByDescending(b => b.CreatedAt)
            .Take(50)
            .ToListAsync(ct))
        .Select(b => ToDto(b, includeRows: false))
        .ToList();

    public async Task<ImportBatchDto> GetAsync(Guid id, CancellationToken ct) =>
        ToDto(await FindAsync(id, ct), includeRows: true);

    public async Task<ImportBatchDto> CommitAsync(Guid id, CommitImportRequest request, CancellationToken ct)
    {
        var batch = await FindAsync(id, ct);
        if (batch.Status != ImportStatus.ReadyForReview) throw new AppException("This import isn't waiting for review.");
        if (request.Rows.Count == 0) throw new AppException("Select at least one transaction to save, or discard the import.");

        var staged = Deserialize(batch.StagedRowsJson);
        var categories = await db.Categories.ToListAsync(ct);
        var byName = categories.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var validIds = categories.Select(c => c.Id).ToHashSet();

        foreach (var row in request.Rows)
        {
            if (row.Amount <= 0) throw new AppException($"\"{row.Description}\" needs an amount above zero.");
            if (string.IsNullOrWhiteSpace(row.Description)) throw new AppException("Every transaction needs a description.");

            var category = ResolveCategory(row.CategoryId, row.NewCategoryName, row.Type, byName, validIds, categories);
            var amount = decimal.Round(row.Amount, 2);

            db.Transactions.Add(new Transaction
            {
                Date = row.Date,
                Amount = amount,
                Type = row.Type,
                Description = row.Description.Trim(),
                Merchant = string.IsNullOrWhiteSpace(row.Merchant) ? null : row.Merchant.Trim(),
                Category = category,
                Source = batch.Source,
                ImportBatchId = batch.Id,
                Fingerprint = TransactionFingerprint.Compute(row.Date, amount, row.Type, row.Description.Trim()),
            });
        }

        await LearnRulesAsync(staged, request.Rows, byName, ct);

        batch.Status = ImportStatus.Committed;
        batch.TransactionCount = request.Rows.Count;
        batch.StagedRowsJson = null;
        batch.CompletedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return ToDto(batch, includeRows: false);
    }

    /// <summary>Discards a pending import, or undoes a committed one by deleting the transactions it created.</summary>
    public async Task DiscardAsync(Guid id, CancellationToken ct)
    {
        var batch = await FindAsync(id, ct);
        if (batch.Status is ImportStatus.Queued or ImportStatus.Processing)
        {
            throw new AppException("This import is still being read. Wait for it to finish, then discard it.");
        }

        if (batch.Status == ImportStatus.Committed)
        {
            var created = await db.Transactions.Where(t => t.ImportBatchId == id).ToListAsync(ct);
            db.Transactions.RemoveRange(created);
        }

        batch.Status = ImportStatus.Discarded;
        batch.StagedRowsJson = null;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// When the user picks a different category than the one suggested, remember it for that merchant.
    /// Rows are matched to staged rows by description.
    /// </summary>
    private async Task LearnRulesAsync(List<StagedRow> staged, IReadOnlyList<CommitRow> committed, Dictionary<string, Category> byName, CancellationToken ct)
    {
        var suggestions = staged
            .GroupBy(s => s.Description, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var rules = await db.MerchantRules.ToListAsync(ct);

        var byId = byName.Values.Where(c => c.Id > 0).ToDictionary(c => c.Id);

        foreach (var row in committed)
        {
            if (!suggestions.TryGetValue(row.Description.Trim(), out var suggested)) continue;

            Category? chosen = row.CategoryId is int id
                ? byId.GetValueOrDefault(id)
                : row.NewCategoryName is { } name ? byName.GetValueOrDefault(name.Trim()) : null;
            if (chosen is null) continue;

            var keptSuggestion = chosen.Id > 0
                ? chosen.Id == suggested.CategoryId
                : string.Equals(chosen.Name, suggested.NewCategoryName, StringComparison.OrdinalIgnoreCase);
            if (keptSuggestion) continue;

            var key = MerchantKey.From(row.Merchant ?? row.Description);
            if (key.Length == 0) continue;

            var rule = rules.FirstOrDefault(r => r.Pattern == key);
            if (rule is null)
            {
                rule = new MerchantRule { Pattern = key };
                db.MerchantRules.Add(rule);
                rules.Add(rule);
            }

            rule.Category = chosen;
        }
    }

    private Category? ResolveCategory(int? categoryId, string? newName, TransactionType type,
        Dictionary<string, Category> byName, HashSet<int> validIds, List<Category> all)
    {
        if (categoryId is not null)
        {
            if (!validIds.Contains(categoryId.Value)) throw new AppException("One of the selected categories no longer exists.");
            return all.First(c => c.Id == categoryId.Value);
        }

        if (string.IsNullOrWhiteSpace(newName)) return null;

        var name = newName.Trim();
        if (byName.TryGetValue(name, out var existing)) return existing;

        var created = new Category
        {
            Name = name,
            Color = CategoryTemplates.NextColor(all.Count),
            Kind = type == TransactionType.Income ? TransactionType.Income : TransactionType.Expense,
        };
        db.Categories.Add(created);
        all.Add(created);
        byName[name] = created;
        return created;
    }

    private async Task<ImportBatch> FindAsync(Guid id, CancellationToken ct) =>
        await db.ImportBatches.FirstOrDefaultAsync(b => b.Id == id, ct)
        ?? throw new AppException("Import not found.", 404);

    private static List<StagedRow> Deserialize(string? json) =>
        string.IsNullOrEmpty(json) ? [] : JsonSerializer.Deserialize<List<StagedRow>>(json, Json) ?? [];

    private static ImportBatchDto ToDto(ImportBatch b, bool includeRows) =>
        new(b.Id, b.FileName, b.Source, b.Status, b.Error, b.TransactionCount, b.CreatedAt, b.CompletedAt,
            includeRows ? Deserialize(b.StagedRowsJson) : null);
}

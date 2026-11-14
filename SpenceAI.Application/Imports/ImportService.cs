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

    /// <summary>
    /// Stores the upload for the background worker. PDFs are read to text right here, so a locked PDF can ask for its
    /// password up front and only the extracted text (never the PDF or the password) touches the disk.
    /// </summary>
    public async Task<ImportBatchDto> CreateAsync(string fileName, long length, Stream content, CancellationToken ct, string? password = null)
    {
        if (length == 0) throw new AppException("The file is empty.");
        if (length > MaxFileBytes) throw new AppException("Files must be 20 MB or smaller.");
        if (!Supported.TryGetValue(Path.GetExtension(fileName), out var kind))
        {
            throw new AppException("Upload a PDF statement, a CSV export, or a photo (PNG, JPG, WEBP).");
        }

        var batch = new ImportBatch { FileName = Path.GetFileName(fileName), Source = kind.Source };

        if (kind.Source == TransactionSource.Pdf)
        {
            var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, ct);
            buffer.Position = 0;
            var pages = pdf.ExtractPages(buffer, password);
            if (pages.All(string.IsNullOrWhiteSpace))
            {
                throw new AppException("This PDF has no readable text (it's probably a scan). Upload a photo or screenshot of it instead.");
            }

            await files.SaveAsync(batch.Id, new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(pages, Json)), ct);
        }
        else
        {
            await files.SaveAsync(batch.Id, content, ct);
        }

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
            var bytes = await files.ReadAsync(batchId, ct);

            List<ExtractedWithSource> extracted;
            switch (batch.Source)
            {
                case TransactionSource.Pdf:
                    extracted = await extractor.FromTextChunksAsync(client, TransactionExtractor.ChunkPages(ReadPages(bytes)), categories, ct);
                    break;

                case TransactionSource.Csv:
                    var csv = Encoding.UTF8.GetString(bytes);
                    extracted = await extractor.FromTextChunksAsync(client, TransactionExtractor.ChunkCsv(csv), categories, ct);
                    break;

                default:
                    var mediaType = Supported[Path.GetExtension(batch.FileName)].MediaType;
                    extracted = await extractor.FromImageAsync(client, bytes, mediaType, categories, ct);
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

    /// <summary>PDF batches store extracted page text as JSON; batches queued before that change still hold the raw PDF.</summary>
    private IReadOnlyList<string> ReadPages(byte[] bytes) =>
        bytes.AsSpan().StartsWith("%PDF"u8)
            ? pdf.ExtractPages(new MemoryStream(bytes))
            : JsonSerializer.Deserialize<List<string>>(bytes, Json) ?? [];

    private async Task<List<StagedRow>> StageAsync(List<ExtractedWithSource> extracted, List<Category> categories, CancellationToken ct)
    {
        var byName = categories.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var byId = categories.ToDictionary(c => c.Id);
        var rules = await db.MerchantRules.AsNoTracking().ToListAsync(ct);

        var dates = TransactionExtractor.ResolveDates(extracted.Select(e => e.Item).ToList());
        var parsed = extracted
            .Select((e, i) => (e.Item, e.SourceText, Date: dates[i], Type: TransactionExtractor.ParseType(e.Item.Type), Amount: Math.Abs(decimal.Round(e.Item.Amount, 2))))
            .Where(p => p.Amount != 0 && p.Date is not null)
            .Select(p => (p.Item, p.SourceText, Date: p.Date!.Value, p.Type, p.Amount))
            .ToList();

        // Transactions already saved in the same date range, keyed by date + amount + description (not type: a row
        // saved as an expense and re-imported as a transfer is still the same money). Each saved transaction can
        // match only one staged row, so two genuinely identical charges on a statement aren't both flagged.
        var existing = new Dictionary<string, Queue<Transaction>>();
        if (parsed.Count > 0)
        {
            var min = parsed.Min(p => p.Date);
            var max = parsed.Max(p => p.Date);
            var saved = await db.Transactions.AsNoTracking()
                .Include(t => t.ImportBatch)
                .Where(t => t.Date >= min && t.Date <= max)
                .ToListAsync(ct);
            foreach (var t in saved.OrderBy(t => t.Id))
            {
                var k = DuplicateKey(t.Date, t.Amount, t.Description);
                if (!existing.TryGetValue(k, out var queue)) existing[k] = queue = new Queue<Transaction>();
                queue.Enqueue(t);
            }
        }

        var rows = new List<StagedRow>(parsed.Count);
        foreach (var (item, sourceText, date, aiType, amount) in parsed.OrderBy(p => p.Date))
        {
            var type = aiType;
            var description = item.Description.Trim();
            var merchant = string.IsNullOrWhiteSpace(item.Merchant) ? null : item.Merchant.Trim();

            // The printed statement is the ground truth for the amount and its sign.
            string? warning = null;
            var evidence = sourceText is null ? AmountEvidence.Found : AmountCheck.Check(sourceText, amount, description);
            if (evidence == AmountEvidence.NotFound) warning = "Amount isn't on the statement";
            else if (evidence == AmountEvidence.FoundAsCredit && type == TransactionType.Expense) type = TransactionType.Refund;

            int? categoryId = null;
            string? newCategory = null;
            if (!string.IsNullOrWhiteSpace(item.Category))
            {
                if (byName.TryGetValue(item.Category.Trim(), out var match)) categoryId = match.Id;
                else newCategory = item.Category.Trim();
            }

            var key = MerchantKey.From(merchant ?? description);
            var rule = rules.FirstOrDefault(r => MerchantKey.Matches(key, r.Pattern));
            if (rule?.Type is { } ruleType) type = ruleType;
            if (rule?.CategoryId is { } ruleCategory)
            {
                categoryId = ruleCategory;
                newCategory = null;
            }

            if (type == TransactionType.Transfer)
            {
                // Transfers move money between the user's own accounts; they belong to no spending or income category.
                categoryId = null;
                newCategory = null;
            }
            else if (type == TransactionType.Refund && categoryId is int id && byId.GetValueOrDefault(id)?.Kind == TransactionType.Income)
            {
                // A refund offsets spending, so it needs a spending category; leave it for the user to pick.
                categoryId = null;
            }

            string? duplicateOf = null;
            if (existing.TryGetValue(DuplicateKey(date, amount, description), out var matches) && matches.TryDequeue(out var saved))
            {
                duplicateOf = saved.ImportBatch is { } from
                    ? $"Already saved from an import on {from.CreatedAt.ToLocalTime():d MMM}"
                    : "Already saved (added by hand)";
            }

            var isDuplicate = duplicateOf is not null;
            rows.Add(new StagedRow(rows.Count, date, amount, type, description, merchant, categoryId, newCategory,
                isDuplicate, Include: !isDuplicate && warning is null, MatchedRule: rule?.Pattern, Warning: warning, DuplicateOf: duplicateOf));
        }

        return rows;
    }

    private static string DuplicateKey(DateOnly date, decimal amount, string description) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"{date:yyyyMMdd}|{decimal.Round(amount, 2):0.00}|{MerchantKey.From(description, maxTokens: 6)}");

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
    /// Remembers corrections per merchant: a different category than the one suggested, or marking a row as a transfer
    /// (e.g. a card bill payment). Other type changes aren't learned, because refunds and purchases share merchants.
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

            var key = MerchantKey.From(row.Merchant ?? row.Description);
            if (key.Length == 0) continue;
            var rule = rules.FirstOrDefault(r => r.Pattern == key);

            // Type: learn "this merchant is a transfer"; forget it when the user says otherwise.
            var learnTransfer = row.Type != suggested.Type && row.Type == TransactionType.Transfer;
            var forgetType = row.Type != suggested.Type && row.Type != TransactionType.Transfer && rule?.Type is not null;

            // Category: learn it when the user picked something other than the suggestion.
            Category? chosen = row.CategoryId is int id
                ? byId.GetValueOrDefault(id)
                : row.NewCategoryName is { } name ? byName.GetValueOrDefault(name.Trim()) : null;
            var learnCategory = chosen is not null && row.Type != TransactionType.Transfer && (chosen.Id > 0
                ? chosen.Id != suggested.CategoryId
                : !string.Equals(chosen.Name, suggested.NewCategoryName, StringComparison.OrdinalIgnoreCase));

            if (!learnTransfer && !forgetType && !learnCategory) continue;

            if (rule is null)
            {
                rule = new MerchantRule { Pattern = key };
                db.MerchantRules.Add(rule);
                rules.Add(rule);
            }

            if (learnTransfer) rule.Type = TransactionType.Transfer;
            if (forgetType) rule.Type = null;
            if (learnCategory) rule.Category = chosen;
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

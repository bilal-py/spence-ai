using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SpenceAI.Application;
using SpenceAI.Application.Analytics;
using SpenceAI.Application.Imports;
using SpenceAI.Domain;

namespace SpenceAI.WebApi.Endpoints;

public record TransactionDto(
    int Id, DateOnly Date, decimal Amount, TransactionType Type, string Description, string? Merchant, string? Notes,
    int? CategoryId, string? CategoryName, string? CategoryColor, string? CategoryIcon,
    TransactionSource Source, Guid? ImportBatchId);

public record TransactionPage(IReadOnlyList<TransactionDto> Items, int Total, int Page, int PageSize, decimal TotalExpense, decimal TotalIncome);

public record UpsertTransactionRequest(
    DateOnly Date, decimal Amount, TransactionType Type, string Description, string? Merchant, string? Notes,
    int? CategoryId, bool RememberCategory = false);

public record BulkCategorizeRequest(IReadOnlyList<int> Ids, int? CategoryId);
public record BulkDeleteRequest(IReadOnlyList<int> Ids);

public static class TransactionEndpoints
{
    public static void MapTransactionEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/transactions").WithTags("Transactions");

        g.MapGet("/", List);
        g.MapGet("/export", Export);
        g.MapPost("/", Create);
        g.MapPut("/{id:int}", Update);
        g.MapDelete("/{id:int}", Delete);
        g.MapPost("/bulk/categorize", BulkCategorize);
        g.MapPost("/bulk/delete", BulkDelete);
    }

    /// <param name="categoryId">0 means "uncategorized".</param>
    private static async Task<TransactionPage> List(
        IAppDbContext db, CancellationToken ct,
        DateOnly? from = null, DateOnly? to = null, int? categoryId = null, TransactionType? type = null,
        string? search = null, string sort = "date", string dir = "desc", int page = 1, int pageSize = 50)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 500);

        var items = await Filter(db, from, to, categoryId, type, search).ToListAsync(ct);

        // Sorting happens in memory: SQLite can't ORDER BY decimal, and a single user's data is small.
        IEnumerable<Transaction> sorted = (sort, dir) switch
        {
            ("amount", "asc") => items.OrderBy(t => t.Amount),
            ("amount", _) => items.OrderByDescending(t => t.Amount),
            ("date", "asc") => items.OrderBy(t => t.Date).ThenBy(t => t.Id),
            _ => items.OrderByDescending(t => t.Date).ThenByDescending(t => t.Id),
        };

        return new TransactionPage(
            sorted.Skip((page - 1) * pageSize).Take(pageSize).Select(ToDto).ToList(),
            items.Count, page, pageSize,
            AnalyticsService.NetSpent(items),
            items.Where(t => t.Type == TransactionType.Income).Sum(t => t.Amount));
    }

    private static async Task<IResult> Export(
        IAppDbContext db, CancellationToken ct,
        DateOnly? from = null, DateOnly? to = null, int? categoryId = null, TransactionType? type = null, string? search = null)
    {
        var items = (await Filter(db, from, to, categoryId, type, search).ToListAsync(ct))
            .OrderBy(t => t.Date).ThenBy(t => t.Id);

        var csv = new StringBuilder("Date,Type,Amount,Description,Merchant,Category,Notes,Source\n");
        foreach (var t in items)
        {
            csv.AppendJoin(',',
                t.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), t.Type,
                t.Amount.ToString(CultureInfo.InvariantCulture),
                Esc(t.Description), Esc(t.Merchant), Esc(t.Category?.Name), Esc(t.Notes), t.Source).Append('\n');
        }

        return Results.File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"spence-transactions-{DateTime.Now:yyyyMMdd}.csv");

        static string Esc(string? v) => v is null ? "" : $"\"{v.Replace("\"", "\"\"")}\"";
    }

    private static async Task<TransactionDto> Create(UpsertTransactionRequest req, IAppDbContext db, CancellationToken ct)
    {
        var tx = new Transaction { Source = TransactionSource.Manual };
        await Apply(tx, req, db, ct);
        db.Transactions.Add(tx);
        await db.SaveChangesAsync(ct);
        return await Load(db, tx.Id, ct);
    }

    private static async Task<TransactionDto> Update(int id, UpsertTransactionRequest req, IAppDbContext db, CancellationToken ct)
    {
        var tx = await db.Transactions.FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw new AppException("Transaction not found.", 404);
        await Apply(tx, req, db, ct);
        tx.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return await Load(db, tx.Id, ct);
    }

    private static async Task<IResult> Delete(int id, IAppDbContext db, CancellationToken ct)
    {
        var deleted = await db.Transactions.Where(t => t.Id == id).ExecuteDeleteAsync(ct);
        return deleted == 0 ? throw new AppException("Transaction not found.", 404) : Results.NoContent();
    }

    private static async Task<IResult> BulkCategorize(BulkCategorizeRequest req, IAppDbContext db, CancellationToken ct)
    {
        if (req.CategoryId is int cid && !await db.Categories.AnyAsync(c => c.Id == cid, ct)) throw new AppException("Category not found.", 404);
        var now = DateTime.UtcNow;
        var count = await db.Transactions.Where(t => req.Ids.Contains(t.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.CategoryId, req.CategoryId).SetProperty(t => t.UpdatedAt, now), ct);
        return Results.Ok(new { updated = count });
    }

    private static async Task<IResult> BulkDelete(BulkDeleteRequest req, IAppDbContext db, CancellationToken ct)
    {
        var count = await db.Transactions.Where(t => req.Ids.Contains(t.Id)).ExecuteDeleteAsync(ct);
        return Results.Ok(new { deleted = count });
    }

    private static IQueryable<Transaction> Filter(IAppDbContext db, DateOnly? from, DateOnly? to, int? categoryId, TransactionType? type, string? search)
    {
        var q = db.Transactions.AsNoTracking().Include(t => t.Category).AsQueryable();
        if (from is not null) q = q.Where(t => t.Date >= from);
        if (to is not null) q = q.Where(t => t.Date <= to);
        if (categoryId == 0) q = q.Where(t => t.CategoryId == null);
        else if (categoryId is not null) q = q.Where(t => t.CategoryId == categoryId);
        if (type is not null) q = q.Where(t => t.Type == type);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = $"%{search.Trim()}%";
            q = q.Where(t => EF.Functions.Like(t.Description, s) || EF.Functions.Like(t.Merchant!, s) || EF.Functions.Like(t.Notes!, s));
        }

        return q;
    }

    private static async Task Apply(Transaction tx, UpsertTransactionRequest req, IAppDbContext db, CancellationToken ct)
    {
        if (req.Amount <= 0) throw new AppException("Amount must be above zero.");
        if (string.IsNullOrWhiteSpace(req.Description)) throw new AppException("Add a description.");
        if (req.CategoryId is int cid && !await db.Categories.AnyAsync(c => c.Id == cid, ct)) throw new AppException("Category not found.", 404);

        tx.Date = req.Date;
        tx.Amount = decimal.Round(req.Amount, 2);
        tx.Type = req.Type;
        tx.Description = req.Description.Trim();
        tx.Merchant = string.IsNullOrWhiteSpace(req.Merchant) ? null : req.Merchant.Trim();
        tx.Notes = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes.Trim();
        tx.CategoryId = req.CategoryId;
        tx.Fingerprint = TransactionFingerprint.Compute(tx.Date, tx.Amount, tx.Type, tx.Description);

        if (req.RememberCategory && req.CategoryId is int categoryId)
        {
            var key = RuleBook.Keys(tx.Merchant, tx.Description).General;
            var direction = RuleBook.DirectionOf(tx.Type);
            if (key.Length > 0)
            {
                var rule = await db.MerchantRules.FirstOrDefaultAsync(r => r.Pattern == key && r.Direction == direction, ct);
                if (rule is null) db.MerchantRules.Add(new MerchantRule { Pattern = key, Direction = direction, CategoryId = categoryId });
                else
                {
                    rule.CategoryId = categoryId;
                    rule.UpdatedAt = DateTime.UtcNow;
                }
            }
        }
    }

    private static async Task<TransactionDto> Load(IAppDbContext db, int id, CancellationToken ct) =>
        ToDto(await db.Transactions.AsNoTracking().Include(t => t.Category).FirstAsync(t => t.Id == id, ct));

    private static TransactionDto ToDto(Transaction t) =>
        new(t.Id, t.Date, t.Amount, t.Type, t.Description, t.Merchant, t.Notes,
            t.CategoryId, t.Category?.Name, t.Category?.Color, t.Category?.Icon, t.Source, t.ImportBatchId);
}

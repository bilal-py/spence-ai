using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SpenceAI.Application;
using SpenceAI.Domain;

namespace SpenceAI.WebApi.Endpoints;

public record AccountDto(int Id, string Name, AccountKind Kind, string? Last4, string Color, bool IsArchived, int TransactionCount);
public record UpsertAccountRequest(string Name, AccountKind Kind, string? Last4, string Color, bool IsArchived = false);

public static partial class AccountEndpoints
{
    public static void MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/accounts").WithTags("Accounts");
        g.MapGet("/", List);
        g.MapPost("/", Create);
        g.MapPut("/{id:int}", Update);
        g.MapDelete("/{id:int}", Delete);
    }

    private static async Task<List<AccountDto>> List(IAppDbContext db, CancellationToken ct) =>
        await db.Accounts.AsNoTracking()
            .OrderBy(a => a.IsArchived).ThenBy(a => a.Name)
            .Select(a => new AccountDto(a.Id, a.Name, a.Kind, a.Last4, a.Color, a.IsArchived, a.Transactions.Count))
            .ToListAsync(ct);

    private static async Task<AccountDto> Create(UpsertAccountRequest req, IAppDbContext db, CancellationToken ct)
    {
        var account = new Account();
        await Apply(account, req, db, ct);
        db.Accounts.Add(account);
        await db.SaveChangesAsync(ct);
        return ToDto(account, 0);
    }

    private static async Task<AccountDto> Update(int id, UpsertAccountRequest req, IAppDbContext db, CancellationToken ct)
    {
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw new AppException("Account not found.", 404);
        await Apply(account, req, db, ct);
        await db.SaveChangesAsync(ct);
        return ToDto(account, await db.Transactions.CountAsync(t => t.AccountId == id, ct));
    }

    /// <summary>Its transactions are kept, just no longer tagged with an account.</summary>
    private static async Task<IResult> Delete(int id, IAppDbContext db, CancellationToken ct)
    {
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw new AppException("Account not found.", 404);
        await db.Transactions.Where(t => t.AccountId == id).ExecuteUpdateAsync(s => s.SetProperty(t => t.AccountId, (int?)null), ct);
        db.Accounts.Remove(account);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task Apply(Account a, UpsertAccountRequest req, IAppDbContext db, CancellationToken ct)
    {
        var name = req.Name?.Trim() ?? "";
        if (name.Length is 0 or > 60) throw new AppException("Account names need 1 to 60 characters.");
        if (!HexColor().IsMatch(req.Color ?? "")) throw new AppException("Pick a color like #3f8f5f.");
        var last4 = string.IsNullOrWhiteSpace(req.Last4) ? null : req.Last4.Trim();
        if (last4 is not null && !Last4Digits().IsMatch(last4)) throw new AppException("Last digits should be 2 to 4 numbers.");
        if (await db.Accounts.AnyAsync(x => x.Id != a.Id && x.Name.ToLower() == name.ToLower(), ct))
        {
            throw new AppException($"You already have an account called \"{name}\".");
        }

        a.Name = name;
        a.Kind = req.Kind;
        a.Last4 = last4;
        a.Color = req.Color!;
        a.IsArchived = req.IsArchived;
    }

    private static AccountDto ToDto(Account a, int count) => new(a.Id, a.Name, a.Kind, a.Last4, a.Color, a.IsArchived, count);

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColor();

    [GeneratedRegex("^[0-9]{2,4}$")]
    private static partial Regex Last4Digits();
}

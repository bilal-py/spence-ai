namespace SpenceAI.Domain;

public enum TransactionType
{
    Expense,
    Income,
    Transfer,
    /// <summary>Money given back for earlier spending (refund, reversal, cashback, fee waiver). Offsets spending in its category.</summary>
    Refund,
}

public enum TransactionSource
{
    Manual,
    Pdf,
    Image,
    Csv,
}

public enum ImportStatus
{
    Queued,
    Processing,
    ReadyForReview,
    Committed,
    Failed,
    Discarded,
}

/// <summary>What kind of place money sits in. Quick add maps words like "cash", "card" or "upi" to an account through this.</summary>
public enum AccountKind
{
    Bank,
    Card,
    Cash,
    Wallet,
}

public enum AiProvider
{
    Ollama,
    Gemini,
    OpenAI,
}

/// <summary>A single money movement. Amount is always positive; direction comes from <see cref="Type"/>.</summary>
public class Transaction
{
    public int Id { get; set; }
    public DateOnly Date { get; set; }
    public decimal Amount { get; set; }
    public TransactionType Type { get; set; } = TransactionType.Expense;
    public string Description { get; set; } = string.Empty;
    public string? Merchant { get; set; }
    public string? Notes { get; set; }

    public int? CategoryId { get; set; }
    public Category? Category { get; set; }

    /// <summary>The bank account, card, wallet or cash the money moved through. Optional.</summary>
    public int? AccountId { get; set; }
    public Account? Account { get; set; }

    public TransactionSource Source { get; set; } = TransactionSource.Manual;
    public Guid? ImportBatchId { get; set; }
    public ImportBatch? ImportBatch { get; set; }

    /// <summary>Stable hash of date + amount + type + normalized description, used for duplicate detection.</summary>
    public string Fingerprint { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = "#64748b";
    /// <summary>Lucide icon name, e.g. "shopping-cart".</summary>
    public string Icon { get; set; } = "tag";
    public TransactionType Kind { get; set; } = TransactionType.Expense;
    /// <summary>Optional monthly spending limit for this category.</summary>
    public decimal? MonthlyBudget { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
}

/// <summary>A bank account, card, wallet or cash that transactions are tagged with.</summary>
public class Account
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public AccountKind Kind { get; set; } = AccountKind.Bank;
    /// <summary>Last digits of the card or account number, to tell similar accounts apart. Optional.</summary>
    public string? Last4 { get; set; }
    public string Color { get; set; } = "#64748b";
    /// <summary>Hidden from pickers but kept on old transactions.</summary>
    public bool IsArchived { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
}

/// <summary>Which way money moved. Expenses go out; income and refunds come in; a transfer can be either.</summary>
public enum MoneyDirection
{
    Out,
    In,
}

/// <summary>
/// Learned "merchant key → category and/or type" mapping, applied before (and over) the AI's guess.
/// A rule is scoped to one <see cref="Direction"/>, so "Amazon refunds are refunds" never touches Amazon purchases.
/// When several rules match, the one with the longest pattern wins, so a specific exception
/// ("AMAZON PAY BILLPAYMENT" → Transfer) beats the general rule ("AMAZON PAY" → Shopping).
/// </summary>
public class MerchantRule
{
    public int Id { get; set; }
    public string Pattern { get; set; } = string.Empty;
    /// <summary>Null matches both directions (rules created before directions existed).</summary>
    public MoneyDirection? Direction { get; set; }
    public int? CategoryId { get; set; }
    public Category? Category { get; set; }
    public TransactionType? Type { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>One uploaded file. Rows are staged as JSON until the user reviews and commits them.</summary>
public class ImportBatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FileName { get; set; } = string.Empty;
    public TransactionSource Source { get; set; }
    public ImportStatus Status { get; set; } = ImportStatus.Queued;
    public string? Error { get; set; }
    public string? StagedRowsJson { get; set; }
    public int TransactionCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
}

/// <summary>Single-row settings table.</summary>
public class AppSettings
{
    public int Id { get; set; }
    public AiProvider AiProvider { get; set; } = AiProvider.Ollama;
    public string AiModel { get; set; } = "llama3.2";
    public string OllamaUrl { get; set; } = "http://localhost:11434";
    /// <summary>API key protected with ASP.NET Data Protection (keys live in the local data directory).</summary>
    public string? ProtectedApiKey { get; set; }
    public string Currency { get; set; } = "USD";
    /// <summary>Take a backup once a day (kept in the data directory's backups folder).</summary>
    public bool AutoBackup { get; set; } = true;
    /// <summary>Optional extra folder (e.g. OneDrive) that every backup is also copied to.</summary>
    public string? BackupCopyFolder { get; set; }
    /// <summary>Show Sprig, the mascot, and its one nudge a day on the Overview.</summary>
    public bool PetEnabled { get; set; } = true;
    public string PetName { get; set; } = "Sprig";
    /// <summary>The day the user last dismissed a nudge; nothing more is shown until the next day.</summary>
    public DateOnly? PetNudgeDismissedOn { get; set; }
}

/// <summary>Where money in a savings pot came from.</summary>
public enum PotSource
{
    Manual,
    /// <summary>Not stored: computed live from Transfer transactions matching the pot's link pattern.</summary>
    Transfer,
    RoundUp,
    Leftover,
    NoSpend,
    Challenge,
}

public enum ChallengeKind
{
    /// <summary>No spending in one category between the start and end dates.</summary>
    SkipCategory,
    /// <summary>At least <see cref="PotChallenge.Target"/> days without any spending between the start and end dates.</summary>
    NoSpendDays,
}

/// <summary>
/// A savings goal. Money comes in by hand, from linked transfers (counted live), or from amounts the user claims:
/// round-ups, leftover budget, no-spend-day bonuses and won challenges. Claims move a "claimed through" marker so nothing counts twice.
/// </summary>
public class SavingsPot
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = "#1e5a46";
    public string Icon { get; set; } = "piggy-bank";
    public decimal TargetAmount { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? Deadline { get; set; }

    /// <summary>Transfers whose merchant or description contains this text count toward the pot, e.g. "RD" or "Groww".</summary>
    public string? LinkPattern { get; set; }

    /// <summary>Round each expense up to this step (10, 50, 100…) and offer the spare change. Only one pot collects round-ups.</summary>
    public int? RoundUpTo { get; set; }
    public DateOnly? RoundUpsClaimedThrough { get; set; }

    /// <summary>Offer what's left of the monthly budgets after each month ends. Only one pot collects leftovers.</summary>
    public bool CollectLeftover { get; set; }
    public DateOnly? LeftoverClaimedThrough { get; set; }

    /// <summary>A reward the user pays themselves for each day with no spending.</summary>
    public decimal? NoSpendBonus { get; set; }
    public DateOnly? NoSpendClaimedThrough { get; set; }

    public bool IsArchived { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<PotContribution> Contributions { get; set; } = new List<PotContribution>();
    public ICollection<PotChallenge> Challenges { get; set; } = new List<PotChallenge>();
}

/// <summary>Money put into (positive) or taken out of (negative) a pot.</summary>
public class PotContribution
{
    public int Id { get; set; }
    public int PotId { get; set; }
    public SavingsPot? Pot { get; set; }
    public DateOnly Date { get; set; }
    public decimal Amount { get; set; }
    public PotSource Source { get; set; } = PotSource.Manual;
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>A small, time-boxed challenge. Winning it offers <see cref="Reward"/> to move into the pot.</summary>
public class PotChallenge
{
    public int Id { get; set; }
    public int PotId { get; set; }
    public SavingsPot? Pot { get; set; }
    public ChallengeKind Kind { get; set; }
    public int? CategoryId { get; set; }
    public Category? Category { get; set; }
    /// <summary>For <see cref="ChallengeKind.NoSpendDays"/>: how many no-spend days to reach.</summary>
    public int? Target { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public decimal Reward { get; set; }
    public DateTime? ClaimedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

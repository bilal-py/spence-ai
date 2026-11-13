namespace SpenceAI.Domain;

public enum TransactionType
{
    Expense,
    Income,
    Transfer,
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

/// <summary>Learned "merchant key → category" mapping, applied before (and over) the AI's guess.</summary>
public class MerchantRule
{
    public int Id { get; set; }
    public string Pattern { get; set; } = string.Empty;
    public int CategoryId { get; set; }
    public Category? Category { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
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
}

namespace SpenceAI.Domain.Entities;

public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ColorCode { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsSynced { get; set; } = false;
    public string? CloudId { get; set; }
    public ICollection<Expense> Expenses { get; set; } = new List<Expense>();
}

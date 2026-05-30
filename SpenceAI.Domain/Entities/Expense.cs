namespace SpenceAI.Domain.Entities;

public class Expense
{
    public int Id { get; set; }
    public decimal Amount { get; set; }
    public DateTime Date { get; set; }
    public string Description { get; set; } = string.Empty;
    public int CategoryId { get; set; }
    public Category? Category { get; set; }
}

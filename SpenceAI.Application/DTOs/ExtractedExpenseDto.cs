namespace SpenceAI.Application.DTOs;

public record ExtractedExpenseDto(
    string Description,
    decimal Amount,
    DateTime Date,
    string CategoryName,
    bool IsNewCategory);

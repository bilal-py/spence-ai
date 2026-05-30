using Microsoft.AspNetCore.Mvc;
using SpenceAI.Application.Services;

namespace SpenceAI.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ExpensesController : ControllerBase
{
    private readonly ExpenseProcessingService _expenseProcessingService;

    public ExpensesController(ExpenseProcessingService expenseProcessingService)
    {
        _expenseProcessingService = expenseProcessingService;
    }

    [HttpPost("upload-pdf")]
    public async Task<IActionResult> UploadPdf(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new { message = "A PDF file is required." });
        }

        var extension = Path.GetExtension(file.FileName);
        if (!string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { message = "Only PDF files are accepted." });
        }

        await using var stream = file.OpenReadStream();
        await _expenseProcessingService.ProcessPdfUploadAsync(stream, cancellationToken);

        return Ok(new { message = "PDF processed successfully." });
    }
}

using Microsoft.AspNetCore.Mvc;
using SpenceAI.Application.Services;
using SpenceAI.Application.Common.Interfaces;

namespace SpenceAI.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ExpensesController : ControllerBase
{
    private readonly ExpenseProcessingService _expenseProcessingService;
    private readonly IExpenseRepository _expenseRepository;

    public ExpensesController(ExpenseProcessingService expenseProcessingService, IExpenseRepository expenseRepository)
    {
        _expenseProcessingService = expenseProcessingService;
        _expenseRepository = expenseRepository;
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

        try
        {
            using var memoryStream = new MemoryStream();
            await file.CopyToAsync(memoryStream, cancellationToken);
            memoryStream.Position = 0; // Reset position to the beginning

            await _expenseProcessingService.ProcessPdfUploadAsync(memoryStream, cancellationToken);
            return Ok(new { message = "PDF processed successfully." });
        }
        catch (Exception ex)
        {
            // Force the real error text out to the console/browser response
            return StatusCode(500, new { message = "An error occurred while processing the PDF.", details = ex.Message, stackTrace = ex.StackTrace });
        }
    }
}

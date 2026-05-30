using System.Text;
using SpenceAI.Application.Common.Interfaces;
using UglyToad.PdfPig;

namespace SpenceAI.Infrastructure.Services;

public class PdfExtractionService : IPdfExtractionService
{
    public Task<string> ExtractTextFromPdfAsync(Stream pdfStream)
    {
        ArgumentNullException.ThrowIfNull(pdfStream);

        using var document = PdfDocument.Open(pdfStream);
        var builder = new StringBuilder();

        foreach (var page in document.GetPages())
        {
            builder.Append(page.Text);
        }

        return Task.FromResult(builder.ToString());
    }
}

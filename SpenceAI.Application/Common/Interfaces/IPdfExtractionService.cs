namespace SpenceAI.Application.Common.Interfaces;

public interface IPdfExtractionService
{
    Task<string> ExtractTextFromPdfAsync(Stream pdfStream);
}

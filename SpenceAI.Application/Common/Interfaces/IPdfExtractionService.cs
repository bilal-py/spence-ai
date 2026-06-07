namespace SpenceAI.Application.Common.Interfaces;

public interface IPdfExtractionService
{
    Task<string> ExtractTextFromPdfAsync(Stream pdfStream);
    Task<List<string>> ExtractPagesFromPdfAsync(Stream pdfStream);
}

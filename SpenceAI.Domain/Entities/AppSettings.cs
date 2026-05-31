namespace SpenceAI.Domain.Entities;

public class AppSettings
{
    public int Id { get; set; }
    public string StorageMode { get; set; } = "Local";
    public string SelectedAiProvider { get; set; } = "Gemini";
    public string? EncryptedAiApiKey { get; set; }
}

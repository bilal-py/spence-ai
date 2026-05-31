using Microsoft.EntityFrameworkCore;
using SpenceAI.Application.Common.Interfaces;
using SpenceAI.Domain.Entities;

namespace SpenceAI.Application.Services;

public class SettingsService
{
    private readonly IApplicationDbContext _db;
    private readonly IEncryptionService _encryption;

    public SettingsService(IApplicationDbContext db, IEncryptionService encryption)
    {
        _db = db;
        _encryption = encryption;
    }

    public async Task<AppSettings> GetSettingsAsync()
    {
        var settings = await _db.AppSettings.FirstOrDefaultAsync();

        if (settings is not null)
        {
            return settings;
        }

        settings = new AppSettings();
        _db.AppSettings.Add(settings);
        await _db.SaveChangesAsync();

        return settings;
    }

    public async Task UpdateSettingsAsync(string mode, string provider, string? rawApiKey)
    {
        var settings = await GetSettingsAsync();

        settings.StorageMode = mode;
        settings.SelectedAiProvider = provider;

        if (rawApiKey is not null)
        {
            settings.EncryptedAiApiKey = string.IsNullOrWhiteSpace(rawApiKey)
                ? null
                : _encryption.Encrypt(rawApiKey);
        }

        await _db.SaveChangesAsync();
    }
}

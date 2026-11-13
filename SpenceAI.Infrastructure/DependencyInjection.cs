using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SpenceAI.Application;
using SpenceAI.Application.Analytics;
using SpenceAI.Application.Imports;
using SpenceAI.Application.Settings;
using SpenceAI.Infrastructure.Data;

namespace SpenceAI.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Local-first storage: everything (SQLite DB, encryption keys, pending uploads) lives in one data directory.
    /// Defaults to %LOCALAPPDATA%/SpenceAI; override with the "Storage:DataDirectory" setting.
    /// </summary>
    public static IServiceCollection AddSpenceAi(this IServiceCollection services, IConfiguration config)
    {
        var dataDir = config["Storage:DataDirectory"];
        if (string.IsNullOrWhiteSpace(dataDir))
        {
            dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpenceAI");
        }

        var storage = new StorageOptions { DataDirectory = Path.GetFullPath(dataDir) };
        Directory.CreateDirectory(storage.DataDirectory);
        services.AddSingleton(storage);

        services.AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={storage.DatabasePath}"));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddDataProtection()
            .SetApplicationName("SpenceAI")
            .PersistKeysToFileSystem(new DirectoryInfo(storage.KeysDirectory));

        services.AddHttpClient();
        services.AddSingleton<ISecretProtector, SecretProtector>();
        services.AddSingleton<IChatClientFactory, ChatClientFactory>();
        services.AddSingleton<IPdfTextExtractor, PdfTextExtractor>();
        services.AddSingleton<IImportFileStore, ImportFileStore>();
        services.AddSingleton<ImportQueue>();
        services.AddSingleton<IImportQueue>(sp => sp.GetRequiredService<ImportQueue>());
        services.AddHostedService<ImportWorker>();

        services.AddSingleton<TransactionExtractor>();
        services.AddScoped<SettingsService>();
        services.AddScoped<ImportService>();
        services.AddScoped<AnalyticsService>();
        services.AddScoped<InsightService>();

        return services;
    }
}

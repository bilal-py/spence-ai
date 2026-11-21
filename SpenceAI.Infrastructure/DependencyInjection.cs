using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SpenceAI.Application;
using SpenceAI.Application.Analytics;
using SpenceAI.Application.Ask;
using SpenceAI.Application.Bills;
using SpenceAI.Application.Imports;
using SpenceAI.Application.Pet;
using SpenceAI.Application.Quests;
using SpenceAI.Application.QuickAdd;
using SpenceAI.Application.Savings;
using SpenceAI.Application.Settings;
using SpenceAI.Infrastructure.Data;

namespace SpenceAI.Infrastructure;

public static class DependencyInjection
{
    /// <summary>"Storage:DataDirectory", or %LOCALAPPDATA%/SpenceAI (macOS: ~/Library/Application Support, Linux: ~/.local/share).</summary>
    public static string ResolveDataDirectory(IConfiguration config)
    {
        var dataDir = config["Storage:DataDirectory"];
        if (string.IsNullOrWhiteSpace(dataDir))
        {
            dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpenceAI");
        }

        return Path.GetFullPath(dataDir);
    }

    /// <summary>
    /// Local-first storage: everything (SQLite DB, encryption keys, pending uploads) lives in one data directory.
    /// Defaults to %LOCALAPPDATA%/SpenceAI; override with the "Storage:DataDirectory" setting.
    /// </summary>
    public static IServiceCollection AddSpenceAi(this IServiceCollection services, IConfiguration config)
    {
        var storage = new StorageOptions { DataDirectory = ResolveDataDirectory(config) };
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
        services.AddSingleton<BackupService>();
        services.AddHostedService<BackupScheduler>();

        services.AddSingleton<TransactionExtractor>();
        services.AddScoped<SettingsService>();
        services.AddScoped<ImportService>();
        services.AddScoped<AnalyticsService>();
        services.AddScoped<InsightService>();
        services.AddScoped<QuickAddService>();
        services.AddScoped<SavingsService>();
        services.AddScoped<WrappedService>();
        services.AddScoped<PetService>();
        services.AddScoped<BillService>();
        services.AddScoped<QuestService>();
        services.AddScoped<AskService>();

        return services;
    }
}

using System.IO.Compression;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SpenceAI.Application;
using SpenceAI.Application.Categories;
using SpenceAI.Domain;
using SpenceAI.Infrastructure;
using SpenceAI.Infrastructure.Data;

namespace SpenceAI.Tests;

/// <summary>Backup and restore against a real SQLite file in a temporary data directory.</summary>
public sealed class BackupServiceTests : IDisposable
{
    private readonly string _dataDir = Path.Combine(Path.GetTempPath(), "spence-backup-tests-" + Guid.NewGuid().ToString("N"));
    private readonly ServiceProvider _services;
    private readonly BackupService _backups;

    public BackupServiceTests()
    {
        var storage = new StorageOptions { DataDirectory = _dataDir };
        Directory.CreateDirectory(_dataDir);
        Directory.CreateDirectory(storage.KeysDirectory);
        File.WriteAllText(Path.Combine(storage.KeysDirectory, "key-1.xml"), "<key />");

        _services = new ServiceCollection()
            .AddSingleton(storage)
            .AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={storage.DatabasePath}"))
            .BuildServiceProvider();

        using (var scope = _services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.Migrate();
            CategoryTemplates.SeedAsync(db).GetAwaiter().GetResult();
            db.Settings.Add(new AppSettings());
            db.SaveChanges();
        }

        _backups = new BackupService(storage, _services.GetRequiredService<IServiceScopeFactory>(), NullLogger<BackupService>.Instance);
    }

    [Fact]
    public async Task Backup_holds_the_database_keys_and_a_manifest()
    {
        await AddTransactionAsync("Coffee");

        var file = await _backups.CreateAsync(BackupKind.Manual, default);

        Assert.Equal(BackupKind.Manual, file.Kind);
        using var zip = ZipFile.OpenRead(_backups.PathOf(file.FileName));
        Assert.NotNull(zip.GetEntry("spence.db"));
        Assert.NotNull(zip.GetEntry("manifest.json"));
        Assert.NotNull(zip.GetEntry("keys/key-1.xml"));

        await using var stream = File.OpenRead(_backups.PathOf(file.FileName));
        var manifest = await _backups.InspectAsync(stream, default);
        Assert.Equal(1, manifest.Transactions);
        Assert.NotNull(manifest.LastMigration);
    }

    [Fact]
    public async Task Restore_brings_back_the_old_data_and_saves_the_current_data_first()
    {
        await AddTransactionAsync("Before backup");
        var backup = await _backups.CreateAsync(BackupKind.Manual, default);
        await AddTransactionAsync("After backup");

        RestoreResult result;
        await using (var stream = File.OpenRead(_backups.PathOf(backup.FileName)))
        {
            result = await _backups.RestoreAsync(stream, default);
        }

        Assert.Equal(1, result.Restored.Transactions);
        Assert.Equal(["Before backup"], await DescriptionsAsync());

        // The data that was replaced is one click away.
        Assert.StartsWith("spence-before-restore-", result.SafetyBackup);
        await using (var undo = File.OpenRead(_backups.PathOf(result.SafetyBackup)))
        {
            await _backups.RestoreAsync(undo, default);
        }

        Assert.Equal(["After backup", "Before backup"], (await DescriptionsAsync()).Order());
    }

    [Fact]
    public async Task A_file_that_is_not_a_backup_is_rejected_without_changing_anything()
    {
        await AddTransactionAsync("Keep me");

        var error = await Assert.ThrowsAsync<AppException>(() => _backups.RestoreAsync(new MemoryStream("not a zip"u8.ToArray()), default));

        Assert.Contains("isn't a Spence backup", error.Message);
        Assert.Equal(["Keep me"], await DescriptionsAsync());
        Assert.Empty(_backups.List());
    }

    [Fact]
    public async Task Only_the_newest_backups_of_each_kind_are_kept()
    {
        Directory.CreateDirectory(_backups.Directory);
        for (var day = 1; day <= 9; day++)
        {
            File.WriteAllText(Path.Combine(_backups.Directory, $"spence-daily-202601{day:00}-120000.spence"), "old");
        }

        await _backups.CreateAsync(BackupKind.Daily, default);

        var daily = _backups.List().Where(b => b.Kind == BackupKind.Daily).ToList();
        Assert.Equal(BackupService.KeepPerKind, daily.Count);
        Assert.True(daily[0].CreatedAt > new DateTime(2026, 2, 1));
    }

    [Fact]
    public void Backup_names_outside_the_folder_are_not_found() =>
        Assert.Equal(404, Assert.Throws<AppException>(() => _backups.PathOf("..\\spence.db")).StatusCode);

    private async Task AddTransactionAsync(string description)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Transactions.Add(new Transaction { Date = new DateOnly(2026, 11, 1), Amount = 5, Description = description });
        await db.SaveChangesAsync();
    }

    private async Task<List<string>> DescriptionsAsync()
    {
        using var scope = _services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Transactions.Select(t => t.Description).ToListAsync();
    }

    public void Dispose()
    {
        _services.Dispose();
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dataDir)) Directory.Delete(_dataDir, recursive: true);
    }
}

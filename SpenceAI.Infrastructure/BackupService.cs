using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SpenceAI.Application;
using SpenceAI.Domain;
using SpenceAI.Infrastructure.Data;

namespace SpenceAI.Infrastructure;

public enum BackupKind
{
    /// <summary>"Back up now" in Settings.</summary>
    Manual,
    /// <summary>Taken automatically once a day.</summary>
    Daily,
    /// <summary>The data as it was just before a restore replaced it.</summary>
    BeforeRestore,
    /// <summary>The data as it was just before an app update changed the database schema.</summary>
    BeforeUpdate,
}

/// <summary>What a backup holds, stored in it as manifest.json and shown before restoring.</summary>
public record BackupManifest(
    int Format, string AppVersion, string? LastMigration, DateTime CreatedAt,
    int Transactions, int Categories, int Rules, DateOnly? FirstDate, DateOnly? LastDate);

public record BackupFile(string FileName, BackupKind Kind, DateTime CreatedAt, long SizeBytes);

public record BackupStatus(string Directory, bool AutoBackup, string? CopyFolder, DateTime? LastBackupAt, IReadOnlyList<BackupFile> Backups);

public record UpdateBackupSettingsRequest(bool AutoBackup, string? CopyFolder);

public record RestoreResult(BackupManifest Restored, string SafetyBackup);

/// <summary>
/// One-file backups of everything in the data directory that matters: the SQLite DB (copied with VACUUM INTO, so
/// it's consistent while the app runs), the Data Protection keys (without them the saved API key can't be decrypted)
/// and a manifest. A restore checks the file first and always backs up the current data before replacing it.
/// </summary>
public partial class BackupService(StorageOptions storage, IServiceScopeFactory scopes, ILogger<BackupService> logger)
{
    public const int Format = 1;
    public const string Extension = ".spence";
    public const long MaxUploadBytes = 500L * 1024 * 1024;
    /// <summary>Backups kept per kind in the backups folder (and in the copy folder).</summary>
    public const int KeepPerKind = 7;

    private const string DbEntry = "spence.db";
    private const string ManifestEntry = "manifest.json";
    private const string KeysPrefix = "keys/";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>One backup or restore at a time; a restore must not overlap a backup of the file it replaces.</summary>
    private static readonly SemaphoreSlim Gate = new(1, 1);

    [GeneratedRegex(@"^spence-(manual|daily|before-restore|before-update)-(\d{8}-\d{6})\.spence$")]
    private static partial Regex FileNamePattern();

    public string Directory => Path.Combine(storage.DataDirectory, "backups");

    // ---------- Status / settings ----------

    public async Task<BackupStatus> GetStatusAsync(CancellationToken ct)
    {
        var (auto, copyFolder) = await ReadSettingsAsync(ct);
        var files = List();
        return new BackupStatus(Directory, auto, copyFolder, files.FirstOrDefault()?.CreatedAt, files);
    }

    public async Task<BackupStatus> UpdateSettingsAsync(UpdateBackupSettingsRequest req, CancellationToken ct)
    {
        var folder = string.IsNullOrWhiteSpace(req.CopyFolder) ? null : req.CopyFolder.Trim();
        if (folder is not null)
        {
            if (!Path.IsPathFullyQualified(folder)) throw new AppException("Use a full folder path, like C:\\Users\\you\\OneDrive\\Spence.");
            if (!System.IO.Directory.Exists(folder)) throw new AppException("That folder doesn't exist. Create it first, then paste its path.");
        }

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var settings = await db.Settings.FirstAsync(ct);
        settings.AutoBackup = req.AutoBackup;
        settings.BackupCopyFolder = folder;
        await db.SaveChangesAsync(ct);
        return await GetStatusAsync(ct);
    }

    public IReadOnlyList<BackupFile> List()
    {
        if (!System.IO.Directory.Exists(Directory)) return [];
        return new DirectoryInfo(Directory).EnumerateFiles("*" + Extension)
            .Select(f => Parse(f.Name) is { } p ? new BackupFile(f.Name, p.Kind, p.CreatedAt, f.Length) : null)
            .OfType<BackupFile>()
            .OrderByDescending(f => f.CreatedAt)
            .ToList();
    }

    /// <summary>Full path of a backup in the backups folder, or a 404 (names are checked, so no path tricks).</summary>
    public string PathOf(string fileName)
    {
        if (Parse(fileName) is null) throw new AppException("Backup not found.", 404);
        var path = Path.Combine(Directory, fileName);
        return File.Exists(path) ? path : throw new AppException("Backup not found.", 404);
    }

    // ---------- Backup ----------

    public async Task<BackupFile> CreateAsync(BackupKind kind, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            return await CreateUnlockedAsync(kind, ct);
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Takes a daily backup when automatic backups are on and the newest backup is over a day old.</summary>
    public async Task<BackupFile?> CreateDailyIfDueAsync(CancellationToken ct)
    {
        var (auto, _) = await ReadSettingsAsync(ct);
        if (!auto || !File.Exists(storage.DatabasePath)) return null;
        var last = List().FirstOrDefault()?.CreatedAt;
        if (last is not null && DateTime.UtcNow - last.Value < TimeSpan.FromHours(24)) return null;
        return await CreateAsync(BackupKind.Daily, ct);
    }

    private async Task<BackupFile> CreateUnlockedAsync(BackupKind kind, CancellationToken ct)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var now = DateTime.UtcNow;
        string fileName, path;
        // Names have one-second resolution; two backups in the same second (restore, then undo) take the next free second.
        while (true)
        {
            fileName = $"spence-{KindName(kind)}-{now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}{Extension}";
            path = Path.Combine(Directory, fileName);
            if (!File.Exists(path)) break;
            now = now.AddSeconds(1);
        }

        var work = Path.Combine(Directory, ".tmp-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(work);

        try
        {
            var snapshot = Path.Combine(work, DbEntry);
            await using (var live = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = storage.DatabasePath, Pooling = false }.ToString()))
            {
                await live.OpenAsync(ct);
                await using var cmd = live.CreateCommand();
                cmd.CommandText = "VACUUM INTO $path";
                cmd.Parameters.AddWithValue("$path", snapshot);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            var manifest = await ReadManifestFromDbAsync(snapshot, now, ct);

            var partial = path + ".partial";
            await using (var zipStream = File.Create(partial))
            using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create))
            {
                zip.CreateEntryFromFile(snapshot, DbEntry, CompressionLevel.Optimal);
                if (System.IO.Directory.Exists(storage.KeysDirectory))
                {
                    foreach (var key in System.IO.Directory.EnumerateFiles(storage.KeysDirectory, "*.xml"))
                    {
                        zip.CreateEntryFromFile(key, KeysPrefix + Path.GetFileName(key));
                    }
                }

                await using var entry = zip.CreateEntry(ManifestEntry).Open();
                await JsonSerializer.SerializeAsync(entry, manifest, Json, ct);
            }

            File.Move(partial, path);
            Prune(Directory, kind);
            await CopyToFolderAsync(path, kind, ct);

            logger.LogInformation("Backup written: {Path}", path);
            return new BackupFile(fileName, kind, now, new FileInfo(path).Length);
        }
        finally
        {
            TryDeleteDirectory(work);
        }
    }

    private async Task CopyToFolderAsync(string path, BackupKind kind, CancellationToken ct)
    {
        var (_, folder) = await ReadSettingsAsync(ct);
        if (folder is null) return;
        try
        {
            File.Copy(path, Path.Combine(folder, Path.GetFileName(path)), overwrite: true);
            Prune(folder, kind);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The local backup exists; a missing or offline sync folder shouldn't fail it.
            logger.LogWarning(ex, "Couldn't copy the backup to {Folder}", folder);
        }
    }

    private static void Prune(string folder, BackupKind kind)
    {
        var old = new DirectoryInfo(folder).EnumerateFiles("*" + Extension)
            .Select(f => (File: f, Parsed: Parse(f.Name)))
            .Where(x => x.Parsed?.Kind == kind)
            .OrderByDescending(x => x.Parsed!.Value.CreatedAt)
            .Skip(KeepPerKind);
        foreach (var (file, _) in old) file.Delete();
    }

    // ---------- Restore ----------

    /// <summary>Reads a backup without changing anything, so the user can see what it holds before restoring.</summary>
    public async Task<BackupManifest> InspectAsync(Stream upload, CancellationToken ct)
    {
        var work = Path.Combine(Directory, ".tmp-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(work);
        try
        {
            return (await UnpackAndValidateAsync(upload, work, ct)).Manifest;
        }
        finally
        {
            TryDeleteDirectory(work);
        }
    }

    public async Task<RestoreResult> RestoreAsync(Stream upload, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        System.IO.Directory.CreateDirectory(Directory);
        var work = Path.Combine(Directory, ".tmp-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(work);
        try
        {
            var (manifest, db, keys) = await UnpackAndValidateAsync(upload, work, ct);

            using (var scope = scopes.CreateScope())
            {
                var current = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                if (await current.ImportBatches.AnyAsync(b => b.Status == ImportStatus.Queued || b.Status == ImportStatus.Processing, ct))
                {
                    throw new AppException("An import is still being read. Wait for it to finish, then restore.", 409);
                }
            }

            var safety = await CreateUnlockedAsync(BackupKind.BeforeRestore, ct);

            // Close pooled connections so the file can be replaced, then swap it in.
            SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { "-wal", "-shm", "-journal" }) File.Delete(storage.DatabasePath + suffix);
            File.Copy(db, storage.DatabasePath, overwrite: true);

            // Keys are added, never removed: older keys still decrypt values saved before.
            System.IO.Directory.CreateDirectory(storage.KeysDirectory);
            foreach (var key in keys) File.Copy(key, Path.Combine(storage.KeysDirectory, Path.GetFileName(key)), overwrite: true);

            using (var scope = scopes.CreateScope())
            {
                var restored = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await restored.Database.MigrateAsync(ct);
                // Imports that were mid-flight when the backup was taken have no files any more.
                await restored.ImportBatches
                    .Where(b => b.Status == ImportStatus.Queued || b.Status == ImportStatus.Processing)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(b => b.Status, ImportStatus.Failed)
                        .SetProperty(b => b.Error, "This import was still being read when the backup was taken. Upload the file again."), ct);
            }

            logger.LogInformation("Restored a backup from {CreatedAt}; previous data saved as {Safety}", manifest.CreatedAt, safety.FileName);
            return new RestoreResult(manifest, safety.FileName);
        }
        finally
        {
            TryDeleteDirectory(work);
            Gate.Release();
        }
    }

    private async Task<(BackupManifest Manifest, string Db, List<string> Keys)> UnpackAndValidateAsync(Stream upload, string work, CancellationToken ct)
    {
        var db = Path.Combine(work, DbEntry);
        var keysDir = Path.Combine(work, "keys");
        System.IO.Directory.CreateDirectory(keysDir);
        var keys = new List<string>();
        BackupManifest? stored = null;

        try
        {
            using var zip = new ZipArchive(upload, ZipArchiveMode.Read, leaveOpen: true);
            var dbEntry = zip.GetEntry(DbEntry) ?? throw NotABackup();
            var manifestEntry = zip.GetEntry(ManifestEntry) ?? throw NotABackup();
            try
            {
                await using var m = manifestEntry.Open();
                stored = await JsonSerializer.DeserializeAsync<BackupManifest>(m, Json, ct);
            }
            catch (JsonException)
            {
                throw NotABackup();
            }

            if (stored is null || stored.Format > Format) throw new AppException("This backup comes from a newer version of Spence. Update the app, then restore it.");
            if (dbEntry.Length > MaxUploadBytes * 4) throw new AppException("This backup is too large to restore.");

            await using (var output = File.Create(db))
            await using (var input = dbEntry.Open())
            {
                await input.CopyToAsync(output, ct);
            }

            // Only plain file names under keys/ are taken, so an entry can't write outside the work folder.
            foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith(KeysPrefix, StringComparison.Ordinal)))
            {
                var name = entry.FullName[KeysPrefix.Length..];
                if (name.Length == 0 || name != Path.GetFileName(name) || !name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) continue;
                var target = Path.Combine(keysDir, name);
                entry.ExtractToFile(target, overwrite: true);
                keys.Add(target);
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentOutOfRangeException)
        {
            // Not a zip. A file shorter than a zip's end record makes the upload stream throw while seeking.
            throw NotABackup();
        }

        await using (var check = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = db, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
        {
            try
            {
                await check.OpenAsync(ct);
                await using var cmd = check.CreateCommand();
                cmd.CommandText = "PRAGMA integrity_check";
                if (await cmd.ExecuteScalarAsync(ct) as string != "ok") throw new AppException("This backup's database is damaged and can't be restored.");
            }
            catch (SqliteException)
            {
                throw NotABackup();
            }
        }

        // Counts come from the database itself; when and by which version it was made, from the stored manifest.
        var manifest = await ReadManifestFromDbAsync(db, stored!.CreatedAt, ct) with { AppVersion = stored.AppVersion };

        using (var scope = scopes.CreateScope())
        {
            var known = scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.GetMigrations().ToHashSet();
            if (manifest.LastMigration is { } last && !known.Contains(last))
            {
                throw new AppException("This backup comes from a newer version of Spence. Update the app, then restore it.");
            }
        }

        return (manifest, db, keys);

        static AppException NotABackup() => new("That isn't a Spence backup. Choose a .spence file made from Settings.");
    }

    // ---------- Helpers ----------

    /// <summary>Counts what a database file holds.</summary>
    private static async Task<BackupManifest> ReadManifestFromDbAsync(string dbPath, DateTime createdAt, CancellationToken ct)
    {
        await using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        await c.OpenAsync(ct);

        async Task<object?> Scalar(string sql)
        {
            await using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            return await cmd.ExecuteScalarAsync(ct);
        }

        async Task<bool> HasTable(string name) =>
            Convert.ToInt32(await Scalar($"SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '{name}'"), CultureInfo.InvariantCulture) > 0;

        if (!await HasTable("__EFMigrationsHistory") || !await HasTable("Transactions"))
        {
            throw new AppException("That isn't a Spence backup. Choose a .spence file made from Settings.");
        }

        static DateOnly? Date(object? v) => v is string s && DateOnly.TryParse(s, CultureInfo.InvariantCulture, out var d) ? d : null;
        int Count(object? v) => Convert.ToInt32(v, CultureInfo.InvariantCulture);

        return new BackupManifest(
            Format,
            AppVersion(),
            await Scalar("SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC LIMIT 1") as string,
            createdAt,
            Count(await Scalar("SELECT COUNT(*) FROM Transactions")),
            Count(await Scalar("SELECT COUNT(*) FROM Categories")),
            Count(await Scalar("SELECT COUNT(*) FROM MerchantRules")),
            Date(await Scalar("SELECT MIN(Date) FROM Transactions")),
            Date(await Scalar("SELECT MAX(Date) FROM Transactions")));
    }

    private async Task<(bool Auto, string? CopyFolder)> ReadSettingsAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var s = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Settings.AsNoTracking().FirstOrDefaultAsync(ct);
            return (s?.AutoBackup ?? true, s?.BackupCopyFolder);
        }
        catch (SqliteException)
        {
            // Before migrations have run (the backup taken ahead of an update), the columns may not exist yet.
            return (true, null);
        }
    }

    private static (BackupKind Kind, DateTime CreatedAt)? Parse(string fileName)
    {
        var m = FileNamePattern().Match(fileName);
        if (!m.Success) return null;
        var kind = m.Groups[1].Value switch
        {
            "manual" => BackupKind.Manual,
            "daily" => BackupKind.Daily,
            "before-restore" => BackupKind.BeforeRestore,
            _ => BackupKind.BeforeUpdate,
        };
        var at = DateTime.ParseExact(m.Groups[2].Value, "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        return (kind, at);
    }

    private static string KindName(BackupKind kind) => kind switch
    {
        BackupKind.Manual => "manual",
        BackupKind.Daily => "daily",
        BackupKind.BeforeRestore => "before-restore",
        _ => "before-update",
    };

    private static string AppVersion() =>
        typeof(BackupService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "dev";

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (System.IO.Directory.Exists(path)) System.IO.Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp folder is harmless; it's skipped by List().
        }
    }
}

/// <summary>Checks once an hour whether a daily backup is due.</summary>
public class BackupScheduler(BackupService backups, ILogger<BackupScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try
            {
                await backups.CreateDailyIfDueAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Automatic backup failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

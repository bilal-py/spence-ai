using System.Globalization;

namespace SpenceAI.WebApi.Desktop;

/// <summary>
/// Desktop logs: one file a day in &lt;data&gt;/logs, kept for 14 days. The Windows app has no console,
/// so this is where errors go. Levels follow the usual "Logging" configuration.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    const int KeepDays = 14;
    readonly string directory;
    readonly object gate = new();
    StreamWriter? writer;
    DateOnly day;

    public FileLoggerProvider(string directory)
    {
        this.directory = directory;
        try
        {
            Directory.CreateDirectory(directory);
            foreach (var old in Directory.EnumerateFiles(directory, "spence-*.log"))
            {
                if (File.GetLastWriteTimeUtc(old) < DateTime.UtcNow.AddDays(-KeepDays))
                {
                    File.Delete(old);
                }
            }
        }
        catch
        {
            // Logging must never stop the app.
        }
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    void Write(string line)
    {
        lock (gate)
        {
            try
            {
                var today = DateOnly.FromDateTime(DateTime.Now);
                if (writer == null || today != day)
                {
                    writer?.Dispose();
                    day = today;
                    var stream = new FileStream(Path.Combine(directory, $"spence-{today:yyyyMMdd}.log"), FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                    writer = new StreamWriter(stream) { AutoFlush = true };
                }

                writer.WriteLine(line);
            }
            catch
            {
            }
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            writer?.Dispose();
            writer = null;
        }
    }

    sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var line = $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)} [{logLevel}] {category}: {formatter(state, exception)}";
            provider.Write(exception == null ? line : line + Environment.NewLine + exception);
        }
    }
}

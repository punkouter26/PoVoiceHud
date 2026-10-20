using System.Globalization;
using Microsoft.Extensions.Logging;

namespace PoVoiceHud.Core.Diagnostics;

/// <summary>
/// Writes Information and above to logs\{date}.log so a Release build that misbehaves leaves something to read.
/// Files older than <see cref="KeepDays"/> are deleted when the provider is created. One provider lives for the
/// whole process (the DI container is rebuilt on every settings change, the log is not).
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    public const int KeepDays = 14;

    private readonly string _directory;
    private readonly Lock _gate = new();

    public FileLoggerProvider(string directory)
    {
        _directory = directory;
        try
        {
            Directory.CreateDirectory(directory);
            foreach (var old in Directory.GetFiles(directory, "*.log").Where(f => File.GetLastWriteTime(f) < DateTime.Now.AddDays(-KeepDays)))
                File.Delete(old);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // a log folder that cannot be tidied is still written to
        }
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName[(categoryName.LastIndexOf('.') + 1)..]);

    private void Write(LogLevel level, string category, string message, Exception? exception)
    {
        var now = DateTimeOffset.Now;
        var line = string.Create(CultureInfo.InvariantCulture, $"{now:yyyy-MM-dd HH:mm:ss.fff} [{Short(level)}] {category}: {message}\n");
        if (exception is not null) line += exception + "\n";
        try
        {
            lock (_gate) File.AppendAllText(Path.Combine(_directory, $"{now:yyyy-MM-dd}.log"), line);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // logging must never be the thing that fails
        }
    }

    private static string Short(LogLevel level) => level switch
    {
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "DBG",
    };

    public void Dispose()
    {
        // nothing is held open between writes
    }

    private sealed class FileLogger(FileLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel)) owner.Write(logLevel, category, formatter(state, exception), exception);
        }
    }
}

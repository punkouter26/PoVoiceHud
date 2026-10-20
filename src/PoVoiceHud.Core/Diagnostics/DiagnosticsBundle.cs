using System.IO.Compression;

namespace PoVoiceHud.Core.Diagnostics;

/// <summary>
/// A zip to attach to a bug report: the last week of logs, crash.log, settings.json and a description of the machine.
/// Never the session transcripts (they hold what people said) and never secrets (those live in Credential Manager, not in files).
/// </summary>
public static class DiagnosticsBundle
{
    private const int LogDays = 7;

    /// <param name="dataDir">%LOCALAPPDATA%\PoVoiceHud, or a test folder laid out the same way.</param>
    /// <param name="environment">Version, OS, mode and so on, written as environment.txt.</param>
    public static string Create(string dataDir, string zipPath, string environment)
    {
        if (File.Exists(zipPath)) File.Delete(zipPath);
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);

        var logs = Path.Combine(dataDir, "logs");
        if (Directory.Exists(logs))
            foreach (var log in Directory.GetFiles(logs, "*.log").Where(f => File.GetLastWriteTime(f) >= DateTime.Now.AddDays(-LogDays)))
                Add(zip, log, $"logs/{Path.GetFileName(log)}");
        foreach (var name in new[] { "crash.log", "settings.json" })
            Add(zip, Path.Combine(dataDir, name), name);

        using var writer = new StreamWriter(zip.CreateEntry("environment.txt").Open());
        writer.Write(environment);
        return zipPath;
    }

    private static void Add(ZipArchive zip, string file, string entry)
    {
        if (!File.Exists(file)) return;
        // The app may be writing today's log right now: read it shared instead of asking for exclusive access.
        using var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var target = zip.CreateEntry(entry).Open();
        source.CopyTo(target);
    }
}

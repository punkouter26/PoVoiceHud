using Microsoft.Extensions.Logging;
using Velopack;
using Velopack.Exceptions;

namespace PoVoiceHud.App;

/// <summary>
/// Checks the feed in settings.json (an https URL or a folder of Velopack releases, see build/pack.ps1) and downloads a newer
/// version in the background; it is installed when the HUD closes, without a prompt, and runs from the next start.
/// A copy that was not installed by Setup.exe (dotnet run, the artifacts folder) never updates itself.
/// </summary>
internal static class Updates
{
    /// <returns>What to show on the pill, or null when there is nothing to say.</returns>
    public static async Task<string?> CheckAsync(string? feed, ILogger log, bool userAsked)
    {
        if (string.IsNullOrWhiteSpace(feed)) return userAsked ? "No update feed is set (UpdateFeed in settings.json)." : null;
        try
        {
            var manager = new UpdateManager(feed.Trim());
            if (!manager.IsInstalled) return userAsked ? "Updates only apply to an installed copy (run Setup.exe)." : null;
            if (manager.UpdatePendingRestart is { } pending) return $"Version {pending.Version} installs when PoVoiceHud closes.";

            var update = await manager.CheckForUpdatesAsync();
            if (update is null) return userAsked ? $"PoVoiceHud {manager.CurrentVersion} is up to date." : null;

            await manager.DownloadUpdatesAsync(update);
            manager.WaitExitThenApplyUpdates(update.TargetFullRelease, silent: true, restart: false);
            log.LogInformation("Downloaded {Version}; it installs on exit", update.TargetFullRelease.Version);
            return $"Version {update.TargetFullRelease.Version} downloaded — it installs when PoVoiceHud closes.";
        }
        catch (Exception e) when (e is HttpRequestException or IOException or UnauthorizedAccessException or InvalidOperationException
                                     or NotInstalledException or ChecksumFailedException or AcquireLockFailedException or TaskCanceledException)
        {
            log.LogWarning(e, "Update check against {Feed} failed", feed);
            return userAsked ? $"Update check failed: {e.Message}" : null;
        }
    }
}

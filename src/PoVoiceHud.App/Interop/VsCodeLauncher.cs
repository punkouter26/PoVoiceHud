using Windows.System;

namespace PoVoiceHud.App.Interop;

/// <summary>vscode://file/{path}:{line} and https:// links go through the shell; VS Code focuses the line itself.</summary>
internal static class VsCodeLauncher
{
    public static async Task<bool> OpenAsync(Uri uri) => await Launcher.LaunchUriAsync(uri);
}

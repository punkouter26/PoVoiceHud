using PoVoiceHud.App.Interop;
using Velopack;

namespace PoVoiceHud.App;

public static class Program
{
    [STAThread]
    private static void Main()
    {
        // When Setup.exe or an update launches the app with a hook argument, Velopack handles it here and exits.
        // Uninstalling also takes the app out of "Start with Windows", so nothing is left pointing at a deleted exe.
        VelopackApp.Build()
            .OnBeforeUninstallFastCallback(_ => Autostart.Set(false))
            .Run();
        XamlGeneratedProgram.XamlGeneratedMain();
    }
}

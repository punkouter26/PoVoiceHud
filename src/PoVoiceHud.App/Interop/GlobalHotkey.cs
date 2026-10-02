using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using WinUIEx;
using WinUIEx.Messaging;

namespace PoVoiceHud.App.Interop;

/// <summary>
/// Global mute hotkey via RegisterHotKey; WM_HOTKEY arrives through WinUIEx's window message hook.
/// Win+Shift+M is the spec'd binding, but Explorer owns it ("restore minimized windows") on a stock desktop,
/// so Ctrl+Shift+M is tried next. <see cref="Binding"/> says which one is live.
/// </summary>
internal sealed partial class GlobalHotkey : IDisposable
{
    private const int Id = 1;
    private const uint ModShift = 0x0004;
    private const uint ModControl = 0x0002;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;
    private const uint WmHotkey = 0x0312;

    private static readonly (string Name, uint Modifiers)[] Candidates =
    [
        ("Win+Shift+M", ModWin | ModShift | ModNoRepeat),
        ("Ctrl+Shift+M", ModControl | ModShift | ModNoRepeat),
    ];

    private readonly nint _hwnd;
    private readonly WindowManager _manager;
    private readonly Action _pressed;

    public GlobalHotkey(Window window, Action pressed)
    {
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        _manager = WindowManager.Get(window);
        _pressed = pressed;
        Binding = Candidates.FirstOrDefault(c => RegisterHotKey(_hwnd, Id, c.Modifiers, 'M')).Name;
        _manager.WindowMessageReceived += OnMessage;
    }

    /// <summary>The registered combination, or null when both are taken (the pill's dot still toggles mute).</summary>
    public string? Binding { get; }

    private void OnMessage(object? sender, WindowMessageEventArgs e)
    {
        if (e.Message.MessageId == WmHotkey && (int)e.Message.WParam == Id) _pressed();
    }

    public void Dispose()
    {
        _manager.WindowMessageReceived -= OnMessage;
        if (Binding is not null) UnregisterHotKey(_hwnd, Id);
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(nint hWnd, int id);
}

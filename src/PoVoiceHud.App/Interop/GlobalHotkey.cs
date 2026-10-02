using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using WinUIEx;
using WinUIEx.Messaging;

namespace PoVoiceHud.App.Interop;

/// <summary>Win+Shift+M via RegisterHotKey; WM_HOTKEY arrives through WinUIEx's window message hook.</summary>
internal sealed partial class GlobalHotkey : IDisposable
{
    private const int Id = 1;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;
    private const uint WmHotkey = 0x0312;

    private readonly nint _hwnd;
    private readonly WindowManager _manager;
    private readonly Action _pressed;

    public GlobalHotkey(Window window, Action pressed)
    {
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        _manager = WindowManager.Get(window);
        _pressed = pressed;
        Registered = RegisterHotKey(_hwnd, Id, ModWin | ModShift | ModNoRepeat, 'M');
        _manager.WindowMessageReceived += OnMessage;
    }

    /// <summary>False when another app already owns Win+Shift+M; the pill's dot still toggles mute by click.</summary>
    public bool Registered { get; }

    private void OnMessage(object? sender, WindowMessageEventArgs e)
    {
        if (e.Message.MessageId == WmHotkey && (int)e.Message.WParam == Id) _pressed();
    }

    public void Dispose()
    {
        _manager.WindowMessageReceived -= OnMessage;
        UnregisterHotKey(_hwnd, Id);
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(nint hWnd, int id);
}

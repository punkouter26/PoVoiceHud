using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using WinUIEx;
using WinUIEx.Messaging;

namespace PoVoiceHud.App.Interop;

/// <summary>
/// One global hotkey via RegisterHotKey; WM_HOTKEY arrives through WinUIEx's window message hook.
/// Combinations are tried in order because another program may own one (Explorer keeps Win+Shift+M on a
/// stock desktop). <see cref="Binding"/> says which one is live.
/// </summary>
internal sealed partial class GlobalHotkey : IDisposable
{
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;
    private const uint WmHotkey = 0x0312;
    private const uint VkF1 = 0x70;

    private readonly nint _hwnd;
    private readonly int _id;
    private readonly WindowManager _manager;
    private readonly Action _pressed;

    /// <param name="id">Distinguishes this hotkey from the window's others.</param>
    /// <param name="candidates">Combinations such as "Ctrl+Shift+M" or "Ctrl+Shift+F12", most wanted first.</param>
    public GlobalHotkey(Window window, int id, IEnumerable<string> candidates, Action pressed)
    {
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        _id = id;
        _manager = WindowManager.Get(window);
        _pressed = pressed;
        Binding = candidates.Distinct().FirstOrDefault(c => TryParse(c, out var modifiers, out var key) && RegisterHotKey(_hwnd, id, modifiers, key));
        _manager.WindowMessageReceived += OnMessage;
    }

    /// <summary>The registered combination, or null when every candidate is taken.</summary>
    public string? Binding { get; }

    /// <summary>Modifiers joined by '+' and ending in a letter, a digit or F1–F12.</summary>
    internal static bool TryParse(string combination, out uint modifiers, out uint key)
    {
        modifiers = ModNoRepeat;
        key = 0;
        foreach (var part in combination.ToUpperInvariant().Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == "WIN") modifiers |= ModWin;
            else if (part == "CTRL") modifiers |= ModControl;
            else if (part == "SHIFT") modifiers |= ModShift;
            else if (part == "ALT") modifiers |= ModAlt;
            else if (part.Length == 1 && char.IsAsciiLetterOrDigit(part[0])) key = part[0];   // virtual-key codes for A–Z and 0–9 are their ASCII codes
            else if (part[0] == 'F' && int.TryParse(part.AsSpan(1), out var f) && f is >= 1 and <= 12) key = VkF1 + (uint)f - 1;
            else return false;
        }
        return key != 0 && modifiers != ModNoRepeat;
    }

    private void OnMessage(object? sender, WindowMessageEventArgs e)
    {
        if (e.Message.MessageId == WmHotkey && (int)e.Message.WParam == _id) _pressed();
    }

    public void Dispose()
    {
        _manager.WindowMessageReceived -= OnMessage;
        if (Binding is not null) UnregisterHotKey(_hwnd, _id);
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(nint hWnd, int id);
}

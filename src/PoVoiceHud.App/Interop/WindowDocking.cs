using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WinUIEx;

namespace PoVoiceHud.App.Interop;

/// <summary>Snaps the window to the nearest work-area edge or corner when dropped within the snap distance.</summary>
internal sealed class WindowDocking : IDisposable
{
    private const int SnapDistance = 24;
    private readonly Window _window;
    private readonly WindowManager _manager;
    private bool _moving;

    public WindowDocking(Window window)
    {
        _window = window;
        _manager = WindowManager.Get(window);
        _manager.PositionChanged += OnPositionChanged;
    }

    private void OnPositionChanged(object? sender, Windows.Graphics.PointInt32 position)
    {
        if (_moving) return;
        var work = DisplayArea.GetFromWindowId(_window.AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        var size = _window.AppWindow.Size;
        var x = position.X;
        var y = position.Y;
        if (Math.Abs(x - work.X) <= SnapDistance) x = work.X;
        else if (Math.Abs(x + size.Width - (work.X + work.Width)) <= SnapDistance) x = work.X + work.Width - size.Width;
        if (Math.Abs(y - work.Y) <= SnapDistance) y = work.Y;
        else if (Math.Abs(y + size.Height - (work.Y + work.Height)) <= SnapDistance) y = work.Y + work.Height - size.Height;
        if (x == position.X && y == position.Y) return;

        _moving = true;
        try
        {
            _window.AppWindow.Move(new Windows.Graphics.PointInt32(x, y));
        }
        finally
        {
            _moving = false;
        }
    }

    public void Dispose() => _manager.PositionChanged -= OnPositionChanged;
}

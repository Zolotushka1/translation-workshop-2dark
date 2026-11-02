using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Workshop.App;

public sealed class GameHost : HwndHost
{
    private nint container, game, oldParent, oldStyle, oldExStyle, presentation;
    private Native.Rect oldRect;
    private (int Width, int Height) lastSize;
    public event Action? ViewportResized;
    public bool IsAttached => game != 0 && Native.IsWindow(game);
    public nint GameWindow => IsAttached ? game : 0;
    public (int Width, int Height) ClientSize
    {
        get { Native.GetClientRect(GameWindow, out var area); return (area.Right, area.Bottom); }
    }
    public void Present(nint target)
    {
        if (!IsAttached) throw new InvalidOperationException("Сначала запустите выбранную копию игры.");
        presentation = target;
        Native.SetLastError(0);
        if (Native.SetParent(game, target == 0 ? container : target) == 0 && Marshal.GetLastWin32Error() != 0) throw new Win32Exception();
        ResizeGame(); FocusGame();
    }
    public void ResizePresentation() => ResizeGame();

    public GameHost() { Focusable = true; SizeChanged += (_, _) => ResizeGame(); }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        container = Native.CreateWindowEx(0, "static", "", 0x40000000 | 0x10000000 | 0x02000000, 0, 0, 640, 360, hwndParent.Handle, 0, 0, 0);
        if (container == 0) throw new Win32Exception();
        return new(this, container);
    }

    protected override void DestroyWindowCore(HandleRef hwnd) { Detach(); Native.DestroyWindow(hwnd.Handle); container = 0; }

    public void Attach(nint window)
    {
        if (window == 0 || !Native.IsWindow(window)) throw new InvalidOperationException("Окно игры не найдено.");
        if (container == 0) throw new InvalidOperationException("Панель игры ещё не загружена.");
        if (!Native.IsWindow(container)) throw new Win32Exception(1400, "Окно панели игры пересоздано. Повторите запуск.");
        Detach();
        game = window;
        oldParent = Native.GetParent(game);
        oldStyle = Native.GetWindowLongPtr(game, -16);
        oldExStyle = Native.GetWindowLongPtr(game, -20);
        Native.GetWindowRect(game, out oldRect);
        long style = oldStyle.ToInt64();
        style = (style & ~0x80000000L & ~0x00C00000L & ~0x00040000L) | 0x40000000L | 0x10000000L;
        Native.SetWindowLongPtr(game, -16, (nint)style);
        if (!Native.IsWindow(game)) { game = 0; throw new Win32Exception(1400, "Игра пересоздала окно во время запуска."); }
        Native.SetLastError(0);
        var previous = Native.SetParent(game, container);
        if (previous == 0 && Marshal.GetLastWin32Error() != 0)
        {
            int error = Marshal.GetLastWin32Error();
            Detach();
            throw new Win32Exception(error, "Windows не удалось встроить окно. Игра доступна в отдельном окне.");
        }
        Native.ShowWindow(game, 5);
        ResizeGame();
        FocusGame();
    }

    public void Detach()
    {
        if (IsAttached)
        {
            Native.SetParent(game, oldParent);
            Native.SetWindowLongPtr(game, -16, oldStyle);
            Native.SetWindowLongPtr(game, -20, oldExStyle);
            Native.SetWindowPos(game, 0, oldRect.Left, oldRect.Top, Math.Max(640, oldRect.Right - oldRect.Left), Math.Max(360, oldRect.Bottom - oldRect.Top), 0x0020 | 0x0004);
        }
        game = presentation = 0;
    }

    public void FocusGame()
    {
        if (!IsAttached) return;
        uint ownThread = Native.GetCurrentThreadId();
        uint gameThread = Native.GetWindowThreadProcessId(game, out _);
        bool linked = ownThread != gameThread && Native.AttachThreadInput(ownThread, gameThread, true);
        try
        {
            Native.SetFocus(game);
            // GLFW expects activation messages normally sent to a top-level game window.
            // Reparenting changes that routing; supply them when the user activates this panel.
            Native.SendMessageTimeout(game, 0x0006, 1, 0, 2, 200, out _); // WM_ACTIVATE / WA_ACTIVE
            Native.SendMessageTimeout(game, 0x0007, 0, 0, 2, 200, out _); // WM_SETFOCUS
        }
        finally { if (linked) Native.AttachThreadInput(ownThread, gameThread, false); }
    }

    public async Task SendEscapeAsync()
    {
        if (!IsAttached) throw new InvalidOperationException("Игра в этой панели не запущена.");
        FocusGame();
        Native.PostMessage(game, 0x0100, 0x1B, 0x00010001);
        await Task.Delay(140); // KeyDown must survive at least one complete game update.
        Native.PostMessage(game, 0x0101, 0x1B, unchecked((nint)0xC0010001));
    }

    private void ResizeGame()
    {
        if (!IsAttached) return;
        if (presentation != 0)
        {
            Native.GetClientRect(presentation, out var area);
            int width = area.Right, height = area.Bottom;
            // Letterbox rather than stretch the game or its cursor coordinates.
            int w = Math.Min(width, (int)(height * 16.0 / 9)); int h = (int)(w * 9.0 / 16);
            Native.SetWindowPos(game, 0, (width - w) / 2, (height - h) / 2, w, h, 0x0020 | 0x0004 | 0x4000);
            NotifyViewportSize();
            return;
        }
        if (ActualWidth < 1 || ActualHeight < 1) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        Native.SetWindowPos(game, 0, 0, 0, (int)(ActualWidth * dpi.DpiScaleX), (int)(ActualHeight * dpi.DpiScaleY), 0x0020 | 0x0004 | 0x4000);
        NotifyViewportSize();
    }
    private void NotifyViewportSize()
    {
        var size = ClientSize;
        if (size == lastSize) return;
        lastSize = size; ViewportResized?.Invoke();
    }

    /// <summary>Capture the visible game client. The user brings the workshop to the foreground.</summary>
    public BitmapSource Capture()
    {
        if (!IsAttached) throw new InvalidOperationException("Сначала запустите игру в этой панели.");
        Native.GetClientRect(game, out var rect);
        var point = new Native.Point();
        Native.ClientToScreen(game, ref point);
        int width = rect.Right, height = rect.Bottom;
        if (width < 1 || height < 1) throw new InvalidOperationException("Пустое окно игры.");
        nint screen = Native.GetDC(0), memory = Native.CreateCompatibleDC(screen), bitmap = Native.CreateCompatibleBitmap(screen, width, height);
        nint previous = Native.SelectObject(memory, bitmap);
        try
        {
            if (!Native.BitBlt(memory, 0, 0, width, height, screen, point.X, point.Y, 0x00CC0020)) throw new Win32Exception();
            var image = Imaging.CreateBitmapSourceFromHBitmap(bitmap, 0, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            return image;
        }
        finally
        {
            Native.SelectObject(memory, previous);
            Native.DeleteObject(bitmap);
            Native.DeleteDC(memory);
            Native.ReleaseDC(0, screen);
        }
    }

    protected override nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if ((msg == 0x0007 || (msg == 0x0210 && (wParam.ToInt64() & 0xFFFF) == 0x0201)) && IsAttached) FocusGame();
        return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
    }

    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; }
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern nint CreateWindowEx(uint ex, string cls, string name, uint style, int x, int y, int w, int h, nint parent, nint menu, nint instance, nint param);
        [DllImport("user32.dll")] internal static extern bool DestroyWindow(nint h);
        [DllImport("user32.dll")] internal static extern bool IsWindow(nint h);
        [DllImport("user32.dll")] internal static extern nint GetParent(nint h);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern nint GetWindowLongPtr(nint h, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] internal static extern nint SetWindowLongPtr(nint h, int index, nint value);
        [DllImport("user32.dll", SetLastError = true)] internal static extern nint SetParent(nint child, nint parent);
        [DllImport("user32.dll")] internal static extern bool SetWindowPos(nint h, nint after, int x, int y, int w, int ht, uint flags);
        [DllImport("user32.dll")] internal static extern bool ShowWindow(nint h, int command);
        [DllImport("user32.dll")] internal static extern nint SetFocus(nint h);
        [DllImport("kernel32.dll")] internal static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint h, out uint process);
        [DllImport("user32.dll")] internal static extern bool AttachThreadInput(uint ownThread, uint otherThread, bool attach);
        [DllImport("user32.dll")] internal static extern nint SendMessageTimeout(nint h, uint message, nint wparam, nint lparam, uint flags, uint timeout, out nint result);
        [DllImport("user32.dll")] internal static extern bool PostMessage(nint h, uint message, nint wparam, nint lparam);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(nint h, out Rect rect);
        [DllImport("user32.dll")] internal static extern bool GetClientRect(nint h, out Rect rect);
        [DllImport("user32.dll")] internal static extern bool ClientToScreen(nint h, ref Point point);
        [DllImport("kernel32.dll")] internal static extern void SetLastError(uint error);
        [DllImport("user32.dll")] internal static extern nint GetDC(nint h);
        [DllImport("user32.dll")] internal static extern int ReleaseDC(nint h, nint dc);
        [DllImport("gdi32.dll")] internal static extern nint CreateCompatibleDC(nint dc);
        [DllImport("gdi32.dll")] internal static extern nint CreateCompatibleBitmap(nint dc, int w, int h);
        [DllImport("gdi32.dll")] internal static extern nint SelectObject(nint dc, nint obj);
        [DllImport("gdi32.dll")] internal static extern bool BitBlt(nint target, int x, int y, int w, int h, nint source, int sx, int sy, uint mode);
        [DllImport("gdi32.dll")] internal static extern bool DeleteObject(nint obj);
        [DllImport("gdi32.dll")] internal static extern bool DeleteDC(nint dc);
    }
}

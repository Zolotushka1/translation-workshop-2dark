using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using Workshop.Core;

namespace Workshop.App;

/// <summary>Mirror physical input only while one of the two embedded game viewports owns it.</summary>
public sealed class LinkedInput : IDisposable
{
    private readonly GameHost english, russian;
    private readonly Func<bool> canForward;
    private readonly Native.Hook keyboardCallback, mouseCallback;
    private readonly HashSet<int> heldKeys = [];
    private readonly HashSet<int> shortcutKeys = [];
    private nint keyboardHook, mouseHook, dragSource;
    private uint buttons;
    private readonly Dictionary<int, long> keyDownAt = [];
    private readonly Dictionary<uint, long> mouseDownAt = [];
    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    private readonly byte[] engineKeys = new byte[512];
    private double mouseX = .5, mouseY = .5;
    public Action<byte[], double, double, bool>? InputChanged { get; set; }
    public Func<int, bool>? Shortcut { get; set; }
    public bool Enabled { get; set; }
    public bool EngineOnly { get; set; }

    public LinkedInput(GameHost english, GameHost russian, Func<bool> canForward)
    {
        this.english = english; this.russian = russian; this.canForward = canForward;
        keyboardCallback = Keyboard; mouseCallback = Mouse;
        keyboardHook = Native.SetWindowsHookEx(13, keyboardCallback, 0, 0);
        mouseHook = Native.SetWindowsHookEx(14, mouseCallback, 0, 0);
        if (keyboardHook == 0 || mouseHook == 0) { Dispose(); throw new Win32Exception(); }
    }
    private bool Active => Enabled && english.IsAttached && russian.IsAttached && canForward();
    private nint Other(nint source) => source == english.GameWindow ? russian.GameWindow : source == russian.GameWindow ? english.GameWindow : 0;
    private nint FocusedGame()
    {
        var info = new Native.GuiThread { Size = Marshal.SizeOf<Native.GuiThread>() };
        return Native.GetGUIThreadInfo(0, ref info) && Other(info.Focus) != 0 ? info.Focus : 0;
    }
    private nint Keyboard(int code, nint message, nint data)
    {
        if (code >= 0)
        {
            int vk = (int)Marshal.PtrToStructure<Native.KeyboardData>(data).VirtualKey;
            bool up = message is 0x101 or 0x105;
            if (shortcutKeys.Contains(vk)) { if (up) shortcutKeys.Remove(vk); return 1; }
            if (!up && Shortcut?.Invoke(vk) == true) { shortcutKeys.Add(vk); return 1; }
        }
        if (code >= 0 && Active)
        {
            var key = Marshal.PtrToStructure<Native.KeyboardData>(data);
            nint target = Other(FocusedGame());
            int vk = (int)key.VirtualKey;
            bool up = message is 0x101 or 0x105;
            if (target != 0 || up && heldKeys.Contains(vk))
            {
                long flags = 1L | ((long)key.Scan << 16) | ((key.Flags & 1) != 0 ? 1L << 24 : 0) | (up ? 3L << 30 : 0);
                nint[] games = [english.GameWindow, russian.GameWindow];
                if (up)
                {
                    heldKeys.Remove(vk);
                    int delay = (int)Math.Max(0, 90 - (Environment.TickCount64 - keyDownAt.GetValueOrDefault(vk)));
                    _ = ReleaseLater(games, (uint)message, (nint)vk, (nint)flags, delay);
                    _ = ReleaseEngineKey(vk, delay);
                }
                else
                {
                    heldKeys.Add(vk); keyDownAt[vk] = Environment.TickCount64;
                    if (vk < engineKeys.Length && vk != 0xE7) engineKeys[vk] = 1;
                    InputChanged?.Invoke((byte[])engineKeys.Clone(), mouseX, mouseY, false);
                    foreach (nint game in games)
                    {
                        if (vk == 0xE7) Native.PostMessage(game, 0x102, (nint)key.Scan, 1); // Unicode SendInput / WM_CHAR
                        else if (!EngineOnly) Native.PostMessage(game, (uint)message, (nint)vk, (nint)flags);
                    }
                }
                return 1;
            }
        }
        return Native.CallNextHookEx(0, code, message, data);
    }
    private nint Mouse(int code, nint message, nint data)
    {
        if (code >= 0 && Active)
        {
            var mouse = Marshal.PtrToStructure<Native.MouseData>(data);
            uint msg = (uint)message;
            nint source = dragSource;
            if (source == 0)
                foreach (nint game in new[] { english.GameWindow, russian.GameWindow })
                    if (Native.IsWindowVisible(game) && Native.GetAncestor(game, 2) == Native.GetForegroundWindow() && Native.GetWindowRect(game, out var r) && mouse.Point.X >= r.Left && mouse.Point.X < r.Right && mouse.Point.Y >= r.Top && mouse.Point.Y < r.Bottom) { source = game; break; }
            nint target = Other(source);
            // Do not mirror clicks through another foreground application covering the workshop.
            if (target != 0 && Native.GetAncestor(source, 2) == Native.GetForegroundWindow())
            {
                if (msg is 0x201 or 0x204 or 0x207) { dragSource = source; buttons |= msg == 0x201 ? 1u : msg == 0x204 ? 2u : 0x10u; }
                if (msg is 0x202 or 0x205 or 0x208) { buttons &= ~(msg == 0x202 ? 1u : msg == 0x205 ? 2u : 0x10u); if (buttons == 0) dragSource = 0; }
                Native.GetWindowRect(source, out var from); Native.GetClientRect(target, out var to);
                (mouseX, mouseY) = ViewportPointer.Normalize(mouse.Point.X - from.Left, mouse.Point.Y - from.Top, from.Right - from.Left, from.Bottom - from.Top);
                var targetPoint = ViewportPointer.Pixels(mouseX, mouseY, to.Right, to.Bottom);
                int x = (int)Math.Round(targetPoint.X), y = (int)Math.Round(targetPoint.Y);
                uint wparam = buttons;
                if (msg == 0x20A) { var point = new Native.Point { X = x, Y = y }; Native.ClientToScreen(target, ref point); x = point.X; y = point.Y; wparam |= mouse.Data & 0xFFFF0000; }
                nint param = (nint)((y & 0xFFFF) << 16 | (x & 0xFFFF));
                if (msg is 0x201 or 0x204 or 0x207)
                {
                    if (source == english.GameWindow) english.FocusGame(); else russian.FocusGame();
                    int engineKey = msg == 0x201 ? 384 : msg == 0x204 ? 385 : 386;
                    engineKeys[engineKey] = 1; if (engineKey == 384) engineKeys[1] = 1;
                    InputChanged?.Invoke((byte[])engineKeys.Clone(), mouseX, mouseY, false);
                    mouseDownAt[msg] = Environment.TickCount64;
                    if (EngineOnly) return 1;
                    // Deliver both sides through the same path, with a minimum press duration.
                    Native.PostMessage(target, 0x200, (nint)wparam, param);
                    if (!EngineOnly || msg == 0x20A) Native.PostMessage(target, msg, (nint)wparam, param);
                    nint originalPoint = (nint)(((mouse.Point.Y - from.Top) & 0xFFFF) << 16 | ((mouse.Point.X - from.Left) & 0xFFFF));
                    Native.PostMessage(source, msg, (nint)wparam, originalPoint);
                    return 1;
                }
                if (msg is 0x202 or 0x205 or 0x208)
                {
                    int delay = (int)Math.Max(0, 120 - (Environment.TickCount64 - mouseDownAt.GetValueOrDefault(msg - 1)));
                    _ = ReleaseLater([target], msg, (nint)wparam, param, delay);
                    nint originalPoint = (nint)(((mouse.Point.Y - from.Top) & 0xFFFF) << 16 | ((mouse.Point.X - from.Left) & 0xFFFF));
                    _ = ReleaseLater([source], msg, (nint)wparam, originalPoint, delay);
                    _ = ReleaseEngineKey(msg == 0x202 ? 384 : msg == 0x205 ? 385 : 386, delay);
                    return 1;
                }
                if (!EngineOnly || msg == 0x20A) Native.PostMessage(target, msg, (nint)wparam, param);
                InputChanged?.Invoke((byte[])engineKeys.Clone(), mouseX, mouseY, false);
            }
        }
        return Native.CallNextHookEx(0, code, message, data);
    }
    private async Task ReleaseEngineKey(int key, int milliseconds)
    {
        await Task.Delay(milliseconds);
        await dispatcher.InvokeAsync(() =>
        {
            if (key < engineKeys.Length) engineKeys[key] = 0;
            if (key == 384) engineKeys[1] = 0;
            InputChanged?.Invoke((byte[])engineKeys.Clone(), mouseX, mouseY, false);
        });
    }
    private async Task ReleaseLater(nint[] games, uint message, nint wparam, nint lparam, int milliseconds)
    {
        await Task.Delay(milliseconds);
        await dispatcher.InvokeAsync(() => { foreach (nint game in games.Where(x => x != 0 && !EngineOnly)) Native.PostMessage(game, message, wparam, lparam); });
    }
    public void Reset()
    {
        foreach (nint game in new[] { english.GameWindow, russian.GameWindow }.Where(x => x != 0 && !EngineOnly))
        {
            foreach (int key in heldKeys) Native.PostMessage(game, 0x101, (nint)key, unchecked((nint)0xC0000001));
            foreach (uint message in new uint[] { 0x202, 0x205, 0x208 }) Native.PostMessage(game, message, 0, 0);
        }
        heldKeys.Clear(); dragSource = 0; buttons = 0;
        Array.Clear(engineKeys); InputChanged?.Invoke((byte[])engineKeys.Clone(), mouseX, mouseY, true);
    }
    public void Dispose()
    {
        Reset();
        InputChanged = null;
        if (keyboardHook != 0) Native.UnhookWindowsHookEx(keyboardHook);
        if (mouseHook != 0) Native.UnhookWindowsHookEx(mouseHook);
        keyboardHook = mouseHook = 0;
    }
    private static class Native
    {
        internal delegate nint Hook(int code, nint wparam, nint lparam);
        [StructLayout(LayoutKind.Sequential)] internal struct Point { internal int X, Y; }
        [StructLayout(LayoutKind.Sequential)] internal struct Rect { internal int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] internal struct KeyboardData { internal uint VirtualKey, Scan, Flags, Time; internal nuint Extra; }
        [StructLayout(LayoutKind.Sequential)] internal struct MouseData { internal Point Point; internal uint Data, Flags, Time; internal nuint Extra; }
        [StructLayout(LayoutKind.Sequential)] internal struct GuiThread { internal int Size; internal uint Flags; internal nint Active, Focus, Capture, MenuOwner, MoveSize, Caret; internal Rect CaretRect; }
        [DllImport("user32.dll", SetLastError = true)] internal static extern nint SetWindowsHookEx(int type, Hook callback, nint module, uint thread);
        [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(nint hook);
        [DllImport("user32.dll")] internal static extern nint CallNextHookEx(nint hook, int code, nint wparam, nint lparam);
        [DllImport("user32.dll")] internal static extern bool PostMessage(nint window, uint message, nint wparam, nint lparam);
        [DllImport("user32.dll")] internal static extern bool GetGUIThreadInfo(uint thread, ref GuiThread info);
        [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint window);
        [DllImport("user32.dll")] internal static extern nint GetAncestor(nint window, uint flag);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(nint window, out Rect rect);
        [DllImport("user32.dll")] internal static extern bool GetClientRect(nint window, out Rect rect);
        [DllImport("user32.dll")] internal static extern bool ClientToScreen(nint window, ref Point point);
    }
}

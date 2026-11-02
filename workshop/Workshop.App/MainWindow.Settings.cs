using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Workshop.Core;

namespace Workshop.App;

public partial class MainWindow
{
    private WorkshopSettings settings = new();
    private FullscreenWindow? fullscreen;
    private readonly GameAudio audio = new();
    private readonly DispatcherTimer audioTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer inputTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly DispatcherTimer viewportTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly HashSet<GameSession> resizedViewports = [];
    private (byte[] Keys, double X, double Y, bool Release)? pendingInput;
    private long lastShortcut;
    private string lastAudioState = "";
    private string SettingsPath => Path.Combine(corpus.ResearchRoot, "workshop-settings.json");
    private void InitializeSettings()
    {
        void Resize(GameSession session) { resizedViewports.Add(session); viewportTimer.Stop(); viewportTimer.Start(); }
        EnglishHost.ViewportResized += () => Resize(english);
        RussianHost.ViewportResized += () => Resize(russian);
        viewportTimer.Tick += async (_, _) =>
        {
            if (busy || applyingLive) return;
            viewportTimer.Stop();
            var sessions = resizedViewports.Where(x => x.Running).ToArray(); resizedViewports.Clear();
            if (sessions.Length > 0) await ExecuteAsync(async () =>
            {
                foreach (var session in sessions) await session.Control.RefreshUiAsync();
            });
        };
        try { settings = WorkshopSettings.Load(SettingsPath); }
        catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException) { StatusText = "Настройки не прочитаны: " + ex.Message; }
        linkedInput.Shortcut = vk =>
        {
            nint foreground = SettingsNative.GetForegroundWindow();
            SettingsNative.GetWindowThreadProcessId(foreground, out uint pid);
            if (pid != Environment.ProcessId && pid != english.ProcessId && pid != russian.ProcessId) return false;
            // Modal settings/file dialogs keep their own keyboard navigation.
            if (OwnedWindows.Cast<Window>().Any(x => x.IsActive)) return false;
            bool toggle = KeyInterop.KeyFromVirtualKey(vk).ToString() == settings.FullscreenKey;
            bool change = fullscreen is not null && KeyInterop.KeyFromVirtualKey(vk).ToString() == settings.SwitchVersionKey;
            if (!toggle && !change) return false;
            if (Environment.TickCount64 - lastShortcut < 300) return true;
            lastShortcut = Environment.TickCount64;
            Dispatcher.BeginInvoke(() => { if (Ready) { if (toggle) ToggleFullscreen(); else SwitchFullscreen(); } });
            return true;
        };
        inputTimer.Tick += (_, _) =>
        {
            if (frameSync is { Active: true } sync)
            {
                sync.SetSizes(EnglishHost.ClientSize, RussianHost.ClientSize);
                if (pendingInput is { } shared) { sync.SetInput(shared.Keys, shared.X, shared.Y); pendingInput = null; }
                return;
            }
            if (applyingLive || pendingInput is not { } packet) return;
            pendingInput = null;
            try
            {
                foreach (var (session, host) in new[] { (english, EnglishHost), (russian, RussianHost) })
                    if (session.Running && host.IsAttached)
                    {
                        var size = host.ClientSize;
                        var point = ViewportPointer.Pixels(packet.X, packet.Y, size.Width, size.Height);
                        session.Control.LinkedInput(packet.Keys, point.X, point.Y, packet.Release);
                    }
            }
            catch (Exception ex) { StatusText = "Общее управление: " + ex.Message; }
        };
        inputTimer.Start();
        audioTimer.Tick += (_, _) => UpdateAudio(); audioTimer.Start();
    }
    private void UpdateAudio()
    {
        if (!english.Running && !russian.Running) return;
        try
        {
            audio.Apply(english.ProcessId, russian.ProcessId, settings.SingleAudioSource, settings.AudioSource == "RU", english.Paused, russian.Paused);
            string state = System.Text.Json.JsonSerializer.Serialize(new { settings.SingleAudioSource, settings.AudioSource, english = english.ProcessId, russian = russian.ProcessId, muted = audio.LastStates });
            if (state != lastAudioState)
            {
                BuildService.WriteAtomic(Path.Combine(corpus.ResearchRoot, "logs", "audio-state.json"), System.Text.Encoding.UTF8.GetBytes(state)); lastAudioState = state;
            }
        }
        catch (COMException ex) { StatusText = "Звук тестовых копий: " + ex.Message; }
    }
    private void RestoreAudio()
    {
        try { audio.Restore(english.ProcessId, russian.ProcessId); }
        catch (COMException ex) { StatusText = "Возврат звука: " + ex.Message; }
    }
    private void OpenSettings(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(this, settings);
        if (dialog.ShowDialog() != true) return;
        WorkshopSettings.Save(SettingsPath, dialog.Settings); settings = dialog.Settings;
        if (fullscreen is not null) fullscreen.ShowGame(fullscreen.Russian ? RussianHost : EnglishHost, fullscreen.Russian, settings);
        UpdateAudio(); StatusText = $"Настройки сохранены. {settings.FullscreenKey} — полный экран, {settings.SwitchVersionKey} — EN/RU в полном экране.";
    }
    private void ShowEnglishFullscreen(object sender, RoutedEventArgs e) => EnterFullscreen(false);
    private void ShowRussianFullscreen(object sender, RoutedEventArgs e) => EnterFullscreen(true);
    private void ToggleFullscreen()
    {
        if (fullscreen is not null) { fullscreen.Close(); return; }
        EnterFullscreen(russian.Running);
    }
    private void EnterFullscreen(bool ru)
    {
        if (!(ru ? RussianHost : EnglishHost).IsAttached) { StatusText = "Сначала запустите выбранную копию игры."; return; }
        linkedInput.Reset();
        fullscreen ??= new FullscreenWindow(this, settings);
        fullscreen.Closed -= FullscreenClosed; fullscreen.Closed += FullscreenClosed;
        fullscreen.ShowGame(ru ? RussianHost : EnglishHost, ru, settings);
        StatusText = $"{settings.FullscreenKey} — вернуться к редактору; {settings.SwitchVersionKey} — EN/RU.";
    }
    private void SwitchFullscreen()
    {
        if (fullscreen is null) return;
        bool ru = !fullscreen.Russian;
        if (!(ru ? RussianHost : EnglishHost).IsAttached) { StatusText = "Вторая копия ещё не запущена."; return; }
        linkedInput.Reset(); fullscreen.ShowGame(ru ? RussianHost : EnglishHost, ru, settings);
    }
    private void FullscreenClosed(object? sender, EventArgs e) { fullscreen = null; Activate(); }
    private static class SettingsNative
    {
        [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    }
}

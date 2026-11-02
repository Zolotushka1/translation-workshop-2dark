using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;
using Workshop.Core;

namespace Workshop.App;

public sealed class GameSession(GameHost host, string directory)
{
    private Process? process;
    private RuntimeControl? control;
    public string Directory { get; } = directory;
    public bool Running => process is not null && !process.HasExited;
    public bool Paused => Running && control?.Suspended == true;
    public int? ProcessId => Running ? process!.Id : null;
    public bool HasExistingProcess
    {
        get { using var existing = FindExisting(Path.Combine(Directory, "2Dark.exe")); return existing is not null; }
    }
    public RuntimeControl Control => Running ? control ??= new RuntimeControl(process!, Path.Combine(Directory, "2Dark.exe"))
        : throw new InvalidOperationException("Сначала запустите тестовую игру.");
    public void SetPaused(bool pause) { if (pause) Control.Pause(); else Control.Resume(); }

    public async Task StartAsync(string directory)
    {
        if (Running && host.IsAttached) { host.FocusGame(); return; }
        if (!Path.GetFullPath(directory).Equals(Path.GetFullPath(Directory), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Неожиданная папка тестового процесса.");
        string executable = Path.Combine(directory, "2Dark.exe");
        control?.Dispose(); control = null;
        process = FindExisting(executable);
        if (process is null)
        {
            var info = new ProcessStartInfo(executable) { WorkingDirectory = directory, UseShellExecute = false };
            // The game's native Monkey/GLFW runtime resolves monkey://internal/ through APPDATA.
            // Separate EN/RU state, settings and saves from each other and from the installed game's profile.
            string profile = GameProfile.Prepare(directory);
            info.Environment["APPDATA"] = profile;
            info.Environment["SteamAppId"] = "435100";
            info.Environment["SteamGameId"] = "435100";
            process = Process.Start(info) ?? throw new InvalidOperationException("Не удалось запустить игру.");
        }
        nint lastWindow = 0;
        int stableTicks = 0;
        for (int i = 0; i < 250; i++)
        {
            await Task.Delay(100);
            process.Refresh();
            if (process.HasExited) throw new InvalidOperationException("Игра завершилась до открытия окна. Возможно, второй экземпляр не поддерживается или требуется Steam. Проверьте запуск тестовых копий.");
            nint window = process.MainWindowHandle;
            stableTicks = window != 0 && window == lastWindow ? stableTicks + 1 : 0;
            lastWindow = window;
            if (stableTicks >= 15 && process.MainWindowTitle == "2Dark")
            {
                Control.InitializeBridge();
                try { host.Attach(window); }
                catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1400) { stableTicks = 0; continue; }
                return;
            }
        }
        throw new TimeoutException("Не удалось подключить стабильное окно игры за 25 секунд. Проверьте отдельное окно игры или Steam.");
    }

    public async Task StopAsync()
    {
        control?.Dispose(); control = null;
        if (!Running) process = FindExisting(Path.Combine(Directory, "2Dark.exe"));
        if (!Running) { host.Detach(); return; }
        host.Detach();
        process!.Refresh();
        process.CloseMainWindow();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { if (!process.HasExited) { process.Kill(); await process.WaitForExitAsync(); } }
        process.Dispose();
        process = null;
    }

    public BitmapSource Capture() => host.Capture();
    public void Detach() { control?.Resume(); host.Detach(); }
    public void Release() { control?.Dispose(); control = null; host.Detach(); }

    private static Process? FindExisting(string executable)
    {
        Process? found = null;
        foreach (var candidate in Process.GetProcessesByName("2Dark"))
        {
            try { if (found is null && candidate.MainModule?.FileName?.Equals(executable, StringComparison.OrdinalIgnoreCase) == true) { found = candidate; continue; } }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
            candidate.Dispose();
        }
        return found;
    }
}

using System.IO;
using System.Windows;
using System.Security.Cryptography;
using System.Text;

namespace Workshop.App;

public partial class App : Application
{
    private Mutex? sessionMutex;
    private bool ownsSession;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            Log(args.Exception);
            MessageBox.Show(args.Exception.Message, "Мастерская 2Dark", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        try
        {
            string root = FindRoot();
            string identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root.ToUpperInvariant())))[..16];
            sessionMutex = new Mutex(true, "Local\\2DarkWorkshop-" + identity, out ownsSession);
            if (!ownsSession)
            {
                MessageBox.Show("Мастерская для этой папки уже открыта.", "2Dark", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }
            string? project = null;
            int index = Array.IndexOf(e.Args, "--project");
            if (index >= 0 && index + 1 < e.Args.Length) project = Path.GetFullPath(e.Args[index + 1]);
            var window = new MainWindow(root, project);
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            Log(ex);
            MessageBox.Show(ex.Message, "Не удалось открыть мастерскую", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (ownsSession) sessionMutex?.ReleaseMutex();
        sessionMutex?.Dispose();
        base.OnExit(e);
    }

    private static string FindRoot()
    {
        foreach (string start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
            for (DirectoryInfo? dir = new(start); dir is not null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "corpus_inventory.json")) && Directory.Exists(Path.Combine(dir.FullName, "original"))) return dir.FullName;
        throw new DirectoryNotFoundException("Не найдена _localization_research с извлечёнными текстами. Запустите приложение из папки проекта.");
    }

    private static void Log(Exception error)
    {
        try { File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "workshop-errors.log"), $"{DateTime.Now:O} {error}\n"); } catch (IOException) { }
    }
}
